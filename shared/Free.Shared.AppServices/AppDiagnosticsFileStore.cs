using System.Text.Json;
using System.Text.Json.Serialization;

namespace Free.Shared.AppServices;

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
        "dialog",
        "extension",
        "fileType",
        "format",
        "grantKind",
        "payloadRedacted",
        "reason",
        "scope",
        "source",
        "status",
        "worksheetCount"
    };

    // Crash/diagnostics events are recorded from arbitrary threads (AppDomain.UnhandledException,
    // TaskScheduler.UnobservedTaskException, finalizers); serialize appends so concurrent writers
    // do not interleave or corrupt the shared events.jsonl. Static so two stores pointed at the same
    // file still serialize against each other. Diagnostics volume is tiny, so contention is moot.
    private static readonly object FileWriteLock = new();

    private readonly AppDiagnosticsOptions _options;

    public AppDiagnosticsFileStore(AppDiagnosticsOptions options)
    {
        _options = options;
    }

    public bool IsEnabled => _options.IsEnabled;

    public void RecordEvent(
        string eventName,
        AppDiagnosticsMetadata metadata,
        IReadOnlyDictionary<string, string?>? properties = null)
    {
        if (!_options.IsEnabled)
            return;

        try
        {
            Directory.CreateDirectory(_options.DiagnosticsDirectory);
            var payload = CreateBasePayload(eventName, metadata);
            foreach (var (key, value) in SanitizeProperties(properties))
                payload[key] = value;

            var line = JsonSerializer.Serialize(payload, JsonOptions);
            lock (FileWriteLock)
            {
                File.AppendAllText(Path.Combine(_options.DiagnosticsDirectory, "events.jsonl"), line + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics are best-effort and must never affect launch or command flow.
        }
    }

    public string RecordCrash(Exception exception, string source, AppDiagnosticsMetadata metadata)
    {
        if (!_options.IsEnabled)
            return string.Empty;

        try
        {
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
        catch
        {
            // Local crash reporting is best-effort; preserve the original failure path.
            return string.Empty;
        }
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

    public static IEnumerable<KeyValuePair<string, string?>> SanitizeProperties(
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
