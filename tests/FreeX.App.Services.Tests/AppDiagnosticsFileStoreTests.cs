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

    private static AppDiagnosticsMetadata CreateMetadata() =>
        new(
            AppVersion: "Version Test",
            SessionId: "session-1",
            RuntimeDescription: ".NET Test",
            OperatingSystemDescription: "Windows Test",
            ProcessArchitecture: "X64");
}
