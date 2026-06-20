using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class LocalAppDiagnosticsTests
{
    [Fact]
    public void CreateDefault_RecordsEventsToPlannedDiagnosticsDirectory()
    {
        using var temp = new TestTemporaryDirectory();
        var diagnosticsDirectory = Path.Combine(temp.Path, "Diagnostics");
        var diagnostics = LocalAppDiagnostics.CreateDefault(
            "9.9.9",
            new TestDiagnosticsPathProvider(diagnosticsDirectory));

        diagnostics.RecordEvent("app_start");

        var line = File.ReadLines(Path.Combine(diagnosticsDirectory, "events.jsonl")).Single();
        line.Should().Contain("\"eventName\":\"app_start\"");
        line.Should().Contain("\"appVersion\":\"9.9.9\"");
    }

    [Fact]
    public void CreateDefault_RecordsCrashesToPlannedDiagnosticsDirectory()
    {
        using var temp = new TestTemporaryDirectory();
        var diagnosticsDirectory = Path.Combine(temp.Path, "Diagnostics");
        var diagnostics = LocalAppDiagnostics.CreateDefault(
            "9.9.9",
            new TestDiagnosticsPathProvider(diagnosticsDirectory));

        var reportPath = diagnostics.RecordCrash(new InvalidOperationException("boom"), "dispatcher");

        reportPath.Should().StartWith(Path.Combine(diagnosticsDirectory, "CrashReports"));
        File.Exists(reportPath).Should().BeTrue();
        File.ReadAllText(reportPath).Should().Contain("boom");
    }

    [Fact]
    public void RecordMethods_DoNotThrowWhenStoreCannotWrite()
    {
        using var temp = new TestTemporaryDirectory();
        var blockerPath = Path.Combine(temp.Path, "blocker");
        var invalidDirectory = Path.Combine(blockerPath, "child");
        File.WriteAllText(blockerPath, "not a directory");
        var diagnostics = new LocalAppDiagnostics(
            new AppDiagnosticsFileStore(new AppDiagnosticsOptions(invalidDirectory, IsEnabled: true)),
            AppDiagnosticsMetadata.Create("9.9.9"));

        var recordEvent = () => diagnostics.RecordEvent("app_start");
        var recordCrash = () => diagnostics.RecordCrash(new InvalidOperationException("boom"), "dispatcher");

        recordEvent.Should().NotThrow();
        recordCrash.Should().NotThrow().Which.Should().BeEmpty();
    }

    private sealed class TestDiagnosticsPathProvider(string directory) : IAppDiagnosticsPathProvider
    {
        public string GetDiagnosticsDirectory() => directory;
    }
}
