using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guards for review4 findings J9/J10/J12/J13/J34/J35/J36 (group
/// C-avalonia-mainwindow, stage 2 - keyboard + fill interactions):
///
///   J9  - Ctrl+Arrow/Ctrl+Home/Ctrl+End/Ctrl+Shift+Arrow did nothing on Avalonia (the sole
///         KeyDown handler excluded any Control-held event from reaching NavigateActiveCell).
///   J10 - F9/Shift+F9/Ctrl+Alt+F9 recalculation shortcuts did not exist on Avalonia.
///   J12 - the fill-handle drag committed via FillCellsCommand (verbatim edge-cell copy), never
///         running Excel's numeric/date linear-fit series detection that AutofillCommand provides.
///   J13 - the border-drag range-move gesture overwrote populated destination cells with no
///         confirmation prompt, unlike the WPF host's OnSelectionMoveRequested.
///   J34 - plain End moved the active cell by a viewport-width jump instead of toggling Excel's
///         sticky "End Mode" (no cursor movement; the next arrow jumps to the data boundary).
///   J35 - Ctrl+Space / Shift+Space / Ctrl+Shift+Space (select columns/rows/all) did not exist.
///   J36 - arrow-key navigation onto a merged region selected a single non-anchor cell instead of
///         snapping to the whole merged rectangle (unlike WPF's SetActiveCell).
///
/// These drive the real production key/gesture-handling code via the internal test seams
/// (RaiseKeyDownForTest / RaiseAutofillDragForTest / RaiseSelectionMoveDragForTest) added
/// alongside the fixes, so the WorkbookSession state after each call reflects the actual runtime
/// behavior rather than a source-string proxy.
///
/// Every case dispatches a <c>Func&lt;Task&lt;bool&gt;&gt;</c> (rather than a plain synchronous
/// Action) because the fixes under test are asynchronous (RaiseKeyDownForTest awaits the real
/// MainWindow_KeyDownAsync method); HeadlessUnitTestSession.Dispatch only overloads on
/// Action/Func&lt;TResult&gt;/Func&lt;Task&lt;TResult&gt;&gt;, so each body returns a dummy `true`.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaMainWindowKeyboardFillStage2Tests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── J9: Ctrl+Arrow/Ctrl+Home/Ctrl+End/Ctrl+Shift+Arrow used-range navigation ─────────────────

    [Fact]
    public async Task CtrlEnd_JumpsToUsedRangeEnd()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
            sheet.SetCell(new CellAddress(sheet.Id, 10, 5), new TextValue("used range end"));
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.End, KeyModifiers = KeyModifiers.Control });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 10, 5),
                "Ctrl+End must jump to the used range's bottom-right corner");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlHome_JumpsToA1()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectCell(new CellAddress(sheet.Id, 25, 12));

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Home, KeyModifiers = KeyModifiers.Control });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 1, 1), "Ctrl+Home must jump to A1");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlDown_JumpsToDataBoundary_NotOneRow()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            // A contiguous data block in column A, rows 1-5.
            for (uint row = 1; row <= 5; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Down, KeyModifiers = KeyModifiers.Control });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 5, 1),
                "Ctrl+Down must jump to the bottom of the contiguous data block, not move by one row");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlShiftDown_ExtendsSelectionToDataBoundary()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            for (uint row = 1; row <= 5; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            await window.RaiseKeyDownForTest(new KeyEventArgs
            {
                Key = Key.Down,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
            });

            window.Session.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1)));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── J10: F9 / Shift+F9 / Ctrl+Alt+F9 recalculation shortcuts ─────────────────────────────────

    [Fact]
    public async Task F9_RecalculatesInManualMode()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            // Enter the precedent/dependent formulas while still in Automatic mode so A2 actually
            // evaluates, then switch to Manual before making it go stale.
            window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 1));
            window.Session.CommitCellText("10").Success.Should().BeTrue();
            window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 2, 1));
            window.Session.CommitCellText("=A1*2").Success.Should().BeTrue();
            sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(20));

            window.Session.ExecuteReviewCommand(new FreeX.Core.Commands.SetCalculationModeCommand(WorkbookCalculationMode.Manual))
                .Success.Should().BeTrue();

            // Editing the precedent while in Manual mode must NOT auto-recalculate A2.
            window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 1));
            window.Session.CommitCellText("100").Success.Should().BeTrue();
            sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(20),
                "Manual mode must not auto-recalculate A2 after editing its precedent");

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.F9, KeyModifiers = KeyModifiers.None });

            sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(200),
                "F9 must force a full recalculation, updating A2 from its now-stale cached value");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShiftF9_RecalculatesActiveSheetOnly()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 1));
            window.Session.CommitCellText("5").Success.Should().BeTrue();
            window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 2, 1));
            window.Session.CommitCellText("=A1+1").Success.Should().BeTrue();
            sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(6));

            window.Session.ExecuteReviewCommand(new FreeX.Core.Commands.SetCalculationModeCommand(WorkbookCalculationMode.Manual))
                .Success.Should().BeTrue();

            window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 1));
            window.Session.CommitCellText("50").Success.Should().BeTrue();

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.F9, KeyModifiers = KeyModifiers.Shift });

            sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(51),
                "Shift+F9 must recalculate the active sheet's formulas");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlAltF9_RecalculatesInManualMode()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 1));
            window.Session.CommitCellText("1").Success.Should().BeTrue();
            window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 2, 1));
            window.Session.CommitCellText("=A1*10").Success.Should().BeTrue();
            sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(10));

            window.Session.ExecuteReviewCommand(new FreeX.Core.Commands.SetCalculationModeCommand(WorkbookCalculationMode.Manual))
                .Success.Should().BeTrue();

            window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 1));
            window.Session.CommitCellText("2").Success.Should().BeTrue();
            sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(10),
                "Manual mode must not auto-recalculate A2 after editing its precedent");

            await window.RaiseKeyDownForTest(new KeyEventArgs
            {
                Key = Key.F9,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            });

            sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(20),
                "Ctrl+Alt+F9 must force a full recalculation just like plain F9");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── J34: plain End toggles End Mode instead of moving the active cell ───────────────────────

    [Fact]
    public async Task PlainEnd_TogglesEndModeWithoutMovingActiveCell()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var start = new CellAddress(sheet.Id, 3, 4);
            window.Session.SelectCell(start);

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.End, KeyModifiers = KeyModifiers.None });

            window.Session.ActiveCell.Should().Be(start, "plain End must not move the active cell - it only toggles End Mode");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EndModeThenArrow_JumpsToDataBoundary()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            for (uint row = 1; row <= 4; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.End, KeyModifiers = KeyModifiers.None });
            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Down, KeyModifiers = KeyModifiers.None });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 4, 1),
                "after End Mode is toggled on, the next plain arrow key must jump to the data boundary like Ctrl+Arrow");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── J35: Ctrl+Space / Shift+Space / Ctrl+Shift+Space selection shortcuts ─────────────────────

    [Fact]
    public async Task CtrlSpace_SelectsWholeColumns()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 2, 2),
                new CellAddress(sheet.Id, 4, 3)));

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Space, KeyModifiers = KeyModifiers.Control });

            window.Session.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 1, 2),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 3)));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShiftSpace_SelectsWholeRows()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 2, 2),
                new CellAddress(sheet.Id, 4, 3)));

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Space, KeyModifiers = KeyModifiers.Shift });

            window.Session.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 2, 1),
                new CellAddress(sheet.Id, 4, CellAddress.MaxCol)));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlShiftSpace_SelectsAllCells()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectCell(new CellAddress(sheet.Id, 2, 2));

            await window.RaiseKeyDownForTest(new KeyEventArgs
            {
                Key = Key.Space,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
            });

            window.Session.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol)));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── J36: arrow navigation onto a merged region snaps to the whole merge ─────────────────────

    [Fact]
    public async Task LeftArrow_OntoMergedRegion_SnapsToWholeMerge()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            // Vertical merge B2:B5 (anchor B2). Active cell at C3; Left arrow lands raw on B3, a
            // non-anchor member.
            var anchor = new CellAddress(sheet.Id, 2, 2);
            var mergeEnd = new CellAddress(sheet.Id, 5, 2);
            sheet.AddMergedRegion(new GridRange(anchor, mergeEnd));
            window.Session.SelectCell(new CellAddress(sheet.Id, 3, 3));

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Left, KeyModifiers = KeyModifiers.None });

            window.Session.SelectedRange.Should().Be(new GridRange(anchor, mergeEnd),
                "landing on a non-anchor merge member via arrow navigation must select the whole merged region, like WPF's SetActiveCell");
            window.Session.ActiveCell.Should().Be(anchor);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RightArrow_OffMergedRegion_SelectsSingleCell()
    {
        // Guards the opposite direction: moving to a plain, non-merged cell must not spuriously
        // select a range.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sheet.Id, 2, 2),
                new CellAddress(sheet.Id, 5, 2)));
            window.Session.SelectCell(new CellAddress(sheet.Id, 8, 8));

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Right, KeyModifiers = KeyModifiers.None });

            window.Session.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 8, 9),
                new CellAddress(sheet.Id, 8, 9)));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── J12: fill-handle drag must run series detection, not a verbatim copy ────────────────────

    [Fact]
    public async Task AutofillDrag_ContinuesNumericSeries_NotVerbatimCopy()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
            var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
            window.Session.SelectRange(source);

            window.RaiseAutofillDragForTest(source, new CellAddress(sheet.Id, 5, 1));

            sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(new NumberValue(3));
            sheet.GetValue(new CellAddress(sheet.Id, 4, 1)).Should().Be(new NumberValue(4));
            sheet.GetValue(new CellAddress(sheet.Id, 5, 1)).Should().Be(new NumberValue(5),
                "the fill-handle drag must continue the 1,2 linear series to 3,4,5, not copy the last source cell (2) verbatim");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── J13: border-drag move must prompt before overwriting destination content ─────────────────

    [Fact]
    public async Task SelectionMoveDrag_OntoPopulatedDestination_PromptsAndAbortsOnNo()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
            var target = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 3));
            sheet.SetCell(source.Start, new TextValue("source"));
            sheet.SetCell(target.Start, new TextValue("existing"));
            window.Session.SelectRange(source);

            var promptShown = false;
            window.ConfirmSelectionMoveOverwriteOverrideForTest = () =>
            {
                promptShown = true;
                return Task.FromResult(false);
            };

            await window.RaiseSelectionMoveDragForTest(source, target);

            promptShown.Should().BeTrue("dropping onto a populated destination must consult the overwrite confirmation");
            sheet.GetValue(target.Start).Should().Be(new TextValue("existing"),
                "answering No to the overwrite prompt must leave the destination cell untouched");
            sheet.GetValue(source.Start).Should().Be(new TextValue("source"),
                "aborting the move must leave the source cell untouched too");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectionMoveDrag_OntoPopulatedDestination_MovesWhenConfirmed()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
            var target = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 3));
            sheet.SetCell(source.Start, new TextValue("source"));
            sheet.SetCell(target.Start, new TextValue("existing"));
            window.Session.SelectRange(source);

            window.ConfirmSelectionMoveOverwriteOverrideForTest = () => Task.FromResult(true);

            await window.RaiseSelectionMoveDragForTest(source, target);

            sheet.GetValue(target.Start).Should().Be(new TextValue("source"),
                "answering Yes to the overwrite prompt must complete the move onto the destination");
            sheet.GetValue(source.Start).Should().BeOfType<BlankValue>("the source cell must be cleared after a successful move");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectionMoveDrag_OntoEmptyDestination_MovesWithoutPrompting()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
            var target = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 3));
            sheet.SetCell(source.Start, new TextValue("source"));
            window.Session.SelectRange(source);

            var promptShown = false;
            window.ConfirmSelectionMoveOverwriteOverrideForTest = () =>
            {
                promptShown = true;
                return Task.FromResult(false);
            };

            await window.RaiseSelectionMoveDragForTest(source, target);

            promptShown.Should().BeFalse("moving onto empty destination cells must not consult the overwrite confirmation");
            sheet.GetValue(target.Start).Should().Be(new TextValue("source"));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectionMoveDrag_CtrlCopy_SelectsCompleteDestinationRange()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var source = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1));
            var target = new GridRange(
                new CellAddress(sheet.Id, 4, 1),
                new CellAddress(sheet.Id, 5, 1));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("top"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("bottom"));
            window.Session.SelectRange(source);

            await window.RaiseSelectionMoveDragForTest(source, target, ctrlHeld: true);

            window.Session.SelectedRange.Should().Be(target,
                "Ctrl-drag copy must leave the complete destination range selected, matching WPF/Excel");
            sheet.GetValue(target.Start).Should().Be(new TextValue("top"));
            sheet.GetValue(target.End).Should().Be(new TextValue("bottom"));
            sheet.GetValue(source.Start).Should().Be(new TextValue("top"),
                "Ctrl-drag copy must preserve the source range");
            sheet.GetValue(source.End).Should().Be(new TextValue("bottom"));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
