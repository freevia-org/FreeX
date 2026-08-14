using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void ViewShowToggleKeyTips_UpdateSheetAndFormulaBarState()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.ActiveSheetViewOptions.Should().Be((true, true, true));
            var initialFormulaBarVisibility = harness.FormulaBarIsVisible;

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.V);
            harness.KeyTipScope.Should().Be("Commands", "V is the shared Excel Show group prefix for Gridlines, Headings, and Formula Bar");
            harness.HandleKeyTip(Key.G);

            harness.ActiveSheetViewOptions.Should().Be((false, true, true));
            harness.KeyTipScope.Should().Be("None");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.V);
            harness.HandleKeyTip(Key.H);

            harness.ActiveSheetViewOptions.Should().Be((false, false, true));
            harness.KeyTipScope.Should().Be("None");

            // Outside Page Layout, Ruler (RU) is disabled and a lone window has no active pair for
            // Reset Window Position (RP), so the shared prefix has no executable command.
            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.R);

            harness.KeyTipScope.Should().Be("None");
            harness.ActiveSheetViewOptions.Should().Be((false, false, true));

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.P);

            harness.ActiveSheetViewMode.Should().Be(WorksheetViewMode.PageLayout);

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.R);

            harness.KeyTipScope.Should().Be("Commands", "R is the prefix for the Ruler keytip RU");
            harness.ActiveSheetViewOptions.Should().Be((false, false, true));

            harness.HandleKeyTip(Key.U);

            harness.ActiveSheetViewOptions.Should().Be((false, false, false));
            harness.KeyTipScope.Should().Be("None");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.V);

            harness.KeyTipScope.Should().Be("Commands", "V is the prefix for Excel-style Show keytips VG/VH/VF");
            harness.FormulaBarIsVisible.Should().Be(initialFormulaBarVisibility);

            harness.HandleKeyTip(Key.F);

            harness.FormulaBarIsVisible.Should().Be(!initialFormulaBarVisibility);
            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty();
        });
    }

    [Fact]
    public void ViewWorkbookModeKeyTips_UpdateSheetViewMode()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.ActiveSheetViewMode.Should().Be(WorksheetViewMode.Normal);

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.I);

            harness.ActiveSheetViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
            harness.KeyTipScope.Should().Be("None");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.P);

            harness.ActiveSheetViewMode.Should().Be(WorksheetViewMode.PageLayout);
            harness.KeyTipScope.Should().Be("None");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.L);

            harness.ActiveSheetViewMode.Should().Be(WorksheetViewMode.Normal);
            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty();
        });
    }
}
