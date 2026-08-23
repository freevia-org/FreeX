using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Focused regression tests for FreeX cleanup batch MED1 (round-10 MED/LOW findings), one per
/// finding:
///
///   P29 - the Avalonia shell never rendered a worksheet's Page Layout ▸ Background picture (no
///         consumer of Sheet.BackgroundImage anywhere in src/FreeX.App.Avalonia), and CreateCell
///         painted every unfilled cell opaque white, which would have occluded any future underlay.
///   P35 - Alt+Down (Excel's "open the active dropdown" shortcut) opened only the data-validation
///         dropdown; there was no AutoFilter fallback, so a keyboard-only user on a filtered header
///         cell had no way at all to open the column's filter dropdown.
///   P36 - the worksheet cell context menu had no keyboard trigger (Shift+F10/Menu key were handled
///         only for sheet tabs), so a keyboard-only user could never reach Cut/Copy/Format Cells/etc.
///         over the grid.
///   P37 - ShowEditIssue only recolored/rewrote _statusText.Text with no live-region announcement,
///         so a screen-reader user got no signal at all when an edit/validation commit failed.
///   P47 - Avalonia copy placed only plain text on the OS clipboard (no HTML table fragment), so
///         formatting (bold/fill/merges) was lost when pasting into an HTML-aware destination,
///         unlike the WPF host's M7 CF_HTML export.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class FreeXCleanupMED1Tests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── P29: worksheet background image must render behind the grid, and unfilled cells must not ──
    // ── occlude it with an opaque white base fill ────────────────────────────────────────────────

    [Fact]
    public async Task BuildSheetGrid_RendersTiledBackgroundImage_AndUnfilledCellsDoNotOccludeIt()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
            sheet.BackgroundImage = new WorksheetBackgroundImage(MinimalPngBytes(), "image/png", "bg.png");
            ForceViewportRefresh(window);

            var built = window.RebuildSheetGridForTest();
            var grid = FindInnerGrid(built);

            // The sheet grid's own Background must be a real tiled image brush over the background
            // picture, not the plain white brush used when there is no background image.
            grid.Background.Should().BeOfType<ImageBrush>(
                "a worksheet background image must produce a real ImageBrush on the sheet grid, " +
                "not the default opaque white fill");
            var imageBrush = (ImageBrush)grid.Background!;
            imageBrush.TileMode.Should().Be(TileMode.Tile, "Page Layout backgrounds tile across the sheet");

            // The unfilled A1 cell (no explicit fill color) must NOT paint an opaque white base —
            // otherwise it would occlude the tiled background picture directly behind it.
            var cellBorder = FindCellBorder(grid, window.Session, 1, 1);
            cellBorder.Should().NotBeNull();
            cellBorder!.Background.Should().BeNull(
                "an unfilled cell must let the worksheet background image show through instead of " +
                "painting an opaque white base fill over it");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BuildSheetGrid_WithNoBackgroundImage_StillPaintsWhiteBaseFill()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
            sheet.BackgroundImage = null;
            ForceViewportRefresh(window);

            var built = window.RebuildSheetGridForTest();
            var grid = FindInnerGrid(built);

            grid.Background.Should().Be(Brushes.White, "no background image means the grid keeps its plain white base");

            var cellBorder = FindCellBorder(grid, window.Session, 1, 1);
            cellBorder.Should().NotBeNull();
            cellBorder!.Background.Should().Be(Brushes.White,
                "existing (no-background-image) behavior must be preserved: unfilled cells stay opaque white");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── P35: Alt+Down must fall back to the AutoFilter dropdown when there is no data-validation ──
    // ── dropdown and the active cell is a filter-button (header) cell ───────────────────────────

    [Fact]
    public async Task AltDown_OnAutoFilterHeaderCell_OpensAutoFilterDropdown_WhenNoValidationDropdown()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.UpdateLayout();

            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Row1"));

            var headerRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
            window.Session.SelectRange(headerRange);
            var toggleResult = window.Session.ToggleSelectedRangeAutoFilter();
            toggleResult.Success.Should().BeTrue("test setup must succeed in creating a real AutoFilter range");

            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));
            window.RebuildSheetGridForTest();

            var args = new KeyEventArgs { Key = Key.Down, KeyModifiers = KeyModifiers.Alt };
            await window.RaiseKeyDownForTest(args);

            args.Handled.Should().BeTrue(
                "Alt+Down over an AutoFilter header cell must be handled by falling back to the " +
                "column's filter dropdown when there is no data-validation dropdown to open — " +
                "previously this key press did nothing at all for a keyboard-only user");
            window.AutoFilterFlyoutOpenForTest.Should().BeTrue(
                "the handled key must open the actual AutoFilter flyout, not the adjacent-text pick list");
            window.AutoFilterFlyoutPlacementTargetAutomationIdForTest.Should().Be("AutoFilterButton_1_1",
                "the keyboard route must anchor the physical flyout to the live rendered header button");
            window.DataValidationDropdownOpenForTest.Should().BeFalse();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AltDown_OnPlainTextColumn_OpensTextEntryPickListAfterAutoFilterFallback()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.UpdateLayout();

            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("North"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("South"));
            window.Session.SelectCell(new CellAddress(sheet.Id, 3, 1));
            window.RebuildSheetGridForTest();

            var args = new KeyEventArgs { Key = Key.Down, KeyModifiers = KeyModifiers.Alt };
            await window.RaiseKeyDownForTest(args);

            args.Handled.Should().BeTrue();
            window.DataValidationDropdownOpenForTest.Should().BeTrue();
            window.AutoFilterFlyoutOpenForTest.Should().BeFalse();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AltDown_OnPlainCell_WithNoDropdownOrAutoFilter_LeavesKeyUnhandled()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Plain"));
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));
            window.RebuildSheetGridForTest();

            var args = new KeyEventArgs { Key = Key.Down, KeyModifiers = KeyModifiers.Alt };
            await window.RaiseKeyDownForTest(args);

            args.Handled.Should().BeFalse(
                "a plain cell with no data-validation dropdown and no AutoFilter must leave Alt+Down " +
                "unhandled, matching existing (pre-fix) behavior for the ordinary case");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── P36: Shift+F10 / Menu key over the worksheet grid must open the cell context menu ───────

    [Fact]
    public async Task ShiftF10_OverWorksheetGrid_OpensCellContextMenu()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.UpdateLayout();

            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));
            window.RebuildSheetGridForTest();

            var args = new KeyEventArgs { Key = Key.F10, KeyModifiers = KeyModifiers.Shift };
            await window.RaiseKeyDownForTest(args);

            args.Handled.Should().BeTrue(
                "Shift+F10 over the worksheet grid must open the cell context menu for a keyboard-only " +
                "user — previously MainWindow_KeyDownAsync had no case for it at all over the grid");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MenuKey_OverWorksheetGrid_OpensCellContextMenu()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.UpdateLayout();

            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));
            window.RebuildSheetGridForTest();

            var args = new KeyEventArgs { Key = Key.Apps, KeyModifiers = KeyModifiers.None };
            await window.RaiseKeyDownForTest(args);

            args.Handled.Should().BeTrue(
                "the Menu key over the worksheet grid must open the cell context menu for a " +
                "keyboard-only user, matching WPF's KeyboardCommandShortcut.OpenContextMenu");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── P37: a failed edit/validation commit must be announced via a live region on _statusText ──

    [Fact]
    public async Task ShowEditIssue_MarksStatusTextAsALiveRegion_SoScreenReadersAnnounceTheFailure()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);

            // Drives the real production ShowEditIssue code path (the same one
            // CommitDataValidationDropdownSelection/CopySelectedRangeToClipboardAsync/etc. call on a
            // failed commit) via the test-only seam, then asserts on the actual AutomationProperties
            // state it leaves behind — not a source-string check.
            window.InvokeShowEditIssueForTest("This value violates the data validation rule.");

            global::Avalonia.Automation.AutomationProperties.GetLiveSetting(window.StatusTextForTest)
                .Should().Be(global::Avalonia.Automation.AutomationLiveSetting.Polite,
                    "a failed edit/validation commit must mark _statusText as a Polite live region so a " +
                    "screen reader announces the failure — previously there was no live-region signal at all");
            window.StatusTextForTest.Text.Should().Be("This value violates the data validation rule.",
                "ShowEditIssue must still set the visible status text as before");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── P47: copying a range must place an HTML table fragment on the clipboard alongside text ───

    [Fact]
    public void BuildHtmlClipboardFragment_ForStyledRange_ProducesTableWithBoldAndFillCss()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("Bold Fill"));
        var style = new CellStyle
        {
            Bold = true,
            FillPatternStyle = CellFillPatternStyle.Solid,
            FillColor = new CellColor(0xAA, 0xBB, 0xCC),
        };
        sheet.GetCell(address)!.StyleId = workbook.RegisterStyle(style);

        var range = new GridRange(address, address);
        var displayCell = new DisplayCell(
            address.Row, address.Col, new TextValue("Bold Fill"), "Bold Fill", null,
            sheet.GetCell(address)!.StyleId, null, style);
        var viewport = new ViewportModel(Cells: [displayCell], RowMetrics: [], ColMetrics: []);

        var html = MainWindow.BuildHtmlClipboardFragmentForTest(viewport, sheet, range, workbook.Theme);

        html.Should().NotBeNullOrEmpty("a single-cell styled range must produce a real HTML table fragment");
        html.Should().Contain("<table", "the HTML clipboard fragment must be a table so destinations preserve tabular layout");
        html.Should().Contain("font-weight:bold", "bold formatting must survive into the HTML fragment's CSS");
        html.Should().Contain("background-color:#AABBCC", "the cell fill color must survive into the HTML fragment's CSS");
        html.Should().Contain("Bold Fill", "the cell's display text must appear in the fragment");
    }

    /// <summary>
    /// A merged region inside the copied range must render as a single spanned &lt;td&gt; with
    /// colspan/rowspan attributes, not split back into separate cells — matching the WPF host's
    /// BuildHtmlClipboardFragment merge handling.
    /// </summary>
    [Fact]
    public void BuildHtmlClipboardFragment_ForMergedRegion_EmitsColspanAndRowspan()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, new TextValue("Merged"));
        sheet.AddMergedRegion(new GridRange(anchor, new CellAddress(sheet.Id, 2, 2)));

        var range = new GridRange(anchor, new CellAddress(sheet.Id, 2, 2));
        var displayCell = new DisplayCell(anchor.Row, anchor.Col, new TextValue("Merged"), "Merged", null, default, null);
        var viewport = new ViewportModel(Cells: [displayCell], RowMetrics: [], ColMetrics: []);

        var html = MainWindow.BuildHtmlClipboardFragmentForTest(viewport, sheet, range, workbook.Theme);

        html.Should().NotBeNullOrEmpty();
        html.Should().Contain("colspan=\"2\"", "a 2x2 merged region copied in full must span 2 columns in the HTML fragment");
        html.Should().Contain("rowspan=\"2\"", "a 2x2 merged region copied in full must span 2 rows in the HTML fragment");
        // Only ONE <td> should exist for the whole 2x2 merge (the other 3 slots are covered/skipped).
        (html!.Split("<td").Length - 1).Should().Be(1,
            "non-anchor member cells of the merge must be skipped, not rendered as separate <td> cells");
    }

    // ── Shared helpers ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mutating a Sheet directly (SetCell/BackgroundImage) bypasses the WorkbookSession commands that
    /// normally call the private RefreshViewport() afterwards. Force a recompute via the public
    /// UpdateViewportSize (any actual size change re-triggers RefreshViewport()) so
    /// <see cref="FreeX.App.Services.WorkbookSession.Viewport"/> — and therefore BuildSheetGrid —
    /// reflects the mutation before asserting on it. Mirrors
    /// AvaloniaMainWindowGridRenderStage1Tests.ForceViewportRefresh.
    /// </summary>
    private static void ForceViewportRefresh(MainWindow window) =>
        window.Session.UpdateViewportSize(881, 1441);

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    /// <summary>
    /// Mirrors AvaloniaMainWindowGridRenderStage1Tests.FindInnerGrid: BuildSheetGrid returns the
    /// sheet cell grid directly when there is no overlay/page-break content, or wraps it as the first
    /// child of a composite Grid when there is. The sheet's own cell grid is the only one of these
    /// that always sets an explicit Background (white, or the tiled background-image brush post-fix).
    /// </summary>
    private static Grid FindInnerGrid(Control built)
    {
        if (built is Grid { Background: not null } ownGrid)
            return ownGrid;

        if (built is Grid composite)
            return composite.Children.OfType<Grid>().First(g => g.Background is not null);

        return (Grid)built;
    }

    private static Border? FindCellBorder(Grid grid, FreeX.App.Services.WorkbookSession session, uint row, uint col)
    {
        var headerOffset = session.ActiveSheet.ShowHeadings ? 1 : 0;
        var rowIndex = -1;
        var colIndex = -1;
        var rowMetrics = session.Viewport.RowMetrics;
        var colMetrics = session.Viewport.ColMetrics;
        for (var i = 0; i < rowMetrics.Count; i++)
        {
            if (rowMetrics[i].Row == row) { rowIndex = i; break; }
        }
        for (var i = 0; i < colMetrics.Count; i++)
        {
            if (colMetrics[i].Col == col) { colIndex = i; break; }
        }

        if (rowIndex < 0 || colIndex < 0)
            return null;

        var targetRow = rowIndex + headerOffset;
        var targetCol = colIndex + headerOffset;
        return grid.Children.OfType<Border>().FirstOrDefault(b =>
        {
            var br = Grid.GetRow(b);
            var bc = Grid.GetColumn(b);
            var rowSpan = Grid.GetRowSpan(b);
            var colSpan = Grid.GetColumnSpan(b);
            return targetRow >= br && targetRow < br + rowSpan && targetCol >= bc && targetCol < bc + colSpan;
        });
    }
}
