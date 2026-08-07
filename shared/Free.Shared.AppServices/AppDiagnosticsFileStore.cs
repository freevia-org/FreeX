using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Free.Shared.AppServices;

public sealed class AppDiagnosticsFileStore
{
    // events.jsonl accumulates one line per RecordEvent call for the entire lifetime of the install
    // (every command, dialog open, crash, etc.), with no other cleanup anywhere in the app. Cap it by
    // size so a long-lived, heavily-used install never grows the file without bound: once it crosses
    // MaxEventsFileBytes we trim it down to roughly its newest half, keeping only whole lines.
    private const long MaxEventsFileBytes = 2 * 1024 * 1024;

    // CrashReports writes one uniquely-named file per crash with no pruning; cap the count of retained
    // reports the same way, keeping only the most recent MaxCrashReportFiles (filenames are
    // "yyyyMMdd-HHmmssfff-<sessionId>.json", so an ordinal sort is also a chronological sort).
    private const int MaxCrashReportFiles = 50;

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
                var eventsPath = Path.Combine(_options.DiagnosticsDirectory, "events.jsonl");
                File.AppendAllText(eventsPath, line + Environment.NewLine);
                TrimEventsFileIfOversized(eventsPath);
            }
        }
        catch
        {
            // Diagnostics are best-effort and must never affect launch or command flow.
        }
    }

    /// <summary>
    /// Keeps events.jsonl bounded: once it crosses <see cref="MaxEventsFileBytes"/>, drop the oldest
    /// lines and keep roughly the newest half. Called under <see cref="FileWriteLock"/> after every
    /// append so the file never grows without bound for the life of the install.
    /// </summary>
    private static void TrimEventsFileIfOversized(string eventsPath)
    {
        var info = new FileInfo(eventsPath);
        if (!info.Exists || info.Length <= MaxEventsFileBytes)
            return;

        var lines = File.ReadAllLines(eventsPath);
        var keep = new List<string>();
        long keptBytes = 0;
        var budget = MaxEventsFileBytes / 2;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var lineBytes = Encoding.UTF8.GetByteCount(lines[i]) + Environment.NewLine.Length;
            if (keptBytes + lineBytes > budget && keep.Count > 0)
                break;

            keep.Add(lines[i]);
            keptBytes += lineBytes;
        }

        keep.Reverse();
        File.WriteAllLines(eventsPath, keep);
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
            PruneCrashReportsIfOverCap(crashDirectory);

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

    /// <summary>
    /// Keeps CrashReports bounded: once there are more than <see cref="MaxCrashReportFiles"/> reports,
    /// delete the oldest ones (by filename, which sorts chronologically) so the directory never
    /// accumulates one file per crash for the life of the install.
    /// </summary>
    private static void PruneCrashReportsIfOverCap(string crashDirectory)
    {
        var files = Directory.GetFiles(crashDirectory, "*.json");
        if (files.Length <= MaxCrashReportFiles)
            return;

        Array.Sort(files, StringComparer.Ordinal);
        var excess = files.Length - MaxCrashReportFiles;
        for (var i = 0; i < excess; i++)
        {
            try { File.Delete(files[i]); } catch { /* best-effort pruning */ }
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
