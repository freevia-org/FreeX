using System.IO;
using System.Text.Json;
using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class AppDiagnosticsTests
{
    [Fact]
    public void Options_CreateDefault_UsesLocalAppDataDiagnosticsFolder()
    {
        var localAppData = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        var options = AppDiagnosticsOptions.CreateDefault(() => localAppData);

        options.IsEnabled.Should().BeTrue();
        options.DiagnosticsDirectory.Should().Be(Path.Combine(localAppData, "Freexcel", "Diagnostics"));
    }

    [Fact]
    public void FileStore_RecordEvent_WritesJsonLineWithoutWorkbookContent()
    {
        using var temp = new TemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true));
        var metadata = new AppDiagnosticsMetadata(
            AppVersion: "Version Test",
            SessionId: "session-1",
            RuntimeDescription: ".NET Test",
            OperatingSystemDescription: "Windows Test",
            ProcessArchitecture: "X64");

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
    public void FileStore_RecordCrash_WritesCrashReportAndEvent()
    {
        using var temp = new TemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true));
        var metadata = new AppDiagnosticsMetadata(
            AppVersion: "Version Test",
            SessionId: "session-1",
            RuntimeDescription: ".NET Test",
            OperatingSystemDescription: "Windows Test",
            ProcessArchitecture: "X64");
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
    public void FileStore_WhenDisabled_DoesNotCreateDiagnosticsFiles()
    {
        using var temp = new TemporaryDirectory();
        var store = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: false));
        var metadata = AppDiagnosticsMetadata.Create("Version Test");

        store.RecordEvent("app_start", metadata);
        var reportPath = store.RecordCrash(new Exception("boom"), "test", metadata);

        reportPath.Should().BeEmpty();
        Directory.Exists(temp.Path).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(temp.Path).Should().BeEmpty();
    }

    [Fact]
    public void AppDiagnostics_WhenStoreCannotWrite_DoesNotThrow()
    {
        var blockerPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var invalidDirectory = Path.Combine(blockerPath, "child");
        File.WriteAllText(blockerPath, "not a directory");
        var diagnostics = new AppDiagnostics(
            new AppDiagnosticsFileStore(new AppDiagnosticsOptions(invalidDirectory, IsEnabled: true)),
            AppDiagnosticsMetadata.Create("Version Test"));

        var recordEvent = () => diagnostics.RecordEvent("app_start");
        var recordCrash = () => diagnostics.RecordCrash(new Exception("boom"), "test");

        recordEvent.Should().NotThrow();
        recordCrash.Should().NotThrow().Which.Should().BeEmpty();

        File.Delete(blockerPath);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
