using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

// R124-cellscmds-multiarea-rowheight-1: mirror of R123_MultiAreaHeaderInsertTests/
// R123_MultiAreaHeaderDeleteTests for Row Height / Column Width / AutoFit / Hide / Unhide. Ctrl+click
// on row/column headers (AddAdditionalRowSelection/AddAdditionalColumnSelection) builds a genuine
// multi-area selection: SheetGrid.SelectedRanges holds every disjoint whole-row/column area while
// SheetGrid.SelectedRange is only the last-clicked (active) one. FormatRowHeightMenuItem_Click,
// FormatColWidthMenuItem_Click, FormatAutoRowMenuItem_Click, FormatAutoColMenuItem_Click,
// ExecuteRowsHidden and ExecuteColumnsHidden used to read only the active SheetGrid.SelectedRange, so
// with rows 2 and 5 Ctrl+click selected, only row 5 (the active area) was resized/hidden/AutoFit and
// row 2 was silently left untouched -- unlike real Excel, which applies the change to every disjoint
// area of a multi-area selection. The fix routes all six handlers through the same
// selection-ranges-aware plumbing Clear Contents/Insert/Delete already use.
public sealed class R124_MultiAreaHeaderRowColumnSizingTests
{
    // Mirrors the private PixelsPerPoint constant in RowColumnSizingPlanner (Sheet.RowHeights stores
    // device pixels at 96 DPI; the Row Height dialog is expressed in points).
    private const double PixelsPerPoint = 96.0 / 72.0;

    [Fact]
    public void ExecuteRowsHidden_MultiAreaHeaderSelection_HidesEveryDisjointRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSizingHarness.Create();

            // Ctrl+click rows 2 and 5 (disjoint) via SelectRow (plain click, row 2) then
            // AddAdditionalRowSelection (Ctrl+click, row 5) -- exactly the real mouse-handler
            // sequence (SheetGrid_MouseDown's Ctrl+click branch on a row header).
            harness.SelectRow(2);
            harness.AddAdditionalRowSelection(5);
            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(2, "two disjoint row-header areas must be tracked before Hide Rows");

            harness.ExecuteRowsHidden(hidden: true);

            // Before the fix, only the active area (row 5) was hidden; row 2 was silently left visible.
            harness.Sheet.HiddenRows.Should().Contain(2, "row 2's disjoint area must also be hidden");
            harness.Sheet.HiddenRows.Should().Contain(5, "row 5 (the active area) must be hidden");
            harness.Sheet.HiddenRows.Should().NotContain(1, "row 1 was never part of the selection");
            harness.Sheet.HiddenRows.Should().NotContain(3, "row 3 was never part of the selection");
        });
    }

    [Fact]
    public void ExecuteColumnsHidden_MultiAreaHeaderSelection_HidesEveryDisjointColumn()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSizingHarness.Create();

            harness.SelectColumn(2);
            harness.AddAdditionalColumnSelection(5);
            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(2);

            harness.ExecuteColumnsHidden(hidden: true);

            harness.Sheet.HiddenCols.Should().Contain(2, "column 2's disjoint area must also be hidden");
            harness.Sheet.HiddenCols.Should().Contain(5, "column 5 (the active area) must be hidden");
            harness.Sheet.HiddenCols.Should().NotContain(1);
            harness.Sheet.HiddenCols.Should().NotContain(3);
        });
    }

    [Fact]
    public void ExecuteColumnsHidden_ThenUnhide_MultiAreaHeaderSelection_UnhidesEveryDisjointColumn()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSizingHarness.Create();

            // Hide columns 1-10 individually first (single-range calls, unaffected by this fix)
            // so Unhide has something real to reveal at both disjoint areas.
            harness.SelectColumn(2);
            harness.ExecuteColumnsHidden(hidden: true);
            harness.SelectColumn(5);
            harness.ExecuteColumnsHidden(hidden: true);
            harness.Sheet.HiddenCols.Should().Contain([2, 5]);

            harness.SelectColumn(2);
            harness.AddAdditionalColumnSelection(5);
            harness.ExecuteColumnsHidden(hidden: false);

            harness.Sheet.HiddenCols.Should().NotContain(2, "column 2's disjoint area must be unhidden too");
            harness.Sheet.HiddenCols.Should().NotContain(5);
        });
    }

    [Fact]
    public void FormatAutoRowMenuItem_Click_MultiAreaHeaderSelection_SizesEveryDisjointRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSizingHarness.Create();

            harness.SelectRow(2);
            harness.AddAdditionalRowSelection(5);
            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(2);

            harness.FormatAutoRowMenuItemClick();

            // CreateAutoFitRowHeightCommand emits one SetRowHeightCommand per row in the plan
            // regardless of whether the estimated size differs from default, so an explicit
            // RowHeights entry for a row is direct proof AutoFit actually ran for that row. Before
            // the fix, only row 5 (the active area) ever got an entry.
            harness.Sheet.RowHeights.Should().ContainKey(2u, "row 2's disjoint area must also be AutoFit");
            harness.Sheet.RowHeights.Should().ContainKey(5u, "row 5 (the active area) must be AutoFit");
            harness.Sheet.RowHeights.Should().NotContainKey(1u, "row 1 was never part of the selection");
            harness.Sheet.RowHeights.Should().NotContainKey(3u, "row 3 was never part of the selection");
        });
    }

    [Fact]
    public void FormatAutoColMenuItem_Click_MultiAreaHeaderSelection_SizesEveryDisjointColumn()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSizingHarness.Create();

            harness.SelectColumn(2);
            harness.AddAdditionalColumnSelection(5);
            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(2);

            harness.FormatAutoColMenuItemClick();

            harness.Sheet.ColumnWidths.Should().ContainKey(2u, "column 2's disjoint area must also be AutoFit");
            harness.Sheet.ColumnWidths.Should().ContainKey(5u, "column 5 (the active area) must be AutoFit");
            harness.Sheet.ColumnWidths.Should().NotContainKey(1u);
            harness.Sheet.ColumnWidths.Should().NotContainKey(3u);
        });
    }

    [Fact]
    public void FormatRowHeightMenuItem_Click_MultiAreaHeaderSelection_SetsHeightAtEveryDisjointRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSizingHarness.Create();

            harness.SelectRow(2);
            harness.AddAdditionalRowSelection(5);
            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(2);

            harness.RunFormatRowHeightThroughDialog("40");

            var expectedHeightPixels = 40.0 * PixelsPerPoint;
            // Before the fix, only row 5 (the active area) got the new height; row 2 silently kept
            // its old (default, unset) height.
            harness.Sheet.RowHeights.Should().ContainKey(2u, "row 2's disjoint area must also be resized");
            harness.Sheet.RowHeights[2].Should().BeApproximately(expectedHeightPixels, 0.001);
            harness.Sheet.RowHeights.Should().ContainKey(5u, "row 5 (the active area) must be resized");
            harness.Sheet.RowHeights[5].Should().BeApproximately(expectedHeightPixels, 0.001);
            harness.Sheet.RowHeights.Should().NotContainKey(1u);
            harness.Sheet.RowHeights.Should().NotContainKey(3u);
        });
    }

    [Fact]
    public void FormatColWidthMenuItem_Click_MultiAreaHeaderSelection_SetsWidthAtEveryDisjointColumn()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSizingHarness.Create();

            harness.SelectColumn(2);
            harness.AddAdditionalColumnSelection(5);
            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(2);

            harness.RunFormatColWidthThroughDialog("30");

            harness.Sheet.ColumnWidths.Should().ContainKey(2u, "column 2's disjoint area must also be resized");
            harness.Sheet.ColumnWidths[2].Should().BeApproximately(30.0, 0.001);
            harness.Sheet.ColumnWidths.Should().ContainKey(5u, "column 5 (the active area) must be resized");
            harness.Sheet.ColumnWidths[5].Should().BeApproximately(30.0, 0.001);
            harness.Sheet.ColumnWidths.Should().NotContainKey(1u);
            harness.Sheet.ColumnWidths.Should().NotContainKey(3u);
        });
    }

    // No-regression sibling: a plain SINGLE active-range Hide Rows (the overwhelmingly common case --
    // no Ctrl+click involved) must keep hiding exactly that one row, unaffected by routing the command
    // construction through the ranges-aware plumbing.
    [Fact]
    public void ExecuteRowsHidden_SingleActiveRange_StillHidesOnlyThatRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSizingHarness.Create();

            harness.SelectRow(3);
            harness.SelectedRanges.Should().BeNull("a plain single-row click must not create a multi-area selection");

            harness.ExecuteRowsHidden(hidden: true);

            harness.Sheet.HiddenRows.Should().ContainSingle().Which.Should().Be(3u);
        });
    }

    // No-regression sibling for Row Height: a plain single active-range selection must still only
    // resize that one row.
    [Fact]
    public void FormatRowHeightMenuItem_Click_SingleActiveRange_StillSetsOnlyThatRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSizingHarness.Create();

            harness.SelectRow(3);
            harness.SelectedRanges.Should().BeNull();

            harness.RunFormatRowHeightThroughDialog("40");

            harness.Sheet.RowHeights.Should().ContainSingle();
            harness.Sheet.RowHeights.Should().ContainKey(3u);
            harness.Sheet.RowHeights[3].Should().BeApproximately(40.0 * PixelsPerPoint, 0.001);
        });
    }

    private sealed class MultiAreaSizingHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Action<uint> _selectRow;
        private readonly Action<uint> _selectColumn;
        private readonly Action<uint> _addAdditionalRowSelection;
        private readonly Action<uint> _addAdditionalColumnSelection;
        private readonly Action<bool> _executeRowsHidden;
        private readonly Action<bool> _executeColumnsHidden;
        private readonly Action<object, RoutedEventArgs> _formatAutoRowMenuItemClick;
        private readonly Action<object, RoutedEventArgs> _formatAutoColMenuItemClick;

        private MultiAreaSizingHarness(MainWindow window, Sheet sheet)
        {
            _window = window;
            Sheet = sheet;

            _selectRow = BindVoidMethod<uint>("SelectRow");
            _selectColumn = BindVoidMethod<uint>("SelectColumn");
            _addAdditionalRowSelection = BindVoidMethod<uint>("AddAdditionalRowSelection");
            _addAdditionalColumnSelection = BindVoidMethod<uint>("AddAdditionalColumnSelection");
            _executeRowsHidden = BindVoidMethod<bool>("ExecuteRowsHidden");
            _executeColumnsHidden = BindVoidMethod<bool>("ExecuteColumnsHidden");
            _formatAutoRowMenuItemClick = BindVoidMethod<object, RoutedEventArgs>("FormatAutoRowMenuItem_Click");
            _formatAutoColMenuItemClick = BindVoidMethod<object, RoutedEventArgs>("FormatAutoColMenuItem_Click");
        }

        private Action<T> BindVoidMethod<T>(string name)
        {
            var method = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), name);
            return method.CreateDelegate<Action<T>>(_window);
        }

        private Action<T1, T2> BindVoidMethod<T1, T2>(string name)
        {
            var method = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), name);
            return method.CreateDelegate<Action<T1, T2>>(_window);
        }

        public Sheet Sheet { get; }

        public IReadOnlyList<GridRange>? SelectedRanges => _window.SheetGrid.SelectedRanges;

        public void SelectRow(uint row) => _selectRow(row);
        public void SelectColumn(uint col) => _selectColumn(col);
        public void AddAdditionalRowSelection(uint row) => _addAdditionalRowSelection(row);
        public void AddAdditionalColumnSelection(uint col) => _addAdditionalColumnSelection(col);
        public void ExecuteRowsHidden(bool hidden) => _executeRowsHidden(hidden);
        public void ExecuteColumnsHidden(bool hidden) => _executeColumnsHidden(hidden);
        public void FormatAutoRowMenuItemClick() => _formatAutoRowMenuItemClick(_window, new RoutedEventArgs());
        public void FormatAutoColMenuItemClick() => _formatAutoColMenuItemClick(_window, new RoutedEventArgs());

        /// <summary>
        /// Drives the real FormatRowHeightMenuItem_Click entry point end to end through the modal
        /// RowHeightDialog: while the ribbon handler's dialog.ShowDialog() call pumps its own nested
        /// dispatcher loop, a Background-priority action queued beforehand locates the now-open
        /// dialog via Window.OwnedWindows, types the height into its input box, and clicks its
        /// default (OK) button -- mirroring the established pattern in
        /// R90_TextToColumnsDestinationPickerSourceRangeTests.
        /// </summary>
        public void RunFormatRowHeightThroughDialog(string height)
        {
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = _window.OwnedWindows.OfType<RowHeightDialog>().Single();
                var heightBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_heightBox");
                heightBox.Text = height;
                var okButton = WpfTestTree.FindVisualDescendants<Button>(dialog).Single(b => b.IsDefault);
                DialogSourceTestSupport.ClickButton(okButton);
            }), System.Windows.Threading.DispatcherPriority.Background);

            DialogSourceTestSupport.InvokePrivateHandler(_window, "FormatRowHeightMenuItem_Click");
            DispatcherTestPump.PumpDispatcher();
        }

        /// <summary>Column counterpart of RunFormatRowHeightThroughDialog above.</summary>
        public void RunFormatColWidthThroughDialog(string width)
        {
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = _window.OwnedWindows.OfType<ColumnWidthDialog>().Single();
                var widthBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_widthBox");
                widthBox.Text = width;
                var okButton = WpfTestTree.FindVisualDescendants<Button>(dialog).Single(b => b.IsDefault);
                DialogSourceTestSupport.ClickButton(okButton);
            }), System.Windows.Threading.DispatcherPriority.Background);

            DialogSourceTestSupport.InvokePrivateHandler(_window, "FormatColWidthMenuItem_Click");
            DispatcherTestPump.PumpDispatcher();
        }

        public static MultiAreaSizingHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                new RecalcEngine(graph, evaluator),
                Array.Empty<IFileAdapter>(),
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                Width = 1280,
                Height = 720
            };

            window.Show();
            var sheet = workbookRef.Current.Sheets[0];

            // AutoFit needs a non-empty used range to have anything to measure (an entire-row/column
            // selection's measurement bounds fall back to the sheet's used range -- see
            // RowColumnSizingPlanner.GetMeasurementBounds). Row markers down column A ("R1".."R10")
            // and column markers across row 12 ("C1".."C10") establish that used range without
            // colliding with each other, mirroring R123_MultiAreaHeaderInsertTests' marker layout.
            for (uint row = 1; row <= 10; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));
            for (uint col = 1; col <= 10; col++)
                sheet.SetCell(new CellAddress(sheet.Id, 12, col), new TextValue($"C{col}"));

            window.UpdateLayout();
            DispatcherTestPump.PumpDispatcher();
            return new MultiAreaSizingHarness(window, sheet);
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in _window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }
}
