using System.Reflection;
using System.Windows;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

// freex-array-spill-edit-F1: ExecuteCopy's clipCells capture (MainWindow.ClipboardCommands.cs)
// called Sheet.GetCell(r, c) for every source cell and fell back to a literal BlankValue Cell
// whenever GetCell returned null. GetCell deliberately does NOT see the dynamic-array spill
// overlay (see the remarks on Sheet.GetCell) -- a non-anchor spill member (e.g. A2:A5 of a
// =SEQUENCE(5) anchored at A1) has no _cells entry, so every one of those cells was silently
// recorded as blank in the internal clipboard, and the paste destination lost the spilled data
// with no error or warning. Fixed by falling back to Sheet.GetValue (which DOES check the spill
// overlay) instead of a bare BlankValue whenever GetCell returns null.
public sealed class FreeXArraySpillEditF1SpillCopyPasteTests
{
    [Fact]
    public void ExecuteCopy_ThenPaste_OfSpillRange_CarriesNonAnchorSpillValues()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = SpillCopyPasteHarness.Create();

            var a1 = harness.Address(1, 1);
            harness.Sheet.SetFormula(a1, "SEQUENCE(5)");
            harness.Session.RecalculateWorkbook();

            // Source setup sanity (not the fix under test): confirm the spill actually took, and
            // that A2:A5 are genuinely non-anchor spill members with NO _cells entry -- otherwise
            // this test would not be exercising the GetCell-returns-null path at all.
            harness.Sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
            harness.Sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
            harness.Sheet.GetValue(5, 1).Should().Be(new NumberValue(5));
            harness.Sheet.GetCell(2, 1).Should().BeNull("A2 must be a live spill member, not a real cell, for this regression to apply");
            harness.Sheet.GetCell(5, 1).Should().BeNull();

            harness.Select(harness.Range(1, 1, 5, 1)); // A1:A5
            harness.ExecuteCopy();

            harness.Select(harness.Range(1, 2, 1, 2)); // B1
            harness.ExecutePaste();

            // Before the fix, B2:B5 came out blank because A2:A5 were captured as literal
            // BlankValue cells in the internal clipboard (GetCell(r,c) returned null for them).
            harness.Sheet.GetValue(2, 2).Should().Be(new NumberValue(2), "B2 must receive A2's spilled value, not a blank");
            harness.Sheet.GetValue(3, 2).Should().Be(new NumberValue(3));
            harness.Sheet.GetValue(4, 2).Should().Be(new NumberValue(4));
            harness.Sheet.GetValue(5, 2).Should().Be(new NumberValue(5));
        });
    }

    /// <summary>
    /// Sibling no-regression coverage: an ordinary, genuinely empty source cell (no formula, no
    /// live spill anywhere near it) must still paste as blank. The fix's fallback
    /// (<c>sheet?.GetValue(r, c) ?? BlankValue.Instance</c>) must keep returning BlankValue for
    /// this case exactly as the old bare <c>BlankValue.Instance</c> fallback did, so a plain blank
    /// cell copy does not start picking up stray content.
    /// </summary>
    [Fact]
    public void ExecuteCopy_ThenPaste_OfOrdinaryBlankCell_StillPastesBlank()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = SpillCopyPasteHarness.Create();

            // C1 has real text; C2 is a genuinely empty cell -- no formula, no spill overlay entry.
            harness.SetText(1, 3, "hello"); // C1
            harness.Sheet.GetCell(2, 3).Should().BeNull("C2 must be a plain empty cell for this sibling case");
            harness.Sheet.GetValue(2, 3).Should().Be(BlankValue.Instance);

            harness.SetText(1, 5, "stale-e1"); // E1: pre-existing content, must be overwritten
            harness.SetText(2, 5, "stale-e2"); // E2: pre-existing content, must become blank

            harness.Select(harness.Range(1, 3, 2, 3)); // C1:C2
            harness.ExecuteCopy();

            harness.Select(harness.Range(1, 5, 1, 5)); // E1
            harness.ExecutePaste();

            harness.Sheet.GetValue(1, 5).Should().Be(new TextValue("hello"));
            harness.Sheet.GetValue(2, 5).Should().Be(BlankValue.Instance, "an ordinary empty source cell must still paste as blank");
        });
    }

    private sealed class SpillCopyPasteHarness : IDisposable
    {
        private readonly MainWindow _window;

        private SpillCopyPasteHarness(MainWindow window, Workbook workbook, Sheet sheet)
        {
            _window = window;
            Workbook = workbook;
            Sheet = sheet;
        }

        public Workbook Workbook { get; }
        public Sheet Sheet { get; }
        public WorkbookSession Session => _window.Session;

        public CellAddress Address(uint row, uint col) => new(Sheet.Id, row, col);

        public GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol) =>
            new(new CellAddress(Sheet.Id, startRow, startCol), new CellAddress(Sheet.Id, endRow, endCol));

        public void SetText(uint row, uint col, string value) =>
            Sheet.SetCell(new CellAddress(Sheet.Id, row, col), new TextValue(value));

        public void Select(GridRange range)
        {
            _window.SheetGrid.SelectedRanges = null;
            _window.SheetGrid.SelectedRange = range;
        }

        public void ExecuteCopy() => Invoke("ExecuteCopy", false);

        public void ExecutePaste() => Invoke(
            "ExecutePaste",
            PasteMode.All,
            default(PasteSpecialOptions),
            false,
            false);

        private void Invoke(string methodName, params object?[] args)
        {
            var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), methodName);
            method.Invoke(_window, args);
        }

        public static SpillCopyPasteHarness Create()
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
            return new SpillCopyPasteHarness(window, workbookRef.Current, sheet);
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in _window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }
}
