using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Freexcel.App.Host;

public sealed record AppDiagnosticsOptions(string DiagnosticsDirectory, bool IsEnabled)
{
    public static AppDiagnosticsOptions CreateDefault() =>
        CreateDefault(() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static AppDiagnosticsOptions CreateDefault(Func<string> localAppDataProvider)
    {
        var disabled = string.Equals(
            Environment.GetEnvironmentVariable("FREEXCEL_DIAGNOSTICS"),
            "0",
            StringComparison.OrdinalIgnoreCase);
        var localAppData = localAppDataProvider();
        var diagnosticsDirectory = Path.Combine(localAppData, "Freexcel", "Diagnostics");
        return new AppDiagnosticsOptions(diagnosticsDirectory, IsEnabled: !disabled);
    }
}

public sealed record AppDiagnosticsMetadata(
    string AppVersion,
    string SessionId,
    string RuntimeDescription,
    string OperatingSystemDescription,
    string ProcessArchitecture)
{
    public static AppDiagnosticsMetadata Create(string appVersion) =>
        new(
            appVersion,
            Guid.NewGuid().ToString("N"),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString());
}

public interface IAppDiagnostics
{
    void RecordEvent(string eventName, IReadOnlyDictionary<string, string?>? properties = null);

    string RecordCrash(Exception exception, string source);
}

public sealed class AppDiagnostics : IAppDiagnostics
{
    private readonly AppDiagnosticsFileStore _fileStore;
    private readonly AppDiagnosticsMetadata _metadata;

    public AppDiagnostics(AppDiagnosticsFileStore fileStore, AppDiagnosticsMetadata metadata)
    {
        _fileStore = fileStore;
        _metadata = metadata;
    }

    public void RecordEvent(string eventName, IReadOnlyDictionary<string, string?>? properties = null)
    {
        try
        {
            _fileStore.RecordEvent(eventName, _metadata, properties);
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
            // Crash reporting is best-effort; preserve the original failure path.
            return string.Empty;
        }
    }
}

public sealed class AppDiagnosticsFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly HashSet<string> AllowedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "command",
        "extension",
        "fileType",
        "format",
        "reason",
        "scope",
        "source",
        "status",
        "worksheetCount"
    };

    private readonly AppDiagnosticsOptions _options;

    public AppDiagnosticsFileStore(AppDiagnosticsOptions options)
    {
        _options = options;
    }

    public void RecordEvent(
        string eventName,
        AppDiagnosticsMetadata metadata,
        IReadOnlyDictionary<string, string?>? properties = null)
    {
        if (!_options.IsEnabled)
            return;

        Directory.CreateDirectory(_options.DiagnosticsDirectory);
        var payload = CreateBasePayload(eventName, metadata);
        foreach (var (key, value) in SanitizeProperties(properties))
            payload[key] = value;

        var line = JsonSerializer.Serialize(payload, JsonOptions);
        File.AppendAllText(Path.Combine(_options.DiagnosticsDirectory, "events.jsonl"), line + Environment.NewLine);
    }

    public string RecordCrash(Exception exception, string source, AppDiagnosticsMetadata metadata)
    {
        if (!_options.IsEnabled)
            return string.Empty;

        var crashDirectory = Path.Combine(_options.DiagnosticsDirectory, "CrashReports");
        Directory.CreateDirectory(crashDirectory);

        var payload = CreateBasePayload("crash", metadata);
        payload["source"] = source;
        payload["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name;
        payload["message"] = exception.Message;
        payload["stackTrace"] = exception.ToString();
        payload["processId"] = Environment.ProcessId.ToString();

        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{metadata.SessionId}.json";
        var reportPath = Path.Combine(crashDirectory, fileName);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonOptions)
        {
            WriteIndented = true
        }));

        RecordEvent("crash", metadata, new Dictionary<string, string?>
        {
            ["source"] = source,
            ["reason"] = exception.GetType().Name
        });

        return reportPath;
    }

    private static Dictionary<string, string?> CreateBasePayload(string eventName, AppDiagnosticsMetadata metadata) =>
        new(StringComparer.Ordinal)
        {
            ["eventName"] = eventName,
            ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["appVersion"] = metadata.AppVersion,
            ["sessionId"] = metadata.SessionId,
            ["runtime"] = metadata.RuntimeDescription,
            ["os"] = metadata.OperatingSystemDescription,
            ["processArchitecture"] = metadata.ProcessArchitecture
        };

    private static IEnumerable<KeyValuePair<string, string?>> SanitizeProperties(
        IReadOnlyDictionary<string, string?>? properties)
    {
        if (properties is null)
            yield break;

        foreach (var pair in properties)
        {
            if (!AllowedPropertyNames.Contains(pair.Key))
                continue;

            yield return pair;
        }
    }
}
