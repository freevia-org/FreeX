using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for R74-commands-name-manager-4-2 (src/FreeX.App.Host/MainWindow.Editing.cs,
/// TryDefineNameFromNameBox): typing an EXISTING named-formula/constant's name into the Name Box and
/// pressing Enter silently redefined it as a workbook-scoped named RANGE over the current selection --
/// because the Name Box's "does this already resolve to something?" check
/// (<c>TryParseNameBoxReferenceRange</c>/<see cref="FreeX.App.Services.WorkbookReferenceNavigator"/>)
/// only ever recognizes NamedRanges/ScopedNamedRanges as navigable, so an existing formula-name
/// (which has no GridRange to navigate to) fell through to the same "define a brand-new name" path a
/// truly-new name would take. NamedRanges then wins over the stale NamedFormulas entry at evaluation
/// time, so every formula referencing the name silently changed value.
/// </summary>
public sealed class R74_NameBoxFormulaCollisionTests
{
    private static ComboBox GetCellAddressBox(MainWindow window) =>
        (ComboBox)window.FindName("CellAddressBox")!;

    private static GridRange? GetSelectedRange(MainWindow window) =>
        ((SheetGridView)window.FindName("SheetGrid")!).SelectedRange;

    private static bool PressEnter(MainWindow window, ComboBox box)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        R49MainWindowTestHarness.Invoke(window, "CellAddressBox_KeyDown", box, args);
        R49MainWindowTestHarness.PumpDispatcher();
        return args.Handled;
    }

    [Fact]
    public void NameBoxEnter_WithExistingNamedFormulaName_DoesNotClobberItWithARange()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                workbook.NamedFormulas["TaxRate"] = "0.08";

                var box = GetCellAddressBox(window);
                box.Text = "TaxRate";
                R49MainWindowTestHarness.PumpDispatcher();

                PressEnter(window, box);

                workbook.NamedFormulas.Should().ContainKey("TaxRate");
                workbook.NamedFormulas["TaxRate"].Should().Be("0.08",
                    "typing an existing named formula/constant's name in the Name Box must never silently redefine it");
                workbook.NamedRanges.Should().NotContainKey("TaxRate",
                    "the Name Box must not add a colliding NamedRanges entry for a name that already exists as a NamedFormula");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void NameBoxEnter_WithExistingSheetScopedNamedFormulaName_DoesNotClobberItWithARange()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.Sheets[0];
                workbook.DefineNamedFormula("LocalRate", "0.05", sheet.Id);

                var box = GetCellAddressBox(window);
                box.Text = "LocalRate";
                R49MainWindowTestHarness.PumpDispatcher();

                PressEnter(window, box);

                workbook.ScopedNamedFormulas.Should().ContainKey(("LocalRate", sheet.Id));
                workbook.ScopedNamedFormulas[("LocalRate", sheet.Id)].Should().Be("0.05");
                workbook.NamedRanges.Should().NotContainKey("LocalRate");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: the ordinary "type a brand new name to define it" workflow must still work.
    [Fact]
    public void NameBoxEnter_WithBrandNewUniqueName_StillDefinesNamedRange()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(workbook.Sheets[0].Id, 3, 3));
                R49MainWindowTestHarness.PumpDispatcher();

                var box = GetCellAddressBox(window);
                box.Text = "BrandNewFormulaFreeName";
                R49MainWindowTestHarness.PumpDispatcher();

                PressEnter(window, box);

                workbook.NamedRanges.Should().ContainKey("BrandNewFormulaFreeName",
                    "typing a brand-new, non-colliding name in the Name Box must still define it, exactly as before this fix");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: typing an existing named RANGE's name must still navigate to it (this
    // fix must only reject formula-name collisions, not the ordinary named-range navigation path).
    [Fact]
    public void NameBoxEnter_WithExistingNamedRangeName_StillNavigates()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.Sheets[0];
                var namedRange = new GridRange(new CellAddress(sheet.Id, 7, 2), new CellAddress(sheet.Id, 7, 2));
                workbook.DefineNamedRange("Total", namedRange);

                var box = GetCellAddressBox(window);
                box.Text = "Total";
                R49MainWindowTestHarness.PumpDispatcher();

                PressEnter(window, box);

                GetSelectedRange(window).Should().Be(namedRange);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
