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

    // A crash report used to carry only a stack trace, which says where the app died but not what the
    // user was doing to get there. The events trail lives in a separate events.jsonl that a tester
    // sending in a single crash file does not include, so every crash needed manual correlation by
    // session id and timestamp. Keep the most recent events in memory and embed them in the report so
    // one crash file is self-contained: the fault plus the actions that led to it. Bounded so a
    // long-running session cannot grow it, and it carries the same allow-listed, already-sanitized
    // properties as the trail on disk — no cell contents, file paths, or other user data.
    private const int MaxBreadcrumbs = 25;

    private readonly Queue<Dictionary<string, string?>> _breadcrumbs = new();
    private readonly object _breadcrumbLock = new();

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

            RecordBreadcrumb(eventName, payload);

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

    /// <summary>
    /// Keeps the newest <see cref="MaxBreadcrumbs"/> recorded events in memory so a crash report can
    /// embed the actions that preceded the fault. Stores only the event name, its timestamp, and the
    /// already allow-listed properties — the same data that goes to disk in events.jsonl.
    /// </summary>
    private void RecordBreadcrumb(string eventName, IReadOnlyDictionary<string, string?> payload)
    {
        var breadcrumb = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["eventName"] = eventName
        };

        if (payload.TryGetValue("timestampUtc", out var timestamp))
            breadcrumb["timestampUtc"] = timestamp;

        // Carry the event-specific properties (command, dialog, reason, ...) but not the per-event
        // copies of the session/environment fields, which the crash report already states once.
        foreach (var name in AllowedPropertyNames)
        {
            if (payload.TryGetValue(name, out var value) && value is not null)
                breadcrumb[name] = value;
        }

        lock (_breadcrumbLock)
        {
            _breadcrumbs.Enqueue(breadcrumb);
            while (_breadcrumbs.Count > MaxBreadcrumbs)
                _breadcrumbs.Dequeue();
        }
    }

    /// <summary>Oldest-to-newest snapshot of the retained breadcrumbs.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string?>> GetRecentBreadcrumbs()
    {
        lock (_breadcrumbLock)
        {
            return _breadcrumbs.ToArray();
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

            // Widen to object values so the retained breadcrumbs serialize as a nested array rather
            // than an escaped string. Captured before the "crash" event below is recorded, so the
            // trail ends with the last thing the user did rather than with the crash's own event.
            var reportPayload = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (key, value) in payload)
                reportPayload[key] = value;
            reportPayload["recentEvents"] = GetRecentBreadcrumbs();

            var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{metadata.SessionId}.json";
            var reportPath = Path.Combine(crashDirectory, fileName);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(reportPayload, new JsonSerializerOptions(JsonOptions)
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
