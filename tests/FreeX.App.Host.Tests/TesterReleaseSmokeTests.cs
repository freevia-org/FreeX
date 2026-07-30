using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class TesterReleaseSmokeTests
{
    [Fact]
    public void Validate_CoversAllRibbonCommandsAndPixelSnappedBorders()
    {
        StaTestRunner.Run(() =>
        {
            var report = TesterReleaseSmoke.Validate();

            report.Success.Should().BeTrue(string.Join(Environment.NewLine, report.Errors));
            report.ActionableRibbonCommandCount.Should().BeGreaterThan(300);
            report.RibbonHandlerCount.Should().BeGreaterThanOrEqualTo(report.ActionableRibbonCommandCount);
            report.BorderPixelSnapPassed.Should().BeTrue();
        });
    }

    [Fact]
    public void TryRun_WritesMachineReadableReport()
    {
        using var temp = new TestTemporaryDirectory();
        var reportPath = Path.Combine(temp.Path, "smoke.json");

        StaTestRunner.Run(() =>
        {
            TesterReleaseSmoke.TryRun(
                    [TesterReleaseSmoke.CommandLineSwitch, reportPath],
                    out var exitCode)
                .Should().BeTrue();
            exitCode.Should().Be(0);
        });

        using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
        document.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("BorderPixelSnapPassed").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("ActionableRibbonCommandCount").GetInt32().Should().BeGreaterThan(300);
    }
}
