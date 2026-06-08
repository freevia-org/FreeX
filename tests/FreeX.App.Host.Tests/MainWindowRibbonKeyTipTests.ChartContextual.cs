using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void ChartContextualTabs_AppearDisappearWithNormalChartAndExposeJcJfKeyTips()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(ConfigureWorkbookWithChart);
            harness.RefreshViewport();

            harness.ContextualTabIsVisible("ChartDesignTab").Should().BeTrue();
            harness.ContextualTabIsVisible("ChartFormatTab").Should().BeTrue();

            harness.EnterKeyTipScope("TopLevel");
            harness.OverlayBadgeTexts.Should().Contain(["JC", "JF"]);
            harness.HandleKeyTip(Key.J);
            harness.SelectedRibbonTabHeader.Should().NotBe("Draw", "visible chart contextual keytips should keep J as a prefix");
            harness.KeyTipScope.Should().Be("TopLevel");
            harness.HandleKeyTip(Key.C);

            harness.SelectedRibbonTabHeader.Should().Be("Chart Design");
            harness.KeyTipScope.Should().Be("Commands");
            harness.VisibleCommandKeyTips("CT").Should().ContainSingle("Change Chart Type");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.J);
            harness.HandleKeyTip(Key.F);

            harness.SelectedRibbonTabHeader.Should().Be("Format");
            harness.KeyTipScope.Should().Be("Commands");
            harness.VisibleCommandKeyTips("F").Should().ContainSingle("Format Chart Area");

            harness.ClearCharts();
            harness.RefreshViewport();

            harness.ContextualTabIsVisible("ChartDesignTab").Should().BeFalse();
            harness.ContextualTabIsVisible("ChartFormatTab").Should().BeFalse();

            harness.EnterKeyTipScope("TopLevel");
            harness.OverlayBadgeTexts.Should().NotContain(["JC", "JF"]);
        });
    }

    [Fact]
    public void ChartContextualTabs_IgnorePivotAndHiddenCharts()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(ConfigureWorkbookWithPivotAndHiddenCharts);
            harness.RefreshViewport();

            harness.ContextualTabIsVisible("ChartDesignTab").Should().BeFalse();
            harness.ContextualTabIsVisible("ChartFormatTab").Should().BeFalse();

            harness.EnterKeyTipScope("TopLevel");
            harness.OverlayBadgeTexts.Should().NotContain(["JC", "JF"]);
        });
    }
}
