using System.Reflection;
using System.Windows;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Free.Shared.AppServices;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

// R127-commands-sort-multiarea-1: SortAscButton_Click/SortDescButton_Click/SortCustomButton_Click
// (MainWindow.DataFilterCommands.cs, also reachable from the Home tab's Sort & Filter dropdown via
// SortAZMenuItem_Click/SortZAMenuItem_Click/SortCustomMenuItem_Click, which delegate straight into
// these) used to key off SheetGrid.SelectedRange alone -- the single "active" area of a Ctrl+click
// multi-area selection -- with no check of SheetGrid.SelectedRanges. With areas A1:A3 and C1:C3
// Ctrl+click selected (C1:C3 active/last-clicked), Sort Ascending used to quietly reorder only
// column C's rows while column A was left completely untouched and unwarned -- worse than a no-op
// if the two areas held related, row-aligned data. Real Excel refuses Sort outright on a multiple
// selection ("This operation is not allowed on multiple selections. Select a single range and click
// the command again."). The renderer now synchronizes selection into WorkbookSession, whose shared
// sort policy rejects the command before any SortCommand is built, mirroring ExecuteCopy/ExecuteCut's
// identical multi-area refusal
// (CreateMultiRangeClipboardError, MainWindow.ClipboardCommands.cs) and the shared Avalonia
// session's SortSelectedRange overloads (WorkbookSession.cs, see
// R127_WorkbookSessionMultiAreaSortRejectionTests in FreeX.App.Services.Tests).
public sealed class R127_MultiAreaSortRejectionTests
{
    [Fact]
    public void SortAscButton_Click_MultiAreaSelection_RejectsWithoutSortingEitherArea()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSortHarness.Create();

            // Deliberately unsorted, all-numeric (no header row detected) so a successful Sort
            // would visibly reorder every area's cell values.
            harness.SetNumber(1, 1, 30); harness.SetNumber(2, 1, 10); harness.SetNumber(3, 1, 20); // A1:A3
            harness.SetNumber(1, 3, 300); harness.SetNumber(2, 3, 100); harness.SetNumber(3, 3, 200); // C1:C3 -- active

            var areaA = harness.Range(1, 1, 3, 1); // A1:A3
            var areaC = harness.Range(1, 3, 3, 3); // C1:C3 -- last-clicked/active area
            harness.SetMultiAreaSelection(active: areaC, all: [areaA, areaC]);

            harness.SortAscButtonClick();

            // Before the fix, column C (the active area) got quietly sorted ascending to
            // 100/200/300 while column A was silently left untouched at 30/10/20 -- neither area
            // may change now that Sort refuses the whole multi-area selection.
            harness.Sheet.GetValue(1, 1).Should().Be(new NumberValue(30), "no area may be reordered on a multi-area selection");
            harness.Sheet.GetValue(2, 1).Should().Be(new NumberValue(10));
            harness.Sheet.GetValue(3, 1).Should().Be(new NumberValue(20));
            harness.Sheet.GetValue(1, 3).Should().Be(new NumberValue(300), "the active area must also stay untouched, not just the non-active one");
            harness.Sheet.GetValue(2, 3).Should().Be(new NumberValue(100));
            harness.Sheet.GetValue(3, 3).Should().Be(new NumberValue(200));

            harness.Messages.Should().ContainSingle(m => m.Contains("Sort") && m.Contains("multiple selected ranges"),
                "Excel surfaces an explicit refusal for Sort on a multiple selection instead of silently doing nothing");
        });
    }

    [Fact]
    public void SortDescButton_Click_MultiAreaSelection_RejectsWithoutSortingEitherArea()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSortHarness.Create();

            harness.SetNumber(1, 1, 10); harness.SetNumber(2, 1, 30); harness.SetNumber(3, 1, 20); // A1:A3
            harness.SetNumber(1, 3, 100); harness.SetNumber(2, 3, 300); harness.SetNumber(3, 3, 200); // C1:C3 -- active

            var areaA = harness.Range(1, 1, 3, 1);
            var areaC = harness.Range(1, 3, 3, 3);
            harness.SetMultiAreaSelection(active: areaC, all: [areaA, areaC]);

            harness.SortDescButtonClick();

            harness.Sheet.GetValue(1, 1).Should().Be(new NumberValue(10));
            harness.Sheet.GetValue(2, 1).Should().Be(new NumberValue(30));
            harness.Sheet.GetValue(3, 1).Should().Be(new NumberValue(20));
            harness.Sheet.GetValue(1, 3).Should().Be(new NumberValue(100));
            harness.Sheet.GetValue(2, 3).Should().Be(new NumberValue(300));
            harness.Sheet.GetValue(3, 3).Should().Be(new NumberValue(200));

            harness.Messages.Should().ContainSingle(m => m.Contains("Sort") && m.Contains("multiple selected ranges"));
        });
    }

    // No-regression sibling: a plain SINGLE active-range Sort (the overwhelmingly common case -- no
    // Ctrl+click involved) must keep sorting exactly that one range, unaffected by the new
    // multi-area check, and must never surface the multi-area refusal message.
    [Fact]
    public void SortAscButton_Click_SingleAreaSelection_StillSortsNormally()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSortHarness.Create();

            harness.SetNumber(1, 1, 30); harness.SetNumber(2, 1, 10); harness.SetNumber(3, 1, 20); // A1:A3
            var areaA = harness.Range(1, 1, 3, 1);
            harness.SetSingleAreaSelection(areaA);

            harness.SortAscButtonClick();

            harness.Sheet.GetValue(1, 1).Should().Be(new NumberValue(10));
            harness.Sheet.GetValue(2, 1).Should().Be(new NumberValue(20));
            harness.Sheet.GetValue(3, 1).Should().Be(new NumberValue(30));
            harness.Messages.Should().BeEmpty("an ordinary single-range Sort must never surface the multi-area refusal");
        });
    }

    private sealed class MultiAreaSortHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly RecordingUserMessageService _messages;
        private readonly Action<object, RoutedEventArgs> _sortAscButtonClick;
        private readonly Action<object, RoutedEventArgs> _sortDescButtonClick;

        private MultiAreaSortHarness(MainWindow window, Workbook workbook, Sheet sheet, RecordingUserMessageService messages)
        {
            _window = window;
            Workbook = workbook;
            Sheet = sheet;
            _messages = messages;

            _sortAscButtonClick = BindVoidMethod<object, RoutedEventArgs>("SortAscButton_Click");
            _sortDescButtonClick = BindVoidMethod<object, RoutedEventArgs>("SortDescButton_Click");
        }

        private Action<T1, T2> BindVoidMethod<T1, T2>(string name)
        {
            var method = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), name);
            return method.CreateDelegate<Action<T1, T2>>(_window);
        }

        public Workbook Workbook { get; }
        public Sheet Sheet { get; }
        public IReadOnlyList<string> Messages => _messages.Messages;

        public GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol) =>
            new(new CellAddress(Sheet.Id, startRow, startCol), new CellAddress(Sheet.Id, endRow, endCol));

        public void SetNumber(uint row, uint col, double value) =>
            Sheet.SetCell(new CellAddress(Sheet.Id, row, col), new NumberValue(value));

        public void SetSingleAreaSelection(GridRange range)
        {
            _window.SheetGrid.SelectedRanges = null;
            _window.SheetGrid.SelectedRange = range;
        }

        // Mirrors the SheetGrid dependency-property state a real Ctrl+click leaves behind when it
        // builds a multi-area cell selection: SelectedRanges holds every disjoint area,
        // SelectedRange is only the last-clicked (active) one. Matches
        // R127_MultiAreaFillCellsTests/R127_MultiAreaMergeCellsTests's own SetMultiAreaSelection.
        public void SetMultiAreaSelection(GridRange active, IReadOnlyList<GridRange> all)
        {
            _window.SheetGrid.SelectedRanges = all;
            _window.SheetGrid.SelectedRange = active;
        }

        public void SortAscButtonClick() => _sortAscButtonClick(_window, new RoutedEventArgs());
        public void SortDescButtonClick() => _sortDescButtonClick(_window, new RoutedEventArgs());

        public static MultiAreaSortHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var messages = new RecordingUserMessageService();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                new RecalcEngine(graph, evaluator),
                Array.Empty<IFileAdapter>(),
                workbookRef,
                workbook,
                messages)
            {
                Width = 1280,
                Height = 720
            };

            window.Show();
            var sheet = workbookRef.Current.Sheets[0];

            window.UpdateLayout();
            DispatcherTestPump.PumpDispatcher();
            return new MultiAreaSortHarness(window, workbookRef.Current, sheet, messages);
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in _window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }

    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public List<string> Messages { get; } = new();

        public void ShowError(string message, string title = "Error") => Messages.Add(message);

        public void ShowWarning(string message, string title = "Warning") => Messages.Add(message);

        public void ShowInfo(string message, string title = "Information") => Messages.Add(message);

        public bool AskYesNo(string message, string title = "Confirm")
        {
            Messages.Add(message);
            return true;
        }

        public UserMessageResult ShowMessage(string message, string title, UserMessageButtons buttons, UserMessageIcon icon)
        {
            Messages.Add(message);
            return UserMessageResult.Ok;
        }
    }
}
