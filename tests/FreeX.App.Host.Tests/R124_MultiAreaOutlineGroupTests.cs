using System.Reflection;
using System.Windows;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

// R124-outlinecmds-multiarea-group-1: mirror of R124_MultiAreaHeaderRowColumnSizingTests for
// Group/Ungroup (Data - Outline). Ctrl+click on row/column headers (AddAdditionalRowSelection/
// AddAdditionalColumnSelection) builds a genuine multi-area selection: SheetGrid.SelectedRanges
// holds every disjoint whole-row/column area while SheetGrid.SelectedRange is only the
// last-clicked (active) one. GroupRowsBtn_Click/UngroupRowsBtn_Click used to read only the active
// SheetGrid.SelectedRange, so with rows 2 and 5 Ctrl+click selected, only row 5 (the active area)
// was grouped/ungrouped and row 2 was silently left untouched -- unlike real Excel, which
// groups/ungroups every disjoint area of a multi-area selection in one Group/Ungroup action. The
// fix routes both handlers through TryExecuteRepeatableCurrentRangesCommand, the same
// GetCurrentSelectionRanges plumbing the Row Height/Column Width/AutoFit/Hide/Unhide multi-area
// fix uses (MainWindow.CommandExecution.cs).
public sealed class R124_MultiAreaOutlineGroupTests
{
    [Fact]
    public void GroupRowsBtn_Click_MultiAreaHeaderSelection_GroupsEveryDisjointRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaOutlineHarness.Create();

            // Ctrl+click rows 2 and 5 (disjoint) via SelectRow (plain click, row 2) then
            // AddAdditionalRowSelection (Ctrl+click, row 5) -- the real mouse-handler sequence.
            harness.SelectRow(2);
            harness.AddAdditionalRowSelection(5);
            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(2, "two disjoint row-header areas must be tracked before Group");

            harness.GroupRowsBtnClick();

            // Before the fix, only row 5 (the active area) was grouped; row 2 was silently left
            // ungrouped.
            harness.Sheet.RowOutlineLevels.Should().ContainKey(2u, "row 2's disjoint area must also be grouped");
            harness.Sheet.RowOutlineLevels[2].Should().Be(1);
            harness.Sheet.RowOutlineLevels.Should().ContainKey(5u, "row 5 (the active area) must be grouped");
            harness.Sheet.RowOutlineLevels[5].Should().Be(1);
            harness.Sheet.RowOutlineLevels.Should().NotContainKey(1u, "row 1 was never part of the selection");
            harness.Sheet.RowOutlineLevels.Should().NotContainKey(3u, "row 3 was never part of the selection");
        });
    }

    [Fact]
    public void GroupRowsBtn_Click_MultiAreaHeaderColumnSelection_GroupsEveryDisjointColumn()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaOutlineHarness.Create();

            harness.SelectColumn(2);
            harness.AddAdditionalColumnSelection(5);
            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(2);

            harness.GroupRowsBtnClick();

            harness.Sheet.ColOutlineLevels.Should().ContainKey(2u, "column 2's disjoint area must also be grouped");
            harness.Sheet.ColOutlineLevels[2].Should().Be(1);
            harness.Sheet.ColOutlineLevels.Should().ContainKey(5u, "column 5 (the active area) must be grouped");
            harness.Sheet.ColOutlineLevels[5].Should().Be(1);
            harness.Sheet.ColOutlineLevels.Should().NotContainKey(1u);
            harness.Sheet.ColOutlineLevels.Should().NotContainKey(3u);
        });
    }

    [Fact]
    public void UngroupRowsBtn_Click_MultiAreaHeaderSelection_UngroupsEveryDisjointRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaOutlineHarness.Create();

            // Group rows 2 and 5 individually first (single-range calls, unaffected by this fix)
            // so Ungroup has something real to remove at both disjoint areas.
            var ctx = new TestCommandContext(harness.Workbook);
            new GroupRowsCommand(harness.Sheet.Id, 2, 2, 1, preserveExistingHierarchy: true).Apply(ctx);
            new GroupRowsCommand(harness.Sheet.Id, 5, 5, 1, preserveExistingHierarchy: true).Apply(ctx);
            harness.Sheet.RowOutlineLevels.Should().ContainKey(2u);
            harness.Sheet.RowOutlineLevels.Should().ContainKey(5u);

            harness.SelectRow(2);
            harness.AddAdditionalRowSelection(5);
            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(2);

            harness.UngroupRowsBtnClick();

            // Before the fix, only row 5 (the active area) was ungrouped; row 2 silently stayed
            // grouped.
            harness.Sheet.RowOutlineLevels.Should().NotContainKey(2u, "row 2's disjoint area must also be ungrouped");
            harness.Sheet.RowOutlineLevels.Should().NotContainKey(5u, "row 5 (the active area) must be ungrouped");
        });
    }

    // No-regression sibling: a plain SINGLE active-range Group (the overwhelmingly common case --
    // no Ctrl+click involved) must keep grouping exactly that one range, unaffected by routing the
    // command construction through the ranges-aware plumbing.
    [Fact]
    public void GroupRowsBtn_Click_SingleActiveRange_StillGroupsOnlyThatRange()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaOutlineHarness.Create();

            harness.SelectRow(3);
            harness.SelectedRanges.Should().BeNull("a plain single-row click must not create a multi-area selection");

            harness.GroupRowsBtnClick();

            harness.Sheet.RowOutlineLevels.Should().ContainSingle();
            harness.Sheet.RowOutlineLevels.Should().ContainKey(3u).WhoseValue.Should().Be(1);
        });
    }

    private sealed class MultiAreaOutlineHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Action<uint> _selectRow;
        private readonly Action<uint> _selectColumn;
        private readonly Action<uint> _addAdditionalRowSelection;
        private readonly Action<uint> _addAdditionalColumnSelection;
        private readonly Action<object, RoutedEventArgs> _groupRowsBtnClick;
        private readonly Action<object, RoutedEventArgs> _ungroupRowsBtnClick;

        private MultiAreaOutlineHarness(MainWindow window, Workbook workbook, Sheet sheet)
        {
            _window = window;
            Workbook = workbook;
            Sheet = sheet;

            _selectRow = BindVoidMethod<uint>("SelectRow");
            _selectColumn = BindVoidMethod<uint>("SelectColumn");
            _addAdditionalRowSelection = BindVoidMethod<uint>("AddAdditionalRowSelection");
            _addAdditionalColumnSelection = BindVoidMethod<uint>("AddAdditionalColumnSelection");
            _groupRowsBtnClick = BindVoidMethod<object, RoutedEventArgs>("GroupRowsBtn_Click");
            _ungroupRowsBtnClick = BindVoidMethod<object, RoutedEventArgs>("UngroupRowsBtn_Click");
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

        public Workbook Workbook { get; }
        public Sheet Sheet { get; }

        public IReadOnlyList<GridRange>? SelectedRanges => _window.SheetGrid.SelectedRanges;

        public void SelectRow(uint row) => _selectRow(row);
        public void SelectColumn(uint col) => _selectColumn(col);
        public void AddAdditionalRowSelection(uint row) => _addAdditionalRowSelection(row);
        public void AddAdditionalColumnSelection(uint col) => _addAdditionalColumnSelection(col);
        public void GroupRowsBtnClick() => _groupRowsBtnClick(_window, new RoutedEventArgs());
        public void UngroupRowsBtnClick() => _ungroupRowsBtnClick(_window, new RoutedEventArgs());

        public static MultiAreaOutlineHarness Create()
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

            window.UpdateLayout();
            DispatcherTestPump.PumpDispatcher();
            return new MultiAreaOutlineHarness(window, workbookRef.Current, sheet);
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in _window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }
}
