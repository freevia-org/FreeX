using FreeW.Validation.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class ReadAloudPauseSmokeTests
{
    [Fact]
    public void TryRun_LeavesOrdinaryStartupArgumentsUnhandled()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handled = ReadAloudPauseSmoke.TryRun(
            ["sample.docx"],
            output,
            error,
            out var exitCode);

        handled.Should().BeFalse();
        exitCode.Should().Be(0);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().BeEmpty();

        var smokeSource = File.ReadAllText(TestWorkspaceFileLocator.Find(
            "freew",
            "TestSupport",
            "Validation.Avalonia",
            "ReadAloudPauseSmoke.cs"));
        smokeSource.Should().NotContain("OwnedProcessIdForSmoke",
            "the smoke coordinator must run against both validation and host-test renderer variants");
    }
}
