namespace FreeW.App.Avalonia;

public sealed partial class AvaloniaSpeechEngine
{
    internal int? OwnedProcessIdForSmoke
    {
        get
        {
            lock (_gate)
                return _process?.ProcessId;
        }
    }
}
