using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// v1.5 — macro recorder, Lumo-style: while a recording is active, every app,
/// file and URL the user launches *through Lumo* is captured as a macro step.
/// Stop &amp; save hands the captured steps to the visual builder.
/// (No global hooks, no key logging — only Lumo's own launches are recorded.)
/// </summary>
public sealed class MacroRecorder
{
    private readonly object _gate = new();
    private readonly List<MacroStep> _steps = new();
    private string? _name;

    /// <summary>Raised on start / stop / cancel / capture (already synchronized).</summary>
    public event Action? Changed;

    public bool Active { get; private set; }

    public int Count { get { lock (_gate) return _steps.Count; } }

    public string? Name { get { lock (_gate) return _name; } }

    public List<MacroStep> Snapshot() { lock (_gate) return new List<MacroStep>(_steps); }

    public void Start(string? name = null)
    {
        lock (_gate)
        {
            _steps.Clear();
            _name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            Active = true;
        }
        DiagnosticLogger.Log("Recorder", "Recording started");
        Changed?.Invoke();
    }

    /// <summary>Ends the recording and returns what was captured (may be empty).</summary>
    public List<MacroStep> Stop()
    {
        List<MacroStep> captured;
        lock (_gate)
        {
            captured = new List<MacroStep>(_steps);
            Active = false;
            _steps.Clear();
        }
        DiagnosticLogger.Log("Recorder", $"Recording stopped — {captured.Count} step(s)");
        Changed?.Invoke();
        return captured;
    }

    public void Cancel()
    {
        bool was;
        lock (_gate) { was = Active; Active = false; _steps.Clear(); }
        if (was) DiagnosticLogger.Log("Recorder", "Recording cancelled");
        if (was) Changed?.Invoke();
    }

    /// <summary>
    /// Captures a successful launcher execution as the next step.
    /// Only App / File / Web results are recorded; everything else is ignored.
    /// </summary>
    public void Capture(ResultItem item)
    {
        try
        {
            if (!Active) return;
            (string type, string arg)? step = item.Kind switch
            {
                ResultKind.App  => ("app",  item.RunArgument),
                ResultKind.File => ("auto", item.RunArgument),   // file or folder — decided at run time
                ResultKind.Web  => ("url",  item.RunArgument),
                _ => null,
            };
            if (step is null || string.IsNullOrWhiteSpace(step.Value.arg)) return;

            bool full = false;
            lock (_gate)
            {
                if (Active && _steps.Count < MacroStep.MaxSteps)
                {
                    _steps.Add(new MacroStep(step.Value.type, step.Value.arg));
                    full = _steps.Count >= MacroStep.MaxSteps;
                }
            }
            if (full)
            {
                DiagnosticLogger.Log("Recorder", $"Step limit ({MacroStep.MaxSteps}) reached");
                Changed?.Invoke();
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Recorder.Capture", ex); }
    }
}
