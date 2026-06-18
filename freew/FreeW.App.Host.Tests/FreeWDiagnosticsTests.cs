using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Free.Shared.AppServices;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for FreeW's P4 local-diagnostics wiring: events/crashes go through the shared
/// <see cref="AppDiagnosticsFileStore"/>, and the shared path planner resolves FreeW's own diagnostics
/// folder (because the test assembly installs AppProduct = "FreeW", mirroring Program.Main).
/// </summary>
public sealed class FreeWDiagnosticsTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeW.DiagnosticsTests", Guid.NewGuid().ToString("N"));

    public FreeWDiagnosticsTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void DiagnosticsDirectory_ResolvesUnderFreeWProductFolder()
    {
        var provider = new TestDiagnosticsPathProvider(Path.Combine(_tempDir, "FreeW", "Diagnostics"));

        var directory = AppStoragePathPlanner.GetDiagnosticsDirectory(provider);

        directory.Should().Contain("FreeW");
        directory.Should().EndWith(Path.Combine("FreeW", "Diagnostics"));
    }

    [Fact]
    public void RecordEvent_WritesEventLineToFreeWDiagnosticsFolder()
    {
        var fileStore = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(_tempDir, IsEnabled: true));
        var metadata = AppDiagnosticsMetadata.Create("9.9.9");

        fileStore.RecordEvent("app_start", metadata);

        var eventsFile = Path.Combine(_tempDir, "events.jsonl");
        File.Exists(eventsFile).Should().BeTrue();
        File.ReadAllText(eventsFile).Should().Contain("app_start");
    }

    [Fact]
    public void RecordCrash_WritesCrashReportFile()
    {
        var fileStore = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(_tempDir, IsEnabled: true));
        var metadata = AppDiagnosticsMetadata.Create("9.9.9");

        var reportPath = fileStore.RecordCrash(new InvalidOperationException("boom"), "appdomain", metadata);

        reportPath.Should().NotBeNullOrEmpty();
        File.Exists(reportPath).Should().BeTrue();
        File.ReadAllText(reportPath).Should().Contain("boom");
        Directory.EnumerateFiles(Path.Combine(_tempDir, "CrashReports")).Should().NotBeEmpty();
    }

    [Fact]
    public void WhenDisabled_RecordEventWritesNothing()
    {
        var fileStore = new AppDiagnosticsFileStore(new AppDiagnosticsOptions(_tempDir, IsEnabled: false));
        var metadata = AppDiagnosticsMetadata.Create("9.9.9");

        fileStore.RecordEvent("app_start", metadata);

        Directory.EnumerateFileSystemEntries(_tempDir).Should().BeEmpty();
    }

    private sealed class TestDiagnosticsPathProvider(string directory) : IAppDiagnosticsPathProvider
    {
        public string GetDiagnosticsDirectory() => directory;
    }
}
