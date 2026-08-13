namespace FreeP.App.Avalonia;

// Compiled into the isolated validation-host renderer variant only.
public sealed partial class MainWindow
{
    private StartupDirtyTrace? _startupDirtyTrace;

    internal IReadOnlyList<StartupDirtyTraceEntry> StartupDirtyTraceEntries =>
        _startupDirtyTrace?.Entries ?? [];

    partial void InitializeConditionalHost()
    {
        if (App.StartupDirtyTraceEnabledForValidationHost)
            _startupDirtyTrace = new StartupDirtyTrace();
    }

    partial void RecordStartupObservation(string stage) =>
        _startupDirtyTrace?.Record(stage, _fileWorkflow);

    partial void RegisterStartupOpenedObservation()
    {
        if (_startupDirtyTrace is not null)
            Opened += (_, _) => RecordStartupObservation("window-opened");
    }
}
