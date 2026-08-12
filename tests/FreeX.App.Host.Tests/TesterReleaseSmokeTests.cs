extern alias ProductionWpf;
extern alias ValidationWpf;

using System.IO;
using System.Text.Json;
using FluentAssertions;
using TesterReleaseSmoke = ValidationWpf::FreeX.Validation.Wpf.TesterReleaseSmoke;

namespace FreeX.App.Host.Tests;

public sealed class TesterReleaseSmokeTests
{
    [Fact]
    public void ShippingAssembly_DoesNotOwnTesterReleaseSmoke()
    {
        var assembly = typeof(ProductionWpf::FreeX.App.Host.MainWindow).Assembly;

        assembly.GetType("FreeX.App.Host.TesterReleaseSmoke").Should().BeNull();
        assembly.GetType("FreeX.App.Host.TesterReleaseSmokeReport").Should().BeNull();
    }

    [Fact]
    public void ValidationAssembly_OwnsTesterReleaseSmoke()
    {
        typeof(TesterReleaseSmoke).Assembly.GetName().Name.Should().Be("FreeX.Validation.Wpf");

        var hostProject = WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "FreeX.App.Host.csproj");
        File.Exists(Path.Combine(
            Path.GetDirectoryName(hostProject)!,
            "TesterReleaseSmoke.cs")).Should().BeFalse();
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "App.xaml.cs")
            .Should().NotContain("TesterReleaseSmoke");
        WorkspaceFileLocator.ReadAllText(
                "tools", "FreeX.Validation.Wpf", "TesterReleaseSmoke.cs")
            .Should().Contain("internal static class TesterReleaseSmoke");
    }

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
