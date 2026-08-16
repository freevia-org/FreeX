using System.Reflection;
using Free.Shared.AppServices;
using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for three round-82 findings in the WPF host's clipboard commands
/// (<c>MainWindow.ClipboardCommands.cs</c>):
///
///  - R82-commands-cutcopy-clipboard-5-1 (<c>ExecuteCopy</c>): a non-conforming multi-area
///    (Ctrl+click) Copy -- one whose areas share neither the same rows nor the same columns --
///    and ANY multi-area Cut must be rejected like real Excel ("That command cannot be used on
///    multiple selections"), instead of silently placing a nonsensical bounding-box marquee/
///    clipboard payload.
///  - R82-commands-cutcopy-clipboard-5-2 (<c>ExecuteCopy</c>'s internal-clipboard capture):
///    copying a filtered range must skip AutoFilter-hidden rows (matching Excel's
///    visible-cells-only restriction for a filtered range) while still including a plain
///    manually-hidden row (Excel does NOT restrict a non-filtered copy to visible rows).
///  - R82-commands-cutcopy-clipboard-5-3 (<c>ExecutePaste</c>): once a Cut's first Cut+Paste move
///    completes, the OS clipboard must be invalidated so a further Ctrl+V elsewhere cannot
///    silently paste the same cut content a second time via the external-text fallback.
/// </summary>
public sealed class R82_ClipboardCutCopyGuardTests
{
    [Fact]
    public void ExecuteCopy_NonConformingMultiAreaSelection_IsRejected()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = new Harness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var sheetId = sheet.Id;

            sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(1));
            sheet.SetCell(new CellAddress(sheetId, 5, 2), new NumberValue(2));
            sheet.SetCell(new CellAddress(sheetId, 6, 4), new NumberValue(3));

            // A1:A3 and B5:D6 -- these share neither the same rows nor the same columns, so real
            // Excel refuses to copy them as a multiple selection.
            var areaA = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 1));
            var areaB = new GridRange(new CellAddress(sheetId, 5, 2), new CellAddress(sheetId, 6, 4));

            harness.Grid.SelectedRanges = new[] { areaA, areaB };
            harness.Grid.SelectedRange = areaB;

            harness.InvokeExecuteCopy(isCut: false);

            harness.Messages.Should().ContainSingle(m => m.Contains("multiple selected ranges"),
                "real Excel refuses Copy on a non-conforming multi-area selection");
            harness.Grid.ClipboardRange.Should().BeNull(
                "a rejected Copy must not start a marching-ants marquee");
            harness.HasInternalClipboard.Should().BeFalse(
                "a rejected Copy must not populate the internal clipboard");
        });
    }

    [Fact]
    public void ExecuteCopy_ConformingMultiAreaSelection_StillSucceeds()
    {
        // Sibling no-regression: a same-row multi-area selection (Excel's allowed "combine
        // side-by-side" shape, already covered by the pre-existing R49 multi-area copy support)
        // must still copy normally -- the new rejection guard must not disturb it.
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = new Harness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var sheetId = sheet.Id;

            sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(7));
            sheet.SetCell(new CellAddress(sheetId, 1, 3), new NumberValue(9));

            var areaA = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)); // A1
            var areaC = new GridRange(new CellAddress(sheetId, 1, 3), new CellAddress(sheetId, 1, 3)); // C1

            harness.Grid.SelectedRanges = new[] { areaA, areaC };
            harness.Grid.SelectedRange = areaC;

            harness.InvokeExecuteCopy(isCut: false);

            harness.Messages.Should().BeEmpty(
                "a conforming (same-row) multi-area Copy must not be rejected");
            harness.Grid.ClipboardRange.Should().NotBeNull(
                "a conforming multi-area Copy still starts the marquee");
            harness.HasInternalClipboard.Should().BeTrue();
        });
    }

    [Fact]
    public void ExecuteCopy_ConformingMultiAreaSelection_CutIsStillRejected()
    {
        // Real Excel rejects Cut on EVERY multi-area selection, even a same-row/-column shape
        // Copy would accept -- unlike Copy, Cut has no "combine side-by-side/stacked" exception.
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = new Harness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var sheetId = sheet.Id;

            sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(7));
            sheet.SetCell(new CellAddress(sheetId, 1, 3), new NumberValue(9));

            var areaA = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));
            var areaC = new GridRange(new CellAddress(sheetId, 1, 3), new CellAddress(sheetId, 1, 3));

            harness.Grid.SelectedRanges = new[] { areaA, areaC };
            harness.Grid.SelectedRange = areaC;

            harness.InvokeExecuteCopy(isCut: true);

            harness.Messages.Should().ContainSingle(m => m.Contains("multiple selected ranges"),
                "real Excel refuses Cut on any multi-area selection, regardless of its shape");
            harness.Grid.ClipboardRange.Should().BeNull();
            harness.HasInternalClipboard.Should().BeFalse();
        });
    }

    [Fact]
    public void ExecuteCopy_FilteredRange_InternalPasteExcludesFilterHiddenRow()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = new Harness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var sheetId = sheet.Id;

            sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(1));
            sheet.SetCell(new CellAddress(sheetId, 2, 1), new NumberValue(2));
            sheet.SetCell(new CellAddress(sheetId, 3, 1), new NumberValue(3));
            sheet.FilterHiddenRows.Add(2); // row 2 hidden by an active AutoFilter

            harness.Grid.SelectedRanges = null;
            harness.Grid.SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 1)); // A1:A3

            harness.InvokeExecuteCopy(isCut: false);

            harness.Grid.SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 5), new CellAddress(sheetId, 1, 5)); // E1
            harness.InvokeExecutePaste();

            // Assert the paste landed before dereferencing. The null-forgiving "!" here used to turn
            // a copy/paste that produced nothing into a bare NullReferenceException, which reads as a
            // defect in the filter logic under test when the actual cause is that the copy never made
            // it to the clipboard -- that is one global OS resource, and the full test gate has other
            // processes contending for it.
            sheet.GetCell(1, 5).Should().NotBeNull(
                "the internal paste must have written A1's value to E1; a null here means the copy " +
                "or paste did not complete, not that filtered rows were mishandled");
            sheet.GetCell(1, 5)!.Value.Should().Be(new NumberValue(1));
            (sheet.GetCell(2, 5)?.Value ?? BlankValue.Instance).Should().Be(
                BlankValue.Instance,
                "row 2 is AutoFilter-hidden and must not be reproduced by an internal paste");
            sheet.GetCell(3, 5).Should().NotBeNull(
                "the internal paste must have written A3's value to E3");
            sheet.GetCell(3, 5)!.Value.Should().Be(new NumberValue(3));
        });
    }

    [Fact]
    public void ExecuteCopy_ManuallyHiddenRow_InternalPasteStillIncludesIt()
    {
        // Sibling no-regression: Excel's visible-cells-only restriction applies ONLY to a
        // filtered range -- a plain manually-hidden row (Format > Hide Row, no AutoFilter
        // involved) must still be copied.
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = new Harness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var sheetId = sheet.Id;

            sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(1));
            sheet.SetCell(new CellAddress(sheetId, 2, 1), new NumberValue(2));
            sheet.SetCell(new CellAddress(sheetId, 3, 1), new NumberValue(3));
            sheet.HiddenRows.Add(2); // row 2 manually hidden (Format > Hide Row)

            harness.Grid.SelectedRanges = null;
            harness.Grid.SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 1)); // A1:A3

            harness.InvokeExecuteCopy(isCut: false);

            harness.Grid.SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 5), new CellAddress(sheetId, 1, 5)); // E1
            harness.InvokeExecutePaste();

            sheet.GetCell(1, 5)!.Value.Should().Be(new NumberValue(1));
            sheet.GetCell(2, 5)!.Value.Should().Be(
                new NumberValue(2),
                "a manually-hidden row (not AutoFilter-hidden) must still be copied by an internal paste");
            sheet.GetCell(3, 5)!.Value.Should().Be(new NumberValue(3));
        });
    }

    [Fact]
    public void CutThenPaste_SecondPasteViaOsClipboardFallback_DoesNothing()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = new Harness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var sheetId = sheet.Id;
            var a1 = new CellAddress(sheetId, 1, 1);
            var b1 = new CellAddress(sheetId, 1, 2);
            var c1 = new CellAddress(sheetId, 1, 3);
            sheet.SetCell(a1, new NumberValue(5));

            harness.Grid.SelectedRanges = null;
            harness.Grid.SelectedRange = new GridRange(a1, a1);
            harness.InvokeExecuteCopy(isCut: true);

            harness.Grid.SelectedRange = new GridRange(b1, b1);
            harness.InvokeExecutePaste();

            sheet.GetCell(b1)!.Value.Should().Be(new NumberValue(5));
            sheet.GetCell(a1).Should().BeNull("the source cell was moved away by the Cut+Paste");

            // A second Ctrl+V elsewhere must be a no-op: real Excel invalidates the clipboard
            // once a Cut-then-Paste move completes.
            harness.Grid.SelectedRange = new GridRange(c1, c1);
            harness.InvokeExecutePaste();

            (sheet.GetCell(c1)?.Value ?? BlankValue.Instance).Should().Be(
                BlankValue.Instance,
                "a Cut must not be pasteable a second time via the OS-clipboard external-text fallback");
        });
    }

    [Fact]
    public void CopyThenPaste_SecondPasteStillWorks()
    {
        // Sibling no-regression: an ordinary Copy (not a Cut) must remain repeatedly pasteable --
        // only a Cut's OS-clipboard payload is meant to be invalidated once its move completes.
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = new Harness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var sheetId = sheet.Id;
            var a1 = new CellAddress(sheetId, 1, 1);
            var b1 = new CellAddress(sheetId, 1, 2);
            var c1 = new CellAddress(sheetId, 1, 3);
            sheet.SetCell(a1, new NumberValue(5));

            harness.Grid.SelectedRanges = null;
            harness.Grid.SelectedRange = new GridRange(a1, a1);
            harness.InvokeExecuteCopy(isCut: false);

            harness.Grid.SelectedRange = new GridRange(b1, b1);
            harness.InvokeExecutePaste();
            harness.Grid.SelectedRange = new GridRange(c1, c1);
            harness.InvokeExecutePaste();

            sheet.GetCell(b1)!.Value.Should().Be(new NumberValue(5));
            sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(5));
        });
    }

    private sealed class Harness : IDisposable
    {
        private readonly MethodInfo _executeCopy;
        private readonly MethodInfo _executePaste;
        private readonly FieldInfo _clipboardSessionField;

        public MainWindow Window { get; }
        public Workbook Workbook { get; }
        public GridView Grid => (GridView)Window.FindName("SheetGrid");
        public List<string> Messages { get; } = [];

        public Harness()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            Window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                new RecordingUserMessageService(Messages));

            Window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied
            // workbook with a fresh one unless adopting a shared document -- capture the *live*
            // workbook afterward, mirroring every other MainWindow-construction test harness.
            Workbook = workbookRef.Current;

            _executeCopy = typeof(MainWindow).GetMethod("ExecuteCopy", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteCopy");
            _executePaste = typeof(MainWindow).GetMethod("ExecutePaste", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExecutePaste");
            _clipboardSessionField = typeof(MainWindow)
                .GetField("_workbookClipboardSession", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_workbookClipboardSession");
        }

        public bool HasInternalClipboard =>
            ((WorkbookClipboardSession)_clipboardSessionField.GetValue(Window)!).HasContent;

        public void InvokeExecuteCopy(bool isCut)
        {
            _executeCopy.Invoke(Window, [isCut]);
            PumpDispatcher();
        }

        public void InvokeExecutePaste()
        {
            _executePaste.Invoke(Window, [PasteMode.All, default(PasteSpecialOptions), false, false]);
            PumpDispatcher();
        }

        public void Dispose()
        {
            Window.SuppressNextClosePrompt();
            Window.Close();
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>Records every <see cref="ShowWarning"/> call so tests can assert a rejected
    /// command actually surfaced the expected error, instead of silently no-op'ing.</summary>
    private sealed class RecordingUserMessageService(List<string> messages) : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") => messages.Add(message);
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => false;
        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Ok;
    }
}
