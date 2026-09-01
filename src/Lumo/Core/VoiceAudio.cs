using System.Buffers.Binary;

namespace Lumo.Core;

/// <summary>
/// v2.6.0-alpha.4 — pure audio policy for record-then-transcribe voice typing:
/// how a raw PCM capture is wrapped as a WAV file (System.Speech wants a stream
/// with a real RIFF header) and where the spoken part of a clip actually starts
/// and ends. The trimming matters for accuracy: feeding the recognizer long
/// stretches of room tone makes SAPI hallucinate filler words at both edges of
/// the utterance ("the", "uh"), which is exactly the garbage the live-dictation
/// build was criticized for. Deliberately free of any SAPI / winmm dependency
/// so it stays unit-testable on the Linux dev box and in the net8.0 test target.
/// The Windows capture side lives in Services/WaveRecorder, the transcription
/// side in Services/VoiceInputService.
/// </summary>
public static class VoiceAudio
{
    /// <summary>Capture format the recorder and the transcription stage agree on.</summary>
    public const int SampleRate = 16000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;

    /// <summary>Size of the canonical 44-byte PCM WAV header this class writes.</summary>
    public const int WavHeaderBytes = 44;

    /// <summary>
    /// Wraps 16-bit PCM data in a canonical 44-byte RIFF/WAVE header, little-endian
    /// throughout — the exact file shape <see cref="System.Speech.Recognition.SpeechRecognitionEngine.SetInputToWaveFile"/>
    /// expects. No chunks beyond fmt/data; no metadata.
    /// </summary>
    public static byte[] BuildWav(ReadOnlySpan<byte> pcm, int sampleRate = SampleRate,
        int channels = Channels, int bitsPerSample = BitsPerSample)
    {
        int blockAlign = channels * bitsPerSample / 8;
        int byteRate = sampleRate * blockAlign;
        var wav = new byte[WavHeaderBytes + pcm.Length];

        wav[0] = (byte)'R'; wav[1] = (byte)'I'; wav[2] = (byte)'F'; wav[3] = (byte)'F';
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4), (uint)(36 + pcm.Length));   // RIFF chunk size
        wav[8] = (byte)'W'; wav[9] = (byte)'A'; wav[10] = (byte)'V'; wav[11] = (byte)'E';

        wav[12] = (byte)'f'; wav[13] = (byte)'m'; wav[14] = (byte)'t'; wav[15] = (byte)' ';
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);                        // fmt chunk length
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);                         // PCM
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), (short)bitsPerSample);

        wav[36] = (byte)'d'; wav[37] = (byte)'a'; wav[38] = (byte)'t'; wav[39] = (byte)'a';
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), pcm.Length);                // data chunk size

        pcm.CopyTo(wav.AsSpan(WavHeaderBytes));
        return wav;
    }

    /// <summary>
    /// Locates the spoken part of a 16-bit little-endian mono clip. Splits the clip
    /// into fixed windows, scores each by RMS loudness, and returns the byte range
    /// from the first window above <paramref name="threshold"/> back-padded by
    /// <paramref name="padMs"/>, through the last such window forward-padded the
    /// same way — clamped to the clip. Returns null when nothing clears the
    /// threshold (pure silence, muted mic) so the caller can fail with a helpful
    /// message instead of making the recognizer chew on room tone.
    /// </summary>
    /// <param name="threshold">RMS loudness (0..32767 scale) a window must clear to count as speech.</param>
    /// <param name="padMs">Breathing room kept around the detected speech.</param>
    /// <param name="windowMs">Analysis window — small enough to find edges, large enough for stable RMS.</param>
    public static (int Start, int End)? TrimSilence(ReadOnlySpan<byte> pcm16Le, int sampleRate = SampleRate,
        int threshold = 320, int padMs = 220, int windowMs = 20)
    {
        int windowSamples = Math.Max(1, sampleRate / 1000 * windowMs);
        int bytesPerSample = 2;
        int windowBytes = windowSamples * bytesPerSample;

        int firstWindow = -1, lastWindow = -1, windowCount = 0;
        for (int offset = 0; offset < pcm16Le.Length; offset += windowBytes, windowCount++)
        {
            int take = Math.Min(windowBytes, pcm16Le.Length - offset);
            if (Rms(pcm16Le.Slice(offset, take)) >= threshold)
            {
                if (firstWindow < 0) firstWindow = windowCount;
                lastWindow = windowCount;
            }
        }

        if (firstWindow < 0) return null;

        int padBytes = Math.Max(0, sampleRate / 1000 * padMs) * bytesPerSample;
        int start = Math.Max(0, firstWindow * windowBytes - padBytes);
        int end = Math.Min(pcm16Le.Length, (lastWindow + 1) * windowBytes + padBytes);
        if (start >= end) return null;
        return (start, end);
    }

    /// <summary>RMS loudness of a run of 16-bit little-endian samples (odd tails tolerated).</summary>
    public static double Rms(ReadOnlySpan<byte> pcm16Le)
    {
        long sum = 0;
        int samples = pcm16Le.Length / 2;
        if (samples == 0) return 0;
        for (int i = 0; i + 1 < pcm16Le.Length; i += 2)
        {
            short s = (short)(pcm16Le[i] | (pcm16Le[i + 1] << 8));
            sum += (long)s * s;
        }
        return Math.Sqrt((double)sum / samples);
    }

    /// <summary>
    /// v2.6.0-alpha.5 — converts 16-bit little-endian mono PCM to the float
    /// samples whisper expects ([-1, 1), same sample rate). Odd tails tolerated;
    /// empty input yields an empty array so the engine can bail cleanly.
    /// </summary>
    public static float[] ToFloatSamples(ReadOnlySpan<byte> pcm16Le)
    {
        int samples = pcm16Le.Length / 2;
        var floats = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            short s = (short)(pcm16Le[i * 2] | (pcm16Le[i * 2 + 1] << 8));
            floats[i] = s / 32768f;
        }
        return floats;
    }

    /// <summary>RMS floor under which a meter reading is treated as digital silence.</summary>
    public const int SilenceRms = 200;

    /// <summary>RMS that already reads as a full-scale meter bar.</summary>
    public const int LoudRms = 3200;

    /// <summary>
    /// v2.6.0-alpha.5 — maps a window's RMS loudness onto the 0..1 waveform-meter
    /// scale: below <see cref="SilenceRms"/> is digital silence (the bars rest at
    /// zero instead of twitching with room noise), <see cref="LoudRms"/> and above
    /// saturate. The power curve lifts quiet speech out of the floor so the
    /// visualizer reacts to normal talking, not just shouting.
    /// </summary>
    public static double RmsToLevel(double rms)
    {
        if (rms <= SilenceRms) return 0;
        double x = Math.Min(1.0, (rms - SilenceRms) / (LoudRms - SilenceRms));
        return Math.Pow(x, 0.6);
    }
}
