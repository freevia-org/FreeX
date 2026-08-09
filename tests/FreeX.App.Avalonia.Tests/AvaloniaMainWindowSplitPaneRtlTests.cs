using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guards for review5 findings K10/K33 (group C-avalonia-mainwindow, stage 1 — split-pane
/// render + RTL flow-direction):
///
///   K10 — View ▸ Split executed (SetSplitPanesCommand set sheet.SplitRow/SplitColumn) but the
///         Avalonia shell never rendered or hit-tested the extra split panes at all: BuildSheetGrid's
///         single Grid only ever walked viewport.RowMetrics/ColMetrics (the main/BottomRight pane), with
///         zero reference to viewport.SplitPanes anywhere in the file. Fixed by combining
///         SplitPanes.TopRows/LeftColumns ahead of the main pane's RowMetrics/ColMetrics into one
///         continuous sequence (CombineSplitRowMetrics/CombineSplitColumnMetrics), merging
///         SplitPanes.Cells into the cell lookup, and drawing a divider Border at the pinned/scrollable
///         boundary — mirroring WPF's RenderSplitDivider in spirit.
///   K33 — Every FormattedText/TextBlock construction for cell content hardcoded FlowDirection.LeftToRight
///         (and General alignment always resolved Left/Right regardless of reading order), so a
///         right-to-left sheet (or a cell with an explicit RTL Text direction override) always rendered
///         LTR. Fixed by resolving each cell's effective reading order via
///         CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft (Sheet.IsRightToLeft +
///         CellStyle.ReadingOrder) and threading it into both the TextBlock's FlowDirection and the
///         General-alignment resolution (MapCellTextAlignment).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaMainWindowSplitPaneRtlTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── K10: all four split quadrants must render as real grid rows/columns ──────────────────

    [Fact]
    public async Task BuildSheetGrid_RendersPinnedTopRowsAndLeftColumns_WhenSheetIsSplit()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            // Content in the pinned quadrants (rows 1-2, col A) and in the main scrollable pane.
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("TopLeft"));
            sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("MainPane"));

            // Split at row 3 / column B: rows 1-2 and column A are pinned.
            sheet.SplitRow = 3;
            sheet.SplitColumn = 2;
            // The main (BottomRight) pane's own scroll position is tracked independently of the split
            // row/column — pin it at exactly the split boundary (the realistic post-scroll state, and
            // the only state where "pinned rows/cols" and "main pane rows/cols" are non-overlapping by
            // construction) so this test's own row/column-count arithmetic is unambiguous.
            sheet.ViewTopRow = 3;
            sheet.ViewLeftCol = 2;
            // Move the active cell into the scrollable (BottomRight) pane before refreshing: the
            // default active cell A1 sits inside the pinned split region, and
            // WorkbookSession.EnsureActiveCellVisible (which only special-cases Freeze panes, not
            // Split panes) would otherwise treat A1 as "scrolled out of view" and snap ViewTopRow/
            // ViewLeftCol straight back to 1, clobbering the split scroll position set above.
            window.Session.SelectCell(new CellAddress(sheet.Id, 3, 2));
            ForceViewportRefresh(window);

            var viewport = window.Session.Viewport;
            viewport.SplitPanes.Should().NotBeNull("a sheet with SplitRow/SplitColumn set must produce split-pane viewport data");
            viewport.SplitPanes!.TopRows.Should().NotBeEmpty();
            viewport.SplitPanes.LeftColumns.Should().NotBeEmpty();

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;

            // The pinned top-left quadrant's own row/col slot (grid row/col index 0,0 plus header
            // offset) must have a rendered Border carrying the pinned row's content — before this fix
            // BuildSheetGrid only ever iterated viewport.RowMetrics/ColMetrics (the main pane), so the
            // pinned pane's own cell content was never rendered as a distinct grid cell.
            var topLeftBorder = FindCellsCoveringSlot(grid, headerOffset, headerOffset).SingleOrDefault();
            topLeftBorder.Should().NotBeNull("the pinned TopLeft split quadrant must render its own cell content, not be skipped entirely");
            ExtractRenderedText(topLeftBorder!).Should().Be("TopLeft");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BuildSheetGrid_GridDimensions_IncludePinnedSplitRowsAndColumns()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.SplitRow = 3;
            sheet.SplitColumn = 2;
            // The main (BottomRight) pane's own scroll position is tracked independently of the split
            // row/column — pin it at exactly the split boundary (the realistic post-scroll state, and
            // the only state where "pinned rows/cols" and "main pane rows/cols" are non-overlapping by
            // construction) so this test's own row/column-count arithmetic is unambiguous.
            sheet.ViewTopRow = 3;
            sheet.ViewLeftCol = 2;
            // Move the active cell into the scrollable (BottomRight) pane before refreshing: the
            // default active cell A1 sits inside the pinned split region, and
            // WorkbookSession.EnsureActiveCellVisible (which only special-cases Freeze panes, not
            // Split panes) would otherwise treat A1 as "scrolled out of view" and snap ViewTopRow/
            // ViewLeftCol straight back to 1, clobbering the split scroll position set above.
            window.Session.SelectCell(new CellAddress(sheet.Id, 3, 2));
            ForceViewportRefresh(window);

            var viewport = window.Session.Viewport;
            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;

            // Total row/col definitions must cover the pinned split rows/columns PLUS the main pane's
            // own rows/columns — before this fix, the Grid's RowDefinitions/ColumnDefinitions were
            // built purely from viewport.RowMetrics/ColMetrics, so the pinned block had no grid slots
            // reserved for it at all.
            var expectedRowCount = viewport.SplitPanes!.TopRows.Count + viewport.RowMetrics.Count + headerOffset;
            var expectedColCount = viewport.SplitPanes.LeftColumns.Count + viewport.ColMetrics.Count + headerOffset;

            grid.RowDefinitions.Count.Should().Be(expectedRowCount);
            grid.ColumnDefinitions.Count.Should().Be(expectedColCount);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BuildSheetGrid_DrawsSplitDivider_AtPinnedScrollableBoundary()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.SplitRow = 3;
            sheet.SplitColumn = 2;
            // The main (BottomRight) pane's own scroll position is tracked independently of the split
            // row/column — pin it at exactly the split boundary (the realistic post-scroll state, and
            // the only state where "pinned rows/cols" and "main pane rows/cols" are non-overlapping by
            // construction) so this test's own row/column-count arithmetic is unambiguous.
            sheet.ViewTopRow = 3;
            sheet.ViewLeftCol = 2;
            ForceViewportRefresh(window);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;
            var viewport = window.Session.Viewport;

            var freezeDividerBrush = GetFreezeDividerBrush();
            var horizontalDivider = grid.Children.OfType<Border>()
                .Where(b => ReferenceEquals(b.Background, freezeDividerBrush))
                .SingleOrDefault(b => Grid.GetColumnSpan(b) > 1);
            horizontalDivider.Should().NotBeNull("a horizontal split divider must be drawn at the row split boundary");
            Grid.GetRow(horizontalDivider!).Should().Be(viewport.SplitPanes!.TopRows.Count - 1 + headerOffset);

            var verticalDivider = grid.Children.OfType<Border>()
                .Where(b => ReferenceEquals(b.Background, freezeDividerBrush))
                .SingleOrDefault(b => Grid.GetRowSpan(b) > 1);
            verticalDivider.Should().NotBeNull("a vertical split divider must be drawn at the column split boundary");
            Grid.GetColumn(verticalDivider!).Should().Be(viewport.SplitPanes.LeftColumns.Count - 1 + headerOffset);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BuildSheetGrid_NoSplitDivider_WhenSheetIsNotSplit()
    {
        // Guards the common (unsplit) case: no divider, and grid dimensions match the main pane exactly
        // (i.e. CombineSplitRowMetrics/CombineSplitColumnMetrics must be a no-op pass-through).
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.ActiveSheet;
            ForceViewportRefresh(window);

            var viewport = window.Session.Viewport;
            viewport.SplitPanes.Should().BeNull();

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;

            grid.RowDefinitions.Count.Should().Be(viewport.RowMetrics.Count + headerOffset);
            grid.ColumnDefinitions.Count.Should().Be(viewport.ColMetrics.Count + headerOffset);

            var freezeDividerBrush = GetFreezeDividerBrush();
            grid.Children.OfType<Border>().Where(b => ReferenceEquals(b.Background, freezeDividerBrush)).Should().BeEmpty();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── K33: cell FlowDirection/alignment must follow the sheet's/cell's effective reading order ──

    [Fact]
    public async Task CreateCell_UsesRightToLeftFlowDirection_WhenSheetIsRightToLeft()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.IsRightToLeft = true;
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("مرحبا"));
            ForceViewportRefresh(window);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;
            var border = FindCellsCoveringSlot(grid, headerOffset, headerOffset).Single();
            var textBlock = FindDescendants(border).OfType<TextBlock>().First();

            textBlock.FlowDirection.Should().Be(FlowDirection.RightToLeft,
                "a right-to-left sheet must render cell text with RightToLeft FlowDirection, not the previously hardcoded LeftToRight");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CreateCell_UsesLeftToRightFlowDirection_WhenSheetIsNotRightToLeft()
    {
        // Guards the common (LTR) case: default behavior is unchanged.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.IsRightToLeft.Should().BeFalse("a freshly-added sheet must default to left-to-right");
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("Hello"));
            ForceViewportRefresh(window);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;
            var border = FindCellsCoveringSlot(grid, headerOffset, headerOffset).Single();
            var textBlock = FindDescendants(border).OfType<TextBlock>().First();

            textBlock.FlowDirection.Should().Be(FlowDirection.LeftToRight);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CreateCell_CellReadingOrderOverride_TakesPrecedenceOverSheetRightToLeftFlag()
    {
        // Matches Excel's Format Cells ▸ Alignment ▸ Text direction semantics: an explicit per-cell
        // override (LeftToRight/RightToLeft) always wins over the sheet's own Right-to-left flag —
        // only readingOrder="0" (Context, the default) follows the sheet.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.IsRightToLeft = true;
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("Hello"));
            var style = new CellStyle { ReadingOrder = CellReadingOrder.LeftToRight };
            sheet.GetCell(address)!.StyleId = window.Session.Workbook.RegisterStyle(style);
            ForceViewportRefresh(window);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;
            var border = FindCellsCoveringSlot(grid, headerOffset, headerOffset).Single();
            var textBlock = FindDescendants(border).OfType<TextBlock>().First();

            textBlock.FlowDirection.Should().Be(FlowDirection.LeftToRight,
                "an explicit per-cell LeftToRight reading-order override must win over the sheet's own Right-to-left flag");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public void MapCellTextAlignment_MirrorsGeneralAlignment_WhenEffectivelyRightToLeft()
    {
        var method = ExtractMethod(
            "private static TextAlignment MapCellTextAlignment(",
            "private static AvaloniaVerticalAlignment MapCellVerticalAlignment(");

        method.Should().Contain("CellHAlign.General when isNumericOrDate => isEffectivelyRightToLeft ? TextAlignment.Left : TextAlignment.Right");
        method.Should().Contain("CellHAlign.General => isEffectivelyRightToLeft ? TextAlignment.Right : TextAlignment.Left");
    }

    // ── Shared helpers (mirroring AvaloniaMainWindowGridRenderStage1Tests conventions) ─────────

    private static void ForceViewportRefresh(MainWindow window) =>
        window.Session.UpdateViewportSize(InitialViewportHeightForTests + 1, InitialViewportWidthForTests);

    private const double InitialViewportHeightForTests = 880;
    private const double InitialViewportWidthForTests = 1440;

    private static Grid FindInnerGrid(Control built)
    {
        if (built is Grid { Background: not null } ownGrid)
            return ownGrid;

        if (built is Grid composite)
            return composite.Children.OfType<Grid>().First(g => g.Background is not null);

        return (Grid)built;
    }

    private static IEnumerable<Border> FindCellsCoveringSlot(Grid grid, int row, int col)
    {
        // Exclude the split/freeze divider overlay Border (see AddSplitPaneDividerOverlayToGrid):
        // it is deliberately drawn at the same grid row/column as the last pinned row/column's own
        // cell content (a thin line overlaid at that row/column's boundary edge, mirroring
        // AddFreezePaneDividerOverlay), so it can share a grid slot with a real cell Border without
        // being a second "cell" at that slot.
        var freezeDividerBrush = GetFreezeDividerBrush();
        return grid.Children.OfType<Border>().Where(b =>
        {
            if (ReferenceEquals(b.Background, freezeDividerBrush) ||
                AutomationProperties.GetAutomationId(b) is not { } automationId ||
                !automationId.StartsWith("Cell_", StringComparison.Ordinal))
            {
                return false;
            }

            var br = Grid.GetRow(b);
            var bc = Grid.GetColumn(b);
            var rowSpan = Grid.GetRowSpan(b);
            var colSpan = Grid.GetColumnSpan(b);
            return row >= br && row < br + rowSpan && col >= bc && col < bc + colSpan;
        });
    }

    private static string? ExtractRenderedText(Border border) =>
        FindDescendants(border).OfType<TextBlock>().FirstOrDefault()?.Text;

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        if (root is Border { Child: { } child })
        {
            yield return child;
            foreach (var descendant in FindDescendants(child))
                yield return descendant;
        }
        else if (root is Panel panel)
        {
            foreach (var c in panel.Children)
            {
                yield return c;
                foreach (var descendant in FindDescendants(c))
                    yield return descendant;
            }
        }
    }

    private static global::Avalonia.Media.IBrush GetFreezeDividerBrush() =>
        (global::Avalonia.Media.IBrush)typeof(MainWindow)
            .GetField("FreezeDividerBrush", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

    private static string MainWindowSource() =>
        System.IO.File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

    private static string ExtractMethod(string startMarker, string endMarker)
    {
        var source = MainWindowSource();
        var start = source.IndexOf(startMarker, System.StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"expected to find '{startMarker}' in MainWindow.cs");
        var end = source.IndexOf(endMarker, start, System.StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"expected to find '{endMarker}' after '{startMarker}' in MainWindow.cs");
        return source[start..end];
    }

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = System.IO.Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (System.IO.File.Exists(candidate))
                return candidate;
        }

        throw new System.IO.FileNotFoundException("Could not locate repository file.", System.IO.Path.Combine(parts));
    }
}
