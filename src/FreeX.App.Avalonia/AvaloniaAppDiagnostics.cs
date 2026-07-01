using FreeX.App.Services;

namespace FreeX.App.Avalonia;

internal sealed class AvaloniaAppDiagnostics
{
    private readonly AppDiagnosticsFileStore _fileStore;
    private readonly AppDiagnosticsMetadata _metadata;
    private readonly bool _isEnabled;

    private AvaloniaAppDiagnostics(AppDiagnosticsFileStore fileStore, AppDiagnosticsMetadata metadata, bool isEnabled)
    {
        _fileStore = fileStore;
        _metadata = metadata;
        _isEnabled = isEnabled;
    }

    public AppDiagnosticsMetadata Metadata => _metadata;

    public bool IsEnabled => _isEnabled;

    public static AvaloniaAppDiagnostics Create(string? diagnosticsDirectory = null)
    {
        var defaultOptions = AppDiagnosticsOptions.CreateDefault();
        var options = new AppDiagnosticsOptions(
            string.IsNullOrWhiteSpace(diagnosticsDirectory)
                ? defaultOptions.DiagnosticsDirectory
                : diagnosticsDirectory,
            defaultOptions.IsEnabled);
        var metadata = AppDiagnosticsMetadata.Create(AppHelpInfo.GetVersionText(typeof(AvaloniaAppDiagnostics).Assembly));

        return new AvaloniaAppDiagnostics(new AppDiagnosticsFileStore(options), metadata, options.IsEnabled);
    }

    public void RegisterUnhandledExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                RecordCrash(exception, "appdomain");
        };
        TaskScheduler.UnobservedTaskException += (_, args) => RecordCrash(args.Exception, "task");
    }

    public void RecordEvent(string eventName, IReadOnlyDictionary<string, string?>? properties = null)
    {
        try
        {
            var safeProperties = AppDiagnosticsFileStore.SanitizeProperties(properties)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            _fileStore.RecordEvent(eventName, _metadata, safeProperties);
        }
        catch
        {
            // Diagnostics are best-effort and must never affect app behavior.
        }
    }

    public string RecordCrash(Exception exception, string source)
    {
        try
        {
            return _fileStore.RecordCrash(exception, source, _metadata);
        }
        catch
        {
            // Preserve the original failure path if local crash reporting fails.
            return string.Empty;
        }
    }
}
