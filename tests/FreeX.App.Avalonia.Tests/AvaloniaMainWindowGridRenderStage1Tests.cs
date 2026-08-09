using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guards for review4 findings J4/J5/J32/J50 (group C-avalonia-mainwindow, stage 1 —
/// grid render):
///
///   J4  — a merged region's member cells rendered as a blank hole once the merge's anchor scrolled
///         out of the viewport (BuildSheetGrid only ever rendered the true merge.Start cell; if that
///         row/col wasn't in the current RowMetrics/ColMetrics it was never iterated, and every other
///         member cell hit the anchor-mismatch `continue`, so nothing at all was drawn for the merge).
///   J5  — the Freeze Panes divider was drawn one row/col too far into the scrollable body whenever a
///         hidden/collapsed row or column fell inside the frozen range (the divider math summed the
///         first `frozenPanes.Rows` RowMetrics entries by raw index, but a hidden row inside the frozen
///         range drops out of RowMetrics entirely, shifting every subsequent index by one).
///   J32 — the autofill (fill) handle disappeared whenever the selection's bottom-right corner
///         resolved into a merge's non-anchor member cell (which never gets its own Border at all —
///         see J4/BuildSheetGrid — so the `address == SelectedRange.End` equality check was never true
///         for any rendered cell).
///   J50 — ResolveShrinkToFitFontSize re-measured text with a brand-new FormattedText on every 1-DIP
///         step, uncached, on every BuildSheetGrid rebuild (scroll/zoom/edit) for every visible
///         Shrink-to-fit cell.
///
/// These use real headless <see cref="MainWindow"/> construction plus the test-only
/// <c>RebuildSheetGridForTest()</c> seam to inspect the actual rendered Avalonia visual tree, not just
/// source text — a stronger regression guard than the source-string assertions in
/// <c>AvaloniaGridMergeShrinkFreezeSelectionTests</c> (which predates J4/J5/J32 and still covers the
/// surrounding, unaffected behavior for H29/H30/H31/H55).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaMainWindowGridRenderStage1Tests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── J4: merge content/fill must render when the anchor is scrolled out of view ───────────

    [Fact]
    public async Task BuildSheetGrid_RendersMergeContent_WhenAnchorIsScrolledOutOfView()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            // A tall merge B5:B10 carrying the anchor's own text.
            var anchor = new CellAddress(sheet.Id, 5, 2);
            var mergeRange = new GridRange(anchor, new CellAddress(sheet.Id, 10, 2));
            sheet.SetCell(anchor, new TextValue("Merged Content"));
            sheet.AddMergedRegion(mergeRange);

            // Scroll so ViewTopRow lands INSIDE the merge (row 7), past the anchor (row 5/6).
            // SetViewportOrigin both moves the origin and forces the RefreshViewport() that picks up
            // the SetCell/AddMergedRegion mutations above.
            window.Session.SetViewportOrigin(7, 1);
            var grid = FindInnerGrid(window.RebuildSheetGridForTest());

            // Every visible row/col slot in the viewport must have SOME Border rendered — no blank
            // hole where the scrolled-into-view portion of the merge (rows 7-10, col B) should be.
            var rowIndexByRow = BuildRowIndexLookup(window.Session.Viewport);
            var colIndexByCol = BuildColIndexLookup(window.Session.Viewport);
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;
            for (var r = 7u; r <= 10u; r++)
            {
                rowIndexByRow.TryGetValue(r, out var rowIndex).Should().BeTrue($"row {r} must be in the viewport");
                colIndexByCol.TryGetValue(2u, out var colIndex).Should().BeTrue("col B must be in the viewport");

                var occupied = FindCellsCoveringSlot(grid, rowIndex + headerOffset, colIndex + headerOffset).Any();
                occupied.Should().BeTrue(
                    $"row {r} col B is inside merge B5:B10 whose anchor (row 5) has scrolled out of view — " +
                    "Excel keeps rendering the merge's content/fill in the visible remainder rather than leaving a gap");
            }

            // The substitute anchor's Border must still carry the true anchor's text content, matching
            // Excel showing the merge's own content in whatever portion remains visible.
            var substituteBorder = FindCellsCoveringSlot(grid, headerOffset, headerOffset + 1).Single();
            var text = ExtractRenderedText(substituteBorder);
            text.Should().Be("Merged Content", "the visible substitute anchor must still show the true anchor's content");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BuildSheetGrid_UnaffectedWhenMergeAnchorIsAlreadyVisible()
    {
        // Guards against a regression in the opposite direction: when the true anchor IS visible
        // (the common case), behavior must be identical to before — one spanned Border at the true
        // anchor's own slot, non-anchor members skipped.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.ActiveSheet;

            var anchor = new CellAddress(sheet.Id, 2, 2);
            sheet.SetCell(anchor, new TextValue("Anchor Text"));
            sheet.AddMergedRegion(new GridRange(anchor, new CellAddress(sheet.Id, 3, 3)));
            ForceViewportRefresh(window);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;

            // Anchor slot (row2,col2 → grid row/col index 1,1 plus header offset) spans 2x2.
            var anchorBorder = FindCellsCoveringSlot(grid, headerOffset + 1, headerOffset + 1).Single();
            Grid.GetRowSpan(anchorBorder).Should().Be(2);
            Grid.GetColumnSpan(anchorBorder).Should().Be(2);
            ExtractRenderedText(anchorBorder).Should().Be("Anchor Text");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── J5: freeze divider must land at the true pinned/scrollable boundary, hidden rows inside ──

    [Fact]
    public async Task AddFreezePaneDividerOverlay_SkipsHiddenRowInsideFrozenRange()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            // Freeze the top 3 rows, then hide row 2 (inside the frozen range).
            sheet.FrozenRows = 3;
            sheet.HiddenRows.Add(2);
            ForceViewportRefresh(window);

            var built = window.RebuildSheetGridForTest();
            var grid = FindInnerGrid(built);
            var overlay = FindOverlayCanvas(built);

            var freezeDividerBrush = GetFreezeDividerBrush();
            var divider = overlay!.Children.OfType<Border>()
                .Single(b => ReferenceEquals(b.Background, freezeDividerBrush) && b.Width == overlay.Width);

            // Expected boundary: sum of the ACTUAL pinned RowMetrics entries (row1, row3 — row2 is
            // hidden and dropped), not 3 raw entries (which would wrongly include row4, the first
            // scrollable body row).
            var viewport = window.Session.Viewport;
            var pinnedRows = viewport.RowMetrics.Where(m => m.Row <= sheet.FrozenRows).ToList();
            pinnedRows.Should().HaveCount(2, "row 2 is hidden and must be dropped from the pinned block");

            // Matches GetDisplayedRowHeight (Math.Max(MinimumDisplayedRowHeight, metric.Height) *
            // zoomFactor): default row height (20) already equals MinimumDisplayedRowHeight (20) and
            // zoom is 100% here, so the raw metric heights sum identically — no private-method
            // reflection needed for this scenario.
            var headerHeight = window.Session.ActiveSheet.ShowHeadings ? GetHeaderRowHeight() : 0;
            var expectedDividerY = headerHeight + pinnedRows.Sum(m => Math.Max(20, m.Height));
            var dividerThickness = GetFreezeDividerThickness();
            var actualTop = Canvas.GetTop(divider);

            actualTop.Should().BeApproximately(
                expectedDividerY - dividerThickness / 2,
                0.5,
                "the divider must land at the boundary after the 2 actually-pinned rows, not after 3 raw RowMetrics entries");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── J32: autofill handle must appear when the selection corner is a non-anchor merge member ──

    [Fact]
    public async Task CreateInteractiveCellBorder_AddsAutofillHandle_WhenSelectionCornerIsNonAnchorMergeMember()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.ActiveSheet;

            // Merge B4:C5 (anchor = B4); select A1:C5 so SelectedRange.End == C5, a non-anchor member.
            var anchor = new CellAddress(sheet.Id, 4, 2);
            var mergeEnd = new CellAddress(sheet.Id, 5, 3);
            sheet.AddMergedRegion(new GridRange(anchor, mergeEnd));
            window.Session.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), mergeEnd));
            window.Session.SelectedRange.End.Should().Be(mergeEnd, "selection must resolve to the merge's non-anchor corner for this test to be meaningful");

            var rebuilt = window.RebuildSheetGridForTest();
            var grid = FindInnerGrid(rebuilt);
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;

            // The merge's anchor (B4) is the only cell Border rendered for this merge. The handle now
            // belongs to the top-level selection overlay, so later cells cannot cover or intercept it.
            var anchorBorder = FindCellsCoveringSlot(grid, headerOffset + 3, headerOffset + 1).Single();
            FindAutofillHandles(anchorBorder).Should().BeEmpty();
            FindAutofillHandles(rebuilt).Should().ContainSingle(
                "the selection overlay must render a usable handle when the bottom-right corner is a non-anchor merge member");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CreateInteractiveCellBorder_NoAutofillHandle_WhenSelectionEndOutsideMerge()
    {
        // Guards the opposite direction: an unrelated merge elsewhere on the sheet must not spuriously
        // grow a handle when the selection's corner has nothing to do with it.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.ActiveSheet;

            var anchor = new CellAddress(sheet.Id, 4, 2);
            sheet.AddMergedRegion(new GridRange(anchor, new CellAddress(sheet.Id, 5, 3)));
            window.Session.SelectCell(new CellAddress(sheet.Id, 8, 8));

            var rebuilt = window.RebuildSheetGridForTest();
            var grid = FindInnerGrid(rebuilt);
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;
            var anchorBorder = FindCellsCoveringSlot(grid, headerOffset + 3, headerOffset + 1).Single();

            FindAutofillHandles(anchorBorder).Should().BeEmpty(
                "cell visuals must never own the fill handle");
            FindAutofillHandles(rebuilt).Should().ContainSingle(
                "the one active selection still owns exactly one overlay handle");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectionOverlay_UsesActualGridSpanAndCellsDoNotForceHandCursor()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.ActiveSheet;
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 3, 2),
                new CellAddress(sheet.Id, 4, 4)));

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var outline = grid.Children
                .OfType<Border>()
                .Single(candidate =>
                    AutomationProperties.GetAutomationId(candidate) == "WorksheetSelectionOutline");
            var handle = grid.Children
                .OfType<Border>()
                .Single(candidate =>
                    AutomationProperties.GetAutomationId(candidate) == "WorksheetAutofillHandle");
            var activeCell = FindDescendants(grid)
                .OfType<Border>()
                .Single(candidate =>
                    AutomationProperties.GetAutomationId(candidate) == "Cell_B3");
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;

            Grid.GetRow(outline).Should().Be(2 + headerOffset);
            Grid.GetColumn(outline).Should().Be(1 + headerOffset);
            Grid.GetRowSpan(outline).Should().Be(2);
            Grid.GetColumnSpan(outline).Should().Be(3);
            outline.ZIndex.Should().BeGreaterThan(activeCell.ZIndex);

            Grid.GetRow(handle).Should().Be(3 + headerOffset);
            Grid.GetColumn(handle).Should().Be(3 + headerOffset);
            handle.Width.Should().Be(10);
            handle.Height.Should().Be(10);
            activeCell.Cursor.Should().BeNull("normal worksheet cells must not force the hand cursor");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── R119: a single click on a merged cell's anchor must expand the selection outline AND
    // the fill handle to the merge's full footprint, matching the WPF host's
    // CalculateSelectionRangeLayout (GridView.Rendering.Selection.cs) merge-expansion. SelectCell
    // always produces an anchor-only 1x1 range (WorkbookSession.SetSingleSelectedRange never
    // merge-expands), so without a matching reroute in AddSelectionOverlayToGrid the outline/handle
    // were sized from that raw 1x1 range -- truncating a merged B2:D2 title cell's outline/handle to
    // column B alone instead of wrapping/anchoring at the true merge footprint (column D).

    [Fact]
    public async Task R119_SelectCell_OnMergedAnchor_ExpandsSelectionOutlineAndFillHandleToFullMergeSpan()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.ActiveSheet;

            // Merge B2:D2 (a typical merged title-row cell) and click its anchor B2 -- exactly what
            // SelectCell does on an ordinary single left-click, never merge-expanding SelectedRange.
            var anchor = new CellAddress(sheet.Id, 2, 2);
            var mergeEnd = new CellAddress(sheet.Id, 2, 4);
            sheet.AddMergedRegion(new GridRange(anchor, mergeEnd));
            window.Session.SelectCell(anchor);
            window.Session.SelectedRange.Should().Be(new GridRange(anchor, anchor),
                "SelectCell must still report the raw anchor-only range -- only the rendered overlay should expand");

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var outline = grid.Children
                .OfType<Border>()
                .Single(candidate =>
                    AutomationProperties.GetAutomationId(candidate) == "WorksheetSelectionOutline");
            var handle = grid.Children
                .OfType<Border>()
                .Single(candidate =>
                    AutomationProperties.GetAutomationId(candidate) == "WorksheetAutofillHandle");
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;

            // Outline must span the WHOLE merge footprint (row 2, columns B..D), not just column B.
            Grid.GetRow(outline).Should().Be(1 + headerOffset);
            Grid.GetColumn(outline).Should().Be(1 + headerOffset);
            Grid.GetRowSpan(outline).Should().Be(1);
            Grid.GetColumnSpan(outline).Should().Be(3, "the outline must wrap the full B2:D2 merge, not just the anchor cell");

            // The fill handle must sit at the merge's true bottom-right corner (column D), not the
            // anchor's own bottom-right corner (column B).
            Grid.GetRow(handle).Should().Be(1 + headerOffset);
            Grid.GetColumn(handle).Should().Be(3 + headerOffset,
                "the fill handle must anchor at the merge's real bottom-right corner (col D), not the un-expanded anchor cell (col B)");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task R119_SelectCell_OnOrdinaryUnmergedCell_SelectionOutlineAndFillHandleStayAtSingleCell()
    {
        // Sibling no-regression guard: an ordinary single-cell selection with NO merge involved must
        // not be spuriously expanded by the new merge-lookup in AddSelectionOverlayToGrid.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.ActiveSheet;

            // An unrelated merge elsewhere on the sheet must not affect this selection at all.
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sheet.Id, 8, 8),
                new CellAddress(sheet.Id, 8, 10)));

            var selected = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(selected);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var outline = grid.Children
                .OfType<Border>()
                .Single(candidate =>
                    AutomationProperties.GetAutomationId(candidate) == "WorksheetSelectionOutline");
            var handle = grid.Children
                .OfType<Border>()
                .Single(candidate =>
                    AutomationProperties.GetAutomationId(candidate) == "WorksheetAutofillHandle");
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;

            Grid.GetRow(outline).Should().Be(1 + headerOffset);
            Grid.GetColumn(outline).Should().Be(1 + headerOffset);
            Grid.GetRowSpan(outline).Should().Be(1);
            Grid.GetColumnSpan(outline).Should().Be(1, "an ordinary unmerged single-cell selection must stay 1x1");

            Grid.GetRow(handle).Should().Be(1 + headerOffset);
            Grid.GetColumn(handle).Should().Be(1 + headerOffset);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── J50: Shrink-to-fit font resolution must be memoized, not re-measured from scratch ────────

    [Fact]
    public async Task ResolveShrinkToFitFontSize_MemoizesResult_AcrossRepeatedGridRebuilds()
    {
        var source = MainWindowSource();

        // The cache lookups/inserts must exist around both the width-measurement hot loop and the
        // shrink-resolution entry point, mirroring WPF's GridView.TextLayoutCache.cs two-tier cache.
        source.Should().Contain("TextWidthMeasurementCache.TryGetValue(key, out var cachedWidth)");
        source.Should().Contain("ShrinkToFitFontSizeCache.TryGetValue(key, out var cachedFontSize)");
        source.Should().Contain("ShrinkToFitFontSizeCache.Add(key, fontSize);");
        source.Should().Contain("TextWidthMeasurementCache.Add(key, formatted.Width);");

        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like "Windows" at B1)
            // — run every scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("A Reasonably Long Shrink To Fit String"));
            var style = new CellStyle { ShrinkToFit = true, FontSize = 72 };
            sheet.GetCell(address)!.StyleId = window.Session.Workbook.RegisterStyle(style);
            sheet.ColumnWidths[1] = 40; // narrow column forces real shrinking work
            ForceViewportRefresh(window);

            // Rebuilding repeatedly must not throw and must keep producing a rendered cell — the
            // point of the memoization is performance (bounded, not unbounded, FormattedText churn),
            // which we assert indirectly here via the source checks above plus this smoke pass.
            for (var i = 0; i < 5; i++)
                window.RebuildSheetGridForTest();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Shared helpers ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every test here mutates the <see cref="Sheet"/> model directly (SetCell/AddMergedRegion/
    /// FrozenRows/HiddenRows), bypassing the WorkbookSession commands that normally call the private
    /// RefreshViewport() afterwards. Force a recompute via the public UpdateViewportSize (any actual
    /// size change re-triggers RefreshViewport()) so <see cref="WorkbookSession.Viewport"/> — and
    /// therefore BuildSheetGrid — reflects the mutation before we assert on it.
    /// </summary>
    private static void ForceViewportRefresh(MainWindow window) =>
        window.Session.UpdateViewportSize(InitialViewportHeightForTests + 1, InitialViewportWidthForTests);

    private const double InitialViewportHeightForTests = 880;
    private const double InitialViewportWidthForTests = 1440;

    /// <summary>
    /// BuildSheetGrid returns the sheet cell grid directly when there is no overlay/page-break
    /// content, or wraps it as the first child of a composite Grid (Children = { grid, ... }) when
    /// there is. The sheet's own cell grid is the only one of these Grids that sets
    /// <c>Background = Brushes.White</c> (see BuildSheetGrid) — the composite wrapper leaves
    /// Background unset. Distinguishing on that marker (rather than blindly taking
    /// "the first nested Grid", which can accidentally match one of the per-column-header
    /// resize-handle Grids from CreateHeaderWithResizeHandle when `built` is already the real
    /// sheet grid) is what makes this reliable.
    /// </summary>
    private static Grid FindInnerGrid(Control built)
    {
        if (built is Grid { Background: not null } ownGrid)
            return ownGrid;

        if (built is Grid composite)
            return composite.Children.OfType<Grid>().First(g => g.Background is not null);

        return (Grid)built;
    }

    private static Canvas? FindOverlayCanvas(Control built) =>
        (built as Grid)?.Children
            .OfType<Canvas>()
            .FirstOrDefault(canvas =>
                AutomationProperties.GetAutomationId(canvas) != "WorksheetSelectionOverlay");

    private static IEnumerable<Border> FindCellsCoveringSlot(Grid grid, int row, int col) =>
        grid.Children.OfType<Border>().Where(b =>
        {
            if (AutomationProperties.GetAutomationId(b) is not { } automationId ||
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

    private static string? ExtractRenderedText(Border border) =>
        FindDescendants(border).OfType<TextBlock>().FirstOrDefault()?.Text;

    private static Border[] FindAutofillHandles(Control root) =>
        FindDescendants(root)
            .OfType<Border>()
            .Where(candidate =>
                AutomationProperties.GetAutomationId(candidate) == "WorksheetAutofillHandle")
            .ToArray();

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

    private static Dictionary<uint, int> BuildRowIndexLookup(ViewportModel viewport)
    {
        var lookup = new Dictionary<uint, int>();
        for (var i = 0; i < viewport.RowMetrics.Count; i++)
            lookup[viewport.RowMetrics[i].Row] = i;
        return lookup;
    }

    private static Dictionary<uint, int> BuildColIndexLookup(ViewportModel viewport)
    {
        var lookup = new Dictionary<uint, int>();
        for (var i = 0; i < viewport.ColMetrics.Count; i++)
            lookup[viewport.ColMetrics[i].Col] = i;
        return lookup;
    }

    private static double GetHeaderRowHeight() =>
        (double)typeof(MainWindow)
            .GetField("HeaderRowHeight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

    private static double GetFreezeDividerThickness() =>
        (double)typeof(MainWindow)
            .GetField("FreezeDividerThickness", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

    private static global::Avalonia.Media.IBrush GetFreezeDividerBrush() =>
        (global::Avalonia.Media.IBrush)typeof(MainWindow)
            .GetField("FreezeDividerBrush", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

    private static string MainWindowSource() =>
        System.IO.File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
