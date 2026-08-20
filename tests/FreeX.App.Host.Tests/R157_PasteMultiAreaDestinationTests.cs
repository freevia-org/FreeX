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

// freex-selection-model-F1: ExecutePaste (MainWindow.ClipboardCommands.cs, the internal-clipboard
// branch of CreatePasteCommand) used to key off SheetGrid.SelectedRange alone -- the single "active"
// area of a Ctrl+click multi-area destination selection -- with no check of SheetGrid.SelectedRanges
// at all. Copy a range, then Ctrl+click TWO equal-sized destination blocks and Ctrl+V: only the
// active (last-clicked) block got the pasted content; the other selected block was silently left
// untouched, with no error or warning. This is the same bug shape R49 (Ctrl+Enter fill) and R127
// (Merge/Sort) were fixed for elsewhere in this file via GetCurrentSelectionRanges -- Paste was never
// routed through it. The Avalonia shell's WorkbookSession.PasteSpecialClipboardAtActiveCell /
// PasteInternalClipboardToSelectedRanges already has this exact, tested behavior (tile into every
// selected area when all sizes match the clipboard's; reject the whole paste -- with an explicit
// "does not support multiple selected ranges yet." message -- when they don't, or when the source is
// a Cut), and the WPF host is now brought into parity with it.
public sealed class R157_PasteMultiAreaDestinationTests
{
    [Fact]
    public void ExecutePaste_MultiAreaDestination_MatchingSizes_FillsEveryArea()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = PasteMultiAreaHarness.Create();

            // Copy a 1x2 source block (C3:D3).
            harness.SetText(3, 3, "West"); // C3
            harness.SetText(3, 4, "East"); // D3
            harness.Select(harness.Range(3, 3, 3, 4));
            harness.ExecuteCopy();

            // Ctrl+click two equal-sized (1x2) destination blocks: E5:F5 and H8:I8, H8:I8 active
            // (last-clicked).
            var firstTarget = harness.Range(5, 5, 5, 6); // E5:F5
            var secondTarget = harness.Range(8, 8, 8, 9); // H8:I8 -- active
            harness.SetMultiAreaSelection(active: secondTarget, all: [firstTarget, secondTarget]);

            harness.ExecutePaste();

            // Before the fix, only the active area (H8:I8) received the pasted content; E5:F5 was
            // silently left untouched.
            harness.Sheet.GetValue(5, 5).Should().Be(new TextValue("West"), "the non-active area must also receive the paste");
            harness.Sheet.GetValue(5, 6).Should().Be(new TextValue("East"));
            harness.Sheet.GetValue(8, 8).Should().Be(new TextValue("West"), "the active area must still receive the paste");
            harness.Sheet.GetValue(8, 9).Should().Be(new TextValue("East"));

            harness.Messages.Should().BeEmpty("a matching-size multi-area paste must succeed without any error");
        });
    }

    [Fact]
    public void ExecutePaste_MultiAreaDestination_MismatchedSize_RejectsWithoutPastingEitherArea()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = PasteMultiAreaHarness.Create();

            // Copy a 1x2 source block (A1:B1).
            harness.SetText(1, 1, "West"); // A1
            harness.SetText(1, 2, "East"); // B1
            harness.Select(harness.Range(1, 1, 1, 2));
            harness.ExecuteCopy();

            // One destination area is the right size (1x2), the other is not (1x1) -- Excel/Avalonia
            // refuse the WHOLE multi-area paste rather than partially fill only the matching area.
            harness.SetText(1, 5, "old-e1"); // E1 (1x1 mismatched area)
            harness.SetText(1, 8, "old-h1"); // H1
            harness.SetText(1, 9, "old-i1"); // I1
            var mismatchedTarget = harness.Range(1, 5, 1, 5); // E1
            var matchingTarget = harness.Range(1, 8, 1, 9); // H1:I1 -- active
            harness.SetMultiAreaSelection(active: matchingTarget, all: [mismatchedTarget, matchingTarget]);

            harness.ExecutePaste();

            harness.Sheet.GetValue(1, 5).Should().Be(new TextValue("old-e1"), "the mismatched area must stay untouched");
            harness.Sheet.GetValue(1, 8).Should().Be(new TextValue("old-h1"), "no area may be partially filled when another area's size doesn't match");
            harness.Sheet.GetValue(1, 9).Should().Be(new TextValue("old-i1"));

            harness.Messages.Should().ContainSingle(
                m => m.Contains("multiple selected ranges"),
                "Excel surfaces an explicit refusal instead of silently doing nothing or partially pasting");
        });
    }

    /// <summary>
    /// No-regression sibling: an ordinary SINGLE-area destination selection still tiles the copied
    /// block across the whole selection exactly as before this fix (the multi-area guard must not
    /// affect the single-area path at all).
    /// </summary>
    [Fact]
    public void ExecutePaste_SingleAreaDestination_StillTilesAcrossWholeSelection()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = PasteMultiAreaHarness.Create();

            harness.SetText(1, 1, "West"); // A1
            harness.SetText(1, 2, "East"); // B1
            harness.Select(harness.Range(1, 1, 1, 2));
            harness.ExecuteCopy();

            // A single 1x4 destination selection is an exact 2x multiple of the 1x2 source -- tiles.
            harness.SetSingleAreaSelection(harness.Range(3, 1, 3, 4)); // A3:D3
            harness.ExecutePaste();

            harness.Sheet.GetValue(3, 1).Should().Be(new TextValue("West")); // A3
            harness.Sheet.GetValue(3, 2).Should().Be(new TextValue("East")); // B3
            harness.Sheet.GetValue(3, 3).Should().Be(new TextValue("West")); // C3 (tile 2)
            harness.Sheet.GetValue(3, 4).Should().Be(new TextValue("East")); // D3

            harness.Messages.Should().BeEmpty();
        });
    }

    private sealed class PasteMultiAreaHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly RecordingUserMessageService _messages;

        private PasteMultiAreaHarness(MainWindow window, Workbook workbook, Sheet sheet, RecordingUserMessageService messages)
        {
            _window = window;
            Workbook = workbook;
            Sheet = sheet;
            _messages = messages;
        }

        public Workbook Workbook { get; }
        public Sheet Sheet { get; }
        public IReadOnlyList<string> Messages => _messages.Messages;

        public GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol) =>
            new(new CellAddress(Sheet.Id, startRow, startCol), new CellAddress(Sheet.Id, endRow, endCol));

        public void SetText(uint row, uint col, string value) =>
            Sheet.SetCell(new CellAddress(Sheet.Id, row, col), new TextValue(value));

        public void Select(GridRange range) => SetSingleAreaSelection(range);

        public void SetSingleAreaSelection(GridRange range)
        {
            _window.SheetGrid.SelectedRanges = null;
            _window.SheetGrid.SelectedRange = range;
        }

        // Mirrors the SheetGrid dependency-property state a real Ctrl+click leaves behind when it
        // builds a multi-area cell selection: SelectedRanges holds every disjoint area, SelectedRange
        // is only the last-clicked (active) one. Matches R127_MultiAreaSortRejectionTests's own
        // SetMultiAreaSelection.
        public void SetMultiAreaSelection(GridRange active, IReadOnlyList<GridRange> all)
        {
            _window.SheetGrid.SelectedRanges = all;
            _window.SheetGrid.SelectedRange = active;
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

        public static PasteMultiAreaHarness Create()
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
            return new PasteMultiAreaHarness(window, workbookRef.Current, sheet, messages);
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
