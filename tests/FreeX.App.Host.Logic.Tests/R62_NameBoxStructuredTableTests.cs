using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for R62-commands-name-box-6-1: typing an existing structured table's name into
/// the Name Box must select the table's data-body range (matching Excel and the structured-reference
/// "#Data" selector), rather than falling through to TryDefineNameFromNameBox and silently defining a
/// colliding workbook-scoped named range with the same identifier as the table.
/// </summary>
public sealed class R62_NameBoxStructuredTableTests
{
    private static ComboBox GetCellAddressBox(MainWindow window) =>
        (ComboBox)window.FindName("CellAddressBox")!;

    private static GridRange? GetSelectedRange(MainWindow window) =>
        ((SheetGridView)window.FindName("SheetGrid")!).SelectedRange;

    /// <summary>
    /// Presses Enter on the Name Box and reports whether the handler took it, along with the state
    /// the handler actually branched on.
    /// </summary>
    /// <remarks>
    /// CellAddressBox_KeyDown returns without handling unless the modifiers are None, and it reads
    /// them from the KeyboardDevice rather than from the event we construct -- WPF resolves those
    /// from the Win32 async key state, which is global to the desktop rather than private to this
    /// test. Under the full 31-assembly gate other UI-driving test processes are synthesising input
    /// at the same time, and this test failed there with a bare "expected True, found False" while
    /// passing alone and passing with its own 1,455-test assembly alone. Report the branch inputs so
    /// a recurrence identifies its own cause instead of only proving that something went wrong.
    /// </remarks>
    private static (bool Handled, string Diagnostics) PressEnter(MainWindow window, ComboBox box)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
        var modifiersBefore = Keyboard.PrimaryDevice.Modifiers;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        R49MainWindowTestHarness.Invoke(window, "CellAddressBox_KeyDown", box, args);
        R49MainWindowTestHarness.PumpDispatcher();

        var diagnostics =
            $"modifiers before={modifiersBefore}, during={args.KeyboardDevice.Modifiers}, " +
            $"box.Text='{box.Text}'";
        return (args.Handled, diagnostics);
    }

    [Fact]
    public void NameBoxEnter_WithExistingTableName_SelectsDataBodyRangeAndDoesNotCreateCollidingName()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.Sheets[0];
                // Header row 1, data rows 2-4, columns 1-2 -- matches how CreateStructuredTableCommand /
                // XlsxStructuredTableModelMapper populate StructuredTableModel.Range (header included).
                var tableRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
                sheet.StructuredTables.Add(new StructuredTableModel
                {
                    Id = 1,
                    Name = "SalesTable",
                    DisplayName = "SalesTable",
                    Range = tableRange,
                });

                var box = GetCellAddressBox(window);
                box.Text = "SalesTable";
                R49MainWindowTestHarness.PumpDispatcher();

                var enter = PressEnter(window, box);
                enter.Handled.Should().BeTrue(
                    "the Name Box must take Enter ({0})", enter.Diagnostics);

                var expectedDataBody = new GridRange(
                    new CellAddress(sheet.Id, 2, 1),
                    new CellAddress(sheet.Id, 4, 2));
                GetSelectedRange(window).Should().Be(expectedDataBody,
                    "Excel selects the table's data-body range (excluding the header row) when a table name is entered in the Name Box");

                workbook.NamedRanges.Should().NotContainKey("SalesTable",
                    "the Name Box must never silently define a workbook-scoped named range that collides with an existing structured table's name");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void NameBoxEnter_WithBrandNewUniqueName_StillDefinesNamedRange()
    {
        // Sibling no-regression test: the structured-table guard must not block the ordinary
        // "type a brand new name to define it" workflow when there is no colliding table.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.Sheets[0];
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheet.Id, 3, 3));
                R49MainWindowTestHarness.PumpDispatcher();

                var box = GetCellAddressBox(window);
                box.Text = "BrandNewName";
                R49MainWindowTestHarness.PumpDispatcher();

                var enter = PressEnter(window, box);
                enter.Handled.Should().BeTrue(
                    "the Name Box must take Enter ({0})", enter.Diagnostics);

                workbook.NamedRanges.Should().ContainKey("BrandNewName",
                    "typing a brand-new, non-colliding name in the Name Box must still define it, exactly as before this fix");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
