using System.Text.Json;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class AppDiagnosticsFileStoreTests
{
    [Fact]
    public void RecordEvent_WritesJsonLineWithoutWorkbookContent()
    {
        using var temp = new TestTemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true));
        var metadata = CreateMetadata();

        store.RecordEvent("workbook_opened", metadata, new Dictionary<string, string?>
        {
            ["extension"] = ".xlsx",
            ["workbookPath"] = "C:\\Users\\tester\\private.xlsx",
            ["worksheetCount"] = "3"
        });

        var eventsPath = Path.Combine(temp.Path, "events.jsonl");
        File.Exists(eventsPath).Should().BeTrue();
        var line = File.ReadLines(eventsPath).Single();
        line.Should().Contain("\"eventName\":\"workbook_opened\"");
        line.Should().Contain("\"extension\":\".xlsx\"");
        line.Should().Contain("\"worksheetCount\":\"3\"");
        line.Should().NotContain("private.xlsx");
        line.Should().NotContain("workbookPath");
    }

    [Fact]
    public void RecordEvent_AllowsUsageMetadataButDropsDocumentDetails()
    {
        using var temp = new TestTemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true));
        var metadata = CreateMetadata();

        store.RecordEvent("dialog_opened", metadata, new Dictionary<string, string?>
        {
            ["dialog"] = "OptionsDialog",
            ["command"] = "Options",
            ["status"] = "opened",
            ["fileType"] = "xlsx",
            ["formula"] = "=PRIVATE()",
            ["path"] = "C:\\Users\\tester\\private.xlsx"
        });

        var line = File.ReadLines(Path.Combine(temp.Path, "events.jsonl")).Single();
        line.Should().Contain("\"dialog\":\"OptionsDialog\"");
        line.Should().Contain("\"command\":\"Options\"");
        line.Should().Contain("\"status\":\"opened\"");
        line.Should().Contain("\"fileType\":\"xlsx\"");
        line.Should().NotContain("PRIVATE");
        line.Should().NotContain("private.xlsx");
        line.Should().NotContain("\"formula\"");
        line.Should().NotContain("\"path\"");
    }

    [Fact]
    public void RecordEvent_AllowsFileAccessGrantMetadataButDropsSensitiveGrantDetails()
    {
        using var temp = new TestTemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true));
        var metadata = CreateMetadata();

        store.RecordEvent("workbook_file_access_scope", metadata, new Dictionary<string, string?>
        {
            ["source"] = "avalonia",
            ["scope"] = "workbook_file_access",
            ["status"] = "scope_started",
            ["grantKind"] = "macos-security-scoped-bookmark",
            ["payloadRedacted"] = "true",
            ["bookmarkPayload"] = "RAW_BOOKMARK_TOKEN",
            ["fileName"] = "private.fxl",
            ["filename"] = "private.fxl",
            ["localPath"] = "/Users/tester/private.fxl",
            ["path"] = "/Users/tester/private.fxl",
            ["workbookPath"] = "/Users/tester/private.fxl",
            ["workbookContents"] = "secret workbook text",
            ["contents"] = "secret workbook text",
            ["formula"] = "=SECRET()",
            ["storageIdentifier"] = "file:///Users/tester/private.fxl",
            ["rawStorageIdentifier"] = "file:///Users/tester/private.fxl"
        });

        var line = File.ReadLines(Path.Combine(temp.Path, "events.jsonl")).Single();
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        root.GetProperty("eventName").GetString().Should().Be("workbook_file_access_scope");
        root.GetProperty("source").GetString().Should().Be("avalonia");
        root.GetProperty("scope").GetString().Should().Be("workbook_file_access");
        root.GetProperty("status").GetString().Should().Be("scope_started");
        root.GetProperty("grantKind").GetString().Should().Be("macos-security-scoped-bookmark");
        root.GetProperty("payloadRedacted").GetString().Should().Be("true");
        line.Should().NotContain("RAW_BOOKMARK_TOKEN");
        line.Should().NotContain("private.fxl");
        line.Should().NotContain("SECRET");
        line.Should().NotContain("secret workbook text");
        line.Should().NotContain("file:///Users/tester/private.fxl");
        line.Should().NotContain("\"bookmarkPayload\"");
        line.Should().NotContain("\"fileName\"");
        line.Should().NotContain("\"filename\"");
        line.Should().NotContain("\"localPath\"");
        line.Should().NotContain("\"path\"");
        line.Should().NotContain("\"workbookPath\"");
        line.Should().NotContain("\"workbookContents\"");
        line.Should().NotContain("\"contents\"");
        line.Should().NotContain("\"formula\"");
        line.Should().NotContain("\"storageIdentifier\"");
        line.Should().NotContain("\"rawStorageIdentifier\"");
    }

    [Fact]
    public void RecordCrash_WritesCrashReportAndEvent()
    {
        using var temp = new TestTemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true));
        var metadata = CreateMetadata();
        var exception = new InvalidOperationException("Failure while saving workbook");

        var reportPath = store.RecordCrash(exception, "dispatcher", metadata);

        File.Exists(reportPath).Should().BeTrue();
        Path.GetDirectoryName(reportPath).Should().Be(Path.Combine(temp.Path, "CrashReports"));
        using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = document.RootElement;
        root.GetProperty("eventName").GetString().Should().Be("crash");
        root.GetProperty("source").GetString().Should().Be("dispatcher");
        root.GetProperty("exceptionType").GetString().Should().Be(typeof(InvalidOperationException).FullName);
        root.GetProperty("message").GetString().Should().Be("Failure while saving workbook");

        var eventLine = File.ReadLines(Path.Combine(temp.Path, "events.jsonl")).Single();
        eventLine.Should().Contain("\"eventName\":\"crash\"");
        eventLine.Should().Contain("\"source\":\"dispatcher\"");
    }

    [Fact]
    public void WhenDisabled_DoesNotCreateDiagnosticsFiles()
    {
        using var temp = new TestTemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: false));
        var metadata = AppDiagnosticsMetadata.Create("Version Test");

        store.RecordEvent("app_start", metadata);
        var reportPath = store.RecordCrash(new Exception("boom"), "test", metadata);

        reportPath.Should().BeEmpty();
        Directory.Exists(temp.Path).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(temp.Path).Should().BeEmpty();
    }

    [Fact]
    public void WhenStoreCannotWrite_DoesNotThrowOrBlock()
    {
        using var temp = new TestTemporaryDirectory();
        var blockerPath = Path.Combine(temp.Path, "blocker");
        var invalidDirectory = Path.Combine(blockerPath, "child");
        File.WriteAllText(blockerPath, "not a directory");
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(invalidDirectory, IsEnabled: true));
        var metadata = AppDiagnosticsMetadata.Create("Version Test");

        var recordEvent = () => store.RecordEvent("app_start", metadata);
        var recordCrash = () => store.RecordCrash(new Exception("boom"), "test", metadata);

        recordEvent.Should().NotThrow();
        recordCrash.Should().NotThrow().Which.Should().BeEmpty();
    }

    [Fact]
    public void R126_RecordEvent_CapsEventsFileSizeAcrossManyAppends()
    {
        // events.jsonl must never grow without bound for the life of the install (R126 finding).
        // Write enough events that the file would otherwise exceed the 2 MiB cap several times over,
        // and confirm the file settles down to a small multiple of the cap instead of growing forever.
        using var temp = new TestTemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true));
        var metadata = CreateMetadata();
        var eventsPath = Path.Combine(temp.Path, "events.jsonl");

        // Each line is ~250-300 bytes; 20,000 lines is roughly 5-6 MiB of raw content, well past the
        // 2 MiB cap, so an unbounded implementation would leave a multi-megabyte file behind.
        for (var i = 0; i < 20_000; i++)
        {
            store.RecordEvent("command_invoked", metadata, new Dictionary<string, string?>
            {
                ["command"] = $"EditCell_{i}",
                ["status"] = "succeeded"
            });
        }

        var finalLength = new FileInfo(eventsPath).Length;
        finalLength.Should().BeLessThan(3 * 1024 * 1024,
            "events.jsonl must be capped, not grow without bound across thousands of appends");

        // The newest event must still be present -- trimming drops the oldest lines, not the newest.
        var lastLine = File.ReadLines(eventsPath).Last();
        lastLine.Should().Contain("\"EditCell_19999\"");

        // The very first event should have been trimmed away once the cap kicked in.
        File.ReadAllText(eventsPath).Should().NotContain("\"EditCell_0\"");
    }

    [Fact]
    public void R126_RecordEvent_BelowCap_NeverTrims()
    {
        // No-regression sibling: normal small-scale usage (well under the cap) must not lose any
        // events -- trimming should only kick in once the file actually crosses the size threshold.
        using var temp = new TestTemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true));
        var metadata = CreateMetadata();
        var eventsPath = Path.Combine(temp.Path, "events.jsonl");

        for (var i = 0; i < 25; i++)
        {
            store.RecordEvent("command_invoked", metadata, new Dictionary<string, string?>
            {
                ["command"] = $"EditCell_{i}",
                ["status"] = "succeeded"
            });
        }

        var lines = File.ReadAllLines(eventsPath);
        lines.Should().HaveCount(25);
        lines[0].Should().Contain("\"EditCell_0\"");
        lines[^1].Should().Contain("\"EditCell_24\"");
    }

    [Fact]
    public void R126_RecordCrash_PrunesOldestReportsOnceOverCap()
    {
        // CrashReports must also be bounded (R126 finding): each crash writes its own uniquely-named
        // file with no pruning anywhere else in the codebase.
        using var temp = new TestTemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true));
        var metadata = CreateMetadata();
        var crashDirectory = Path.Combine(temp.Path, "CrashReports");

        string? firstReportPath = null;
        string lastReportPath = string.Empty;
        for (var i = 0; i < 60; i++)
        {
            var reportPath = store.RecordCrash(new InvalidOperationException($"boom {i}"), "test", metadata);
            firstReportPath ??= reportPath;
            lastReportPath = reportPath;
            // Filenames are millisecond-resolution timestamps; a tiny spin avoids two crashes landing
            // in the same millisecond and colliding on sort order in this tight loop.
            Thread.Sleep(1);
        }

        var remaining = Directory.GetFiles(crashDirectory, "*.json");
        remaining.Length.Should().BeLessThanOrEqualTo(50);
        File.Exists(firstReportPath).Should().BeFalse("the oldest crash reports must be pruned once the cap is exceeded");
        File.Exists(lastReportPath).Should().BeTrue("the newest crash report must be retained");
    }

    private static AppDiagnosticsMetadata CreateMetadata() =>
        new(
            AppVersion: "Version Test",
            SessionId: "session-1",
            RuntimeDescription: ".NET Test",
            OperatingSystemDescription: "Windows Test",
            ProcessArchitecture: "X64");
}
