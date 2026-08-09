using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guards for round-12 review findings (bucket Q1, Avalonia/WPF parity deep-dive):
///
///   R12-avalonia-parity-deep-1 - Alt+PageUp/Alt+PageDown scrolled vertically in Avalonia instead
///                                 of moving one screen left/right (WPF/Excel resolve Alt+Page* as
///                                 a horizontal screen-page move via GetHorizontalPageTarget before
///                                 ever considering the plain vertical PageUp/PageDown fallback).
///   R12-avalonia-parity-deep-2 - Enter/Tab commit out of a multi-row/col merged cell landed the
///                                 cursor on a non-anchor member of the SAME merge in Avalonia
///                                 (WPF's AdjustTargetPastMerge steps past the whole merge instead).
///   R12-avalonia-parity-deep-3 - Ctrl+mouse-wheel panned the grid in Avalonia instead of zooming
///                                 (WPF's SheetGrid_MouseWheel treats Ctrl+wheel as Ctrl+Scroll =
///                                 zoom).
///
/// These drive the real production key/pointer-handling code via the internal test seams
/// (RaiseKeyDownForTest / RaiseFormulaBoxKeyDownForTest / RaisePointerWheelChangedForTest) so the
/// WorkbookSession state after each call reflects actual runtime behavior rather than a
/// source-string proxy.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class FreeXR12Q1Tests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── R12-avalonia-parity-deep-1: Alt+PageUp/Alt+PageDown move one screen horizontally ────────

    [Fact]
    public async Task AltPageDown_MovesOneScreenRight_NotDown()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like
            // "Windows" at B1) — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectCell(new CellAddress(sheet.Id, 10, 11)); // K10

            var pageCols = System.Math.Max(1, window.Session.Viewport.ColMetrics.Count - 1);

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.PageDown, KeyModifiers = KeyModifiers.Alt });

            window.Session.ActiveCell.Row.Should().Be(10u,
                "Alt+PageDown must move horizontally, leaving the row unchanged");
            window.Session.ActiveCell.Col.Should().Be((uint)(11 + pageCols),
                "Alt+PageDown must move one full screen to the RIGHT, matching Excel/WPF");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AltPageUp_MovesOneScreenLeft_NotUp()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var pageCols = System.Math.Max(1, window.Session.Viewport.ColMetrics.Count - 1);
            var startCol = (uint)(pageCols + 20);
            window.Session.SelectCell(new CellAddress(sheet.Id, 10, startCol));

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.PageUp, KeyModifiers = KeyModifiers.Alt });

            window.Session.ActiveCell.Row.Should().Be(10u,
                "Alt+PageUp must move horizontally, leaving the row unchanged");
            window.Session.ActiveCell.Col.Should().Be(startCol - (uint)pageCols,
                "Alt+PageUp must move one full screen to the LEFT, matching Excel/WPF");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PlainPageDown_StillMovesVertically()
    {
        // Guards that the horizontal-page fix didn't regress the plain (no-Alt) vertical case.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            var pageRows = System.Math.Max(1, window.Session.Viewport.RowMetrics.Count - 1);

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.PageDown, KeyModifiers = KeyModifiers.None });

            window.Session.ActiveCell.Col.Should().Be(1u, "plain PageDown must not move horizontally");
            window.Session.ActiveCell.Row.Should().Be((uint)(1 + pageRows),
                "plain PageDown must still move one screen DOWN");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── R12-avalonia-parity-deep-2: Enter/Tab commit steps past a multi-row/col merge ───────────

    [Fact]
    public async Task FormulaBoxEnter_CommitOutOfVerticalMerge_StepsPastWholeMerge()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            // Merge A1:A3 (anchor A1); selecting it makes ActiveCell the anchor.
            var anchor = new CellAddress(sheet.Id, 1, 1);
            var mergeEnd = new CellAddress(sheet.Id, 3, 1);
            sheet.AddMergedRegion(new GridRange(anchor, mergeEnd));
            window.Session.SelectRange(new GridRange(anchor, mergeEnd));
            window.Session.BeginFormulaEdit(anchor);

            window.FormulaBoxTextForTest = "hello";
            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.None });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 4, 1),
                "Enter out of a vertical merge must step past the WHOLE merge (to row 4), not land back inside it");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaBoxTab_CommitOutOfHorizontalMerge_StepsPastWholeMerge()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            // Merge A1:C1 (anchor A1); selecting it makes ActiveCell the anchor.
            var anchor = new CellAddress(sheet.Id, 1, 1);
            var mergeEnd = new CellAddress(sheet.Id, 1, 3);
            sheet.AddMergedRegion(new GridRange(anchor, mergeEnd));
            window.Session.SelectRange(new GridRange(anchor, mergeEnd));
            window.Session.BeginFormulaEdit(anchor);

            window.FormulaBoxTextForTest = "hello";
            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Tab, KeyModifiers = KeyModifiers.None });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 1, 4),
                "Tab out of a horizontal merge must step past the WHOLE merge (to column D), not land back inside it");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── R12-avalonia-parity-deep-3: Ctrl+mouse-wheel zooms instead of panning ────────────────────

    [Fact]
    public async Task CtrlMouseWheel_ZoomsIn_InsteadOfPanning()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));
            var originalZoom = window.Session.ZoomPercent;
            var originalTopRow = window.Session.ActiveSheet.ViewTopRow;

            var pointer = new Pointer(1, PointerType.Mouse, isPrimary: true);
            var args = new PointerWheelEventArgs(
                window,
                pointer,
                window.SheetGridHostForTest,
                new Point(10, 10),
                0,
                new PointerPointProperties(),
                KeyModifiers.Control,
                new Vector(0, 1));

            window.RaisePointerWheelChangedForTest(args);

            window.Session.ZoomPercent.Should().Be(originalZoom + 10,
                "Ctrl+wheel scrolling up must zoom IN by one step, matching Excel/WPF's Ctrl+Scroll = zoom");
            window.Session.ActiveSheet.ViewTopRow.Should().Be(originalTopRow,
                "Ctrl+wheel must zoom, not pan the viewport");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PlainMouseWheel_StillPans_NotZoom()
    {
        // Guards that the Ctrl+wheel zoom fix didn't regress the no-modifier pan case.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));
            var originalZoom = window.Session.ZoomPercent;

            var pointer = new Pointer(1, PointerType.Mouse, isPrimary: true);
            var args = new PointerWheelEventArgs(
                window,
                pointer,
                window.SheetGridHostForTest,
                new Point(10, 10),
                0,
                new PointerPointProperties(),
                KeyModifiers.None,
                new Vector(0, -1));

            window.RaisePointerWheelChangedForTest(args);

            window.Session.ZoomPercent.Should().Be(originalZoom, "plain wheel scroll must not change zoom");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }
}
