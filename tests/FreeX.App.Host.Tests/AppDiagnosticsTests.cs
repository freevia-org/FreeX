using System.IO;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

public sealed class AppDiagnosticsTests
{
    [Fact]
    public void AppDiagnostics_WhenStoreCannotWrite_DoesNotThrow()
    {
        using var temp = new TestTemporaryDirectory();
        var blockerPath = Path.Combine(temp.Path, "blocker");
        var invalidDirectory = Path.Combine(blockerPath, "child");
        File.WriteAllText(blockerPath, "not a directory");
        var diagnostics = new AppDiagnostics(
            new AppDiagnosticsFileStore(new AppDiagnosticsOptions(invalidDirectory, IsEnabled: true)),
            AppDiagnosticsMetadata.Create("Version Test"));

        var recordEvent = () => diagnostics.RecordEvent("app_start");
        var recordCrash = () => diagnostics.RecordCrash(new Exception("boom"), "test");

        recordEvent.Should().NotThrow();
        recordCrash.Should().NotThrow().Which.Should().BeEmpty();
    }
}
