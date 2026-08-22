using System.Windows.Input;
using FluentAssertions;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void InsertPivotChart_IsReachableOnlyForTheSelectedPivotTable()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(ConfigureWorkbookWithPivotTable);
            harness.SelectRibbonTab("Insert", 2400);

            harness.SetActiveCell(1, 1);
            harness.RibbonCommandIsEnabled(FreeXRibbonCommandIds.PivotChartInsert).Should().BeFalse();

            harness.SetActiveCell(6, 5);
            harness.RibbonCommandIsEnabled(FreeXRibbonCommandIds.PivotChartInsert).Should().BeTrue();
        });
    }

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
    public void PivotFieldListPane_FollowsActiveCellImmediatelyWithoutViewportRefresh()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(ConfigureWorkbookWithPivotTable);
            harness.RefreshViewport();

            harness.PivotFieldListPaneIsVisible.Should().BeFalse();
            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeFalse();
            harness.ContextualTabIsVisible("PivotTableDesignTab").Should().BeFalse();

            harness.SetActiveCell(6, 5);

            harness.PivotFieldListPaneIsVisible.Should().BeTrue();
            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeTrue();
            harness.ContextualTabIsVisible("PivotTableDesignTab").Should().BeTrue();

            harness.SetActiveCell(20, 1);

            harness.PivotFieldListPaneIsVisible.Should().BeFalse();
            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeFalse();
            harness.ContextualTabIsVisible("PivotTableDesignTab").Should().BeFalse();
        });
    }

    [Fact]
    public void PivotFieldListPane_ReconcilesSelectionAfterPaneDrivenPivotShrink()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(ConfigureWorkbookWithPivotTable);
            harness.RefreshViewport();
            harness.SetActiveCell(9, 6);
            harness.PivotFieldListPaneIsVisible.Should().BeTrue();

            harness.ApplyPivotLayoutWithoutRowFields();

            harness.SelectedRange.Should().NotBeNull();
            var selected = harness.SelectedRange!.Value.Start;
            harness.ActivePivotVisibleRange.Contains(selected).Should().BeTrue();
            harness.PivotFieldListPaneIsVisible.Should().BeTrue();
            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeTrue();
            harness.ContextualTabIsVisible("PivotTableDesignTab").Should().BeTrue();
        });
    }

    [Fact]
    public void PivotFieldListPane_DroppedAvailableFieldsIntoRowsAndValuesUpdatesBucketsAndStaysVisible()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(ConfigureWorkbookWithExpandablePivotTable);
            harness.RefreshViewport();
            harness.SetActiveCell(6, 5);

            harness.MoveAvailablePivotFieldTo("Quarter", "Rows");

            harness.PivotListItems("PivotRowsList").Should().Contain(["Region", "Quarter"]);
            harness.ActivePivotVisibleRange.Contains(harness.SelectedRange!.Value.Start).Should().BeTrue();
            harness.PivotFieldListPaneIsVisible.Should().BeTrue();

            harness.MoveAvailablePivotFieldTo("Units", "Values");

            harness.PivotListItems("PivotValuesList").Should().Contain(["Sum of Amount", "Sum of Units"]);
            harness.ActivePivotVisibleRange.Contains(harness.SelectedRange!.Value.Start).Should().BeTrue();
            harness.PivotFieldListPaneIsVisible.Should().BeTrue();
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
