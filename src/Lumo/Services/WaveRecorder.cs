using System.Runtime.InteropServices;

namespace Lumo.Services;

/// <summary>
/// v2.6.0-alpha.4 — microphone capture for record-then-transcribe voice typing,
/// straight on the winmm waveIn API that has shipped in Windows since forever:
/// no NAudio, no MediaFoundation, no extra byte in the single exe. Records
/// 16 kHz / 16-bit / mono PCM (the desktop recognizer's home format) into
/// memory via four rotating 250 ms buffers, hands the raw PCM back when the
/// clip is finished, and refuses to grow past <see cref="MaxSeconds"/> so a
/// forgotten mic can't balloon memory.
///
/// Threading: Start/StopAndRead run on the caller (UI) thread; a background
/// pump thread drains the CALLBACK_EVENT signal and copies finished buffers
/// under a lock. StopAndRead resets the device, joins the pump, then sweeps
/// whatever is still marked done — a <c>_owned</c> flag per buffer makes the
/// handoff double-drain-proof. Every winmm failure returns a readable reason
/// string from Start; nothing throws across the API.
/// </summary>
internal sealed class WaveRecorder : IDisposable
{
    public const int SampleRate = 16000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;

    private const int BufferMs = 250;
    private const int BufferCount = 4;

    /// <summary>Recording hard stop — a dictation prompt does not need more than this.</summary>
    public const int MaxSeconds = 60;

    private const int WaveMapper = -1;
    private const int CallbackEvent = 0x00050000;
    private const int WhdrDone = 0x00000001;

    private const int MmsyserrBaddeviceId = 2;
    private const int MmsyserrAllocated = 4;
    private const int MmsyserrNoDriver = 7;
    private const int WaverrBadFormat = 32;

    private IntPtr _dev;
    private readonly IntPtr[] _headers = new IntPtr[BufferCount];
    private readonly IntPtr[] _datas = new IntPtr[BufferCount];
    private readonly bool[] _owned = new bool[BufferCount];   // buffer is queued in the device
    private readonly List<byte[]> _chunks = new();
    private readonly object _gate = new();

    private AutoResetEvent? _callback;
    private Thread? _pump;
    private volatile bool _stopping;
    private long _totalBytes;
    private bool _limitHit;
    private bool _opened;

    private readonly int _bufferBytes = SampleRate / 1000 * BufferMs * (BitsPerSample / 8) * Channels;   // 8000
    private readonly long _limitBytes = (long)SampleRate * (BitsPerSample / 8) * Channels * MaxSeconds;

    public bool IsRecording { get; private set; }

    /// <summary>Raised once, from the pump thread, when MaxSeconds of audio has been captured.</summary>
    public event Action? LimitReached;

    /// <summary>
    /// Opens the default capture device and starts streaming. Returns null on
    /// success, otherwise a human-readable reason for the mic button tooltip.
    /// </summary>
    public string? Start()
    {
        if (IsRecording) return null;
        try
        {
            var fmt = new WAVEFORMATEX
            {
                wFormatTag = 1,   // PCM
                nChannels = Channels,
                nSamplesPerSec = SampleRate,
                wBitsPerSample = BitsPerSample,
                nBlockAlign = Channels * BitsPerSample / 8,
            };
            fmt.nAvgBytesPerSec = fmt.nSamplesPerSec * fmt.nBlockAlign;

            _callback = new AutoResetEvent(false);
            int mmr = waveInOpen(out _dev, WaveMapper, ref fmt,
                _callback.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent);
            if (mmr != 0) return MmrReason(mmr);

            for (int i = 0; i < BufferCount; i++)
            {
                _datas[i] = Marshal.AllocHGlobal(_bufferBytes);
                var hdr = new WAVEHDR { lpData = _datas[i], dwBufferLength = (uint)_bufferBytes };
                _headers[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
                Marshal.StructureToPtr(hdr, _headers[i], false);
                mmr = waveInPrepareHeader(_dev, _headers[i], Marshal.SizeOf<WAVEHDR>());
                if (mmr != 0) return MmrReason(mmr);
                mmr = waveInAddBuffer(_dev, _headers[i], Marshal.SizeOf<WAVEHDR>());
                if (mmr != 0) return MmrReason(mmr);
                _owned[i] = true;
            }

            _stopping = false;
            _totalBytes = 0;
            _limitHit = false;
            _opened = true;
            _pump = new Thread(Pump) { IsBackground = true, Name = "Lumo.VoiceCapture" };
            _pump.Start();

            mmr = waveInStart(_dev);
            if (mmr != 0) return MmrReason(mmr);

            IsRecording = true;
            return null;
        }
        catch (Exception ex)
        {
            return "Microphone could not start: " + ex.Message;
        }
    }

    /// <summary>
    /// Finishes the clip and returns the captured raw PCM (headerless 16-bit LE
    /// mono), or null when nothing usable was captured / the clip never started.
    /// Blocks briefly — waveInReset plus a pump join is milliseconds in practice.
    /// </summary>
    public byte[]? StopAndRead()
    {
        if (!IsRecording || !_opened) return null;
        IsRecording = false;

        _stopping = true;
        try { waveInReset(_dev); } catch { }
        try { _pump?.Join(1500); } catch { }

        // sweep whatever the pump didn't get to — reset marks every queued buffer done
        Drain();

        var pcm = ReadOut();
        ReleaseDevice();
        return pcm;
    }

    /// <summary>Background pump: copy every finished buffer, then hand it back to the device.</summary>
    private void Pump()
    {
        while (!_stopping)
        {
            try { _callback?.WaitOne(100); } catch { return; }
            if (_stopping) break;      // the final sweep belongs to StopAndRead
            Drain(requeue: true);
        }
    }

    private void Drain(bool requeue = false)
    {
        lock (_gate)
        {
            for (int i = 0; i < BufferCount; i++)
            {
                if (!_owned[i]) continue;
                var hdr = Marshal.PtrToStructure<WAVEHDR>(_headers[i]);
                if ((hdr.dwFlags & WhdrDone) == 0) continue;

                int recorded = (int)hdr.dwBytesRecorded;
                if (recorded > 0)
                {
                    var buf = new byte[recorded];
                    Marshal.Copy(_datas[i], buf, 0, recorded);
                    _chunks.Add(buf);
                    _totalBytes += recorded;
                }
                _owned[i] = false;

                if (requeue && !_stopping)
                {
                    try { waveInAddBuffer(_dev, _headers[i], Marshal.SizeOf<WAVEHDR>()); _owned[i] = true; }
                    catch { }
                }
            }

            if (!_limitHit && _totalBytes >= _limitBytes)
            {
                _limitHit = true;
                try { LimitReached?.Invoke(); } catch { }
            }
        }
    }

    private byte[]? ReadOut()
    {
        lock (_gate)
        {
            if (_chunks.Count == 0 || _totalBytes == 0) return null;
            var pcm = new byte[_totalBytes];
            int at = 0;
            foreach (var c in _chunks)
            {
                Buffer.BlockCopy(c, 0, pcm, at, c.Length);
                at += c.Length;
            }
            return at == pcm.Length ? pcm : pcm[..at];
        }
    }

    private void ReleaseDevice()
    {
        if (_opened)
        {
            for (int i = 0; i < BufferCount; i++)
            {
                if (_headers[i] != IntPtr.Zero)
                {
                    try { waveInUnprepareHeader(_dev, _headers[i], Marshal.SizeOf<WAVEHDR>()); } catch { }
                }
            }
            try { waveInClose(_dev); } catch { }
            _opened = false;
        }
        _dev = IntPtr.Zero;
        for (int i = 0; i < BufferCount; i++)
        {
            if (_datas[i] != IntPtr.Zero) { Marshal.FreeHGlobal(_datas[i]); _datas[i] = IntPtr.Zero; }
            if (_headers[i] != IntPtr.Zero) { Marshal.FreeHGlobal(_headers[i]); _headers[i] = IntPtr.Zero; }
        }
        try { _callback?.Dispose(); } catch { }
        _callback = null;
    }

    /// <summary>winmm error codes are numbers — the three the user can actually act on get words.</summary>
    private static string MmrReason(int mmr) => mmr switch
    {
        MmsyserrAllocated => "The microphone is in use by another application — close it and try again.",
        MmsyserrBaddeviceId or MmsyserrNoDriver => "No microphone found — plug one in or check Sound settings.",
        WaverrBadFormat => "This microphone does not support 16 kHz capture.",
        _ => $"Microphone error (winmm 0x{mmr:X}).",
    };

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveInOpen(out IntPtr handle, int deviceId, ref WAVEFORMATEX format,
        IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int waveInStart(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int waveInReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int waveInClose(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int waveInPrepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveInUnprepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveInAddBuffer(IntPtr handle, IntPtr header, int size);

    public void Dispose()
    {
        if (IsRecording) StopAndRead();
        ReleaseDevice();
    }
}
