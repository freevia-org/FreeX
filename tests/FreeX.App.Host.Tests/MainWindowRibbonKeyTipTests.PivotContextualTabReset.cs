using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    // R80-app-ribbon-contextual-5-1: SetPivotContextualTabsVisible(false) used to only collapse
    // the PivotTable Analyze/Design tab headers without checking whether one of them was the
    // active RibbonTabs.SelectedItem, leaving the TabControl rendering the now-headerless pivot
    // tab's content (with no visible active header anywhere in the strip) after the pivot was
    // deselected. Excel snaps the ribbon back to the previously active normal tab instead.
    [Fact]
    public void PivotContextualTabs_ResetSelectedRibbonTabWhenPivotDeselected()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(ConfigureWorkbookWithPivotTable);
            harness.RefreshViewport();

            harness.SelectRange(6, 5, 6, 5);
            harness.RefreshViewport();
            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeTrue();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.J);
            harness.HandleKeyTip(Key.A);
            harness.SelectedRibbonTabHeader.Should().Be("PivotTable Analyze");

            harness.SelectRange(20, 1, 20, 1);
            harness.RefreshViewport();

            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeFalse();
            harness.SelectedRibbonTabHeader.Should().Be(
                "Home",
                "Excel returns the ribbon to the previously active normal tab the instant a pivot table is deselected, " +
                "instead of leaving the strip stuck rendering the now-hidden PivotTable Analyze tab's content");
        });
    }

    // No-regression sibling: when the ribbon is already on a normal tab (not one of the pivot
    // contextual tabs) while the pivot is deselected, the reset guard's ReferenceEquals checks
    // must not clobber that unrelated selection.
    [Fact]
    public void PivotContextualTabs_LeaveUnrelatedSelectedTabAloneWhenPivotDeselected()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(ConfigureWorkbookWithPivotTable);
            harness.RefreshViewport();

            harness.SelectRange(6, 5, 6, 5);
            harness.RefreshViewport();
            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeTrue();
            harness.SelectedRibbonTabHeader.Should().Be("Home");

            harness.SelectRange(20, 1, 20, 1);
            harness.RefreshViewport();

            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeFalse();
            harness.SelectedRibbonTabHeader.Should().Be("Home");
        });
    }
}
