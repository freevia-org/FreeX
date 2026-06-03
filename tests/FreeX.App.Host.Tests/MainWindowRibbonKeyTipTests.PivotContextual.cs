using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void PivotContextualTabs_AppearDisappearWithPivotSelectionAndExposeJaJdKeyTips()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(ConfigureWorkbookWithPivotTable);
            harness.RefreshViewport();

            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeFalse();
            harness.ContextualTabIsVisible("PivotTableDesignTab").Should().BeFalse();
            harness.PivotFieldListPaneIsVisible.Should().BeFalse();

            harness.SelectRange(6, 5, 6, 5);
            harness.RefreshViewport();

            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeTrue();
            harness.ContextualTabIsVisible("PivotTableDesignTab").Should().BeTrue();
            harness.PivotFieldListPaneIsVisible.Should().BeTrue();

            harness.EnterKeyTipScope("TopLevel");
            harness.OverlayBadgeTexts.Should().Contain(["JA", "JD"]);
            harness.HandleKeyTip(Key.J);
            harness.HandleKeyTip(Key.A);

            harness.SelectedRibbonTabHeader.Should().Be("PivotTable Analyze");
            harness.KeyTipScope.Should().Be("Commands");
            harness.VisibleCommandKeyTips("R").Should().ContainSingle("Refresh");

            harness.SelectRange(20, 1, 20, 1);
            harness.RefreshViewport();

            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeFalse();
            harness.ContextualTabIsVisible("PivotTableDesignTab").Should().BeFalse();
            harness.PivotFieldListPaneIsVisible.Should().BeFalse();

            harness.EnterKeyTipScope("TopLevel");
            harness.OverlayBadgeTexts.Should().NotContain(["JA", "JD"]);
        });
    }

    [Fact]
    public void ContextualPivotKeyTips_WaitForJaBeforeSelectingAnalyzeTab()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.ShowPivotContextualTabs();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.J);

            harness.SelectedRibbonTabHeader.Should().NotBe("Draw", "visible JA/JD contextual keytips should keep J as a prefix");
            harness.KeyTipScope.Should().Be("TopLevel");

            harness.HandleKeyTip(Key.A);

            harness.SelectedRibbonTabHeader.Should().Be("PivotTable Analyze");
            harness.KeyTipScope.Should().Be("Commands");

            harness.ShowPivotContextualTabs();
            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.J);
            harness.HandleKeyTip(Key.D);

            harness.SelectedRibbonTabHeader.Should().Be("Design");
            harness.KeyTipScope.Should().Be("Commands");
        });
    }
}
