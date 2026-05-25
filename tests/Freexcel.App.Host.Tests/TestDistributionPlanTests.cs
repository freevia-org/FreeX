using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class TestDistributionPlanTests
{
    [Fact]
    public void DistributionPlan_DocumentsPhaseSixUsageAnalyticsContract()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "TEST_DISTRIBUTION_PLAN.md"));

        source.Should().Contain("6. Lightweight usage analytics");
        source.Should().Contain("app lifecycle");
        source.Should().Contain("command/dialog opened");
        source.Should().Contain("file import/export type");
        source.Should().Contain("crash/session linkage");
        source.Should().Contain("workbook contents, formulas, filenames, or paths");
        source.Should().Contain("FREEXCEL_DIAGNOSTICS=0");
    }

    [Fact]
    public void DistributionPlan_DocumentsPhaseSevenAutoUpdateReadiness()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "TEST_DISTRIBUTION_PLAN.md"));

        source.Should().Contain("7. Auto-update readiness");
        source.Should().Contain("Help > Check for Updates");
        source.Should().Contain("stable latest release page");
        source.Should().Contain("Velopack");
        source.Should().Contain("custom `Main`");
        source.Should().Contain("no background update download");
    }
}
