using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard for round-52 finding R52-render-scroll-viewport-nav-3-2:
///
///   Avalonia's plain PageUp/PageDown page size was computed from the frozen-aware COMBINED
///   RowMetrics/ColMetrics list (<c>_session.Viewport.RowMetrics.Count - 1</c>), which includes the
///   pinned frozen rows/columns. That over-pages by the frozen-row count versus Excel and versus the
///   WPF host, which explicitly excludes frozen rows/columns from the page-size count via
///   CountScrollableRows/CountScrollableColumns (MainWindow.Viewport.cs).
///
/// Drives the real production key-handling code via the RaiseKeyDownForTest seam (MainWindow.cs) so
/// this exercises the actual NavigateActiveCell page-size computation rather than a source-string
/// proxy.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R52_PageDownFrozenPaneTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task PageDown_WithFrozenRows_PagesByScrollableRowsOnly_ExcludingTheFrozenRows()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo — run this scenario
            // on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            // Freeze the top 2 rows the same way the ribbon's Freeze Panes command does: select the
            // first unfrozen row/col and freeze everything above/left of it.
            window.Session.SelectCell(new CellAddress(sheet.Id, 3, 1));
            window.Session.FreezePanesAtActiveCell();
            sheet.FrozenRows.Should().Be(2u,
                "sanity: freezing at row 3 must pin exactly the 2 rows above it");

            var viewport = window.Session.Viewport;
            var scrollableRowCount = viewport.RowMetrics.Count(m => m.Row > sheet.FrozenRows);
            scrollableRowCount.Should().BeLessThan(viewport.RowMetrics.Count,
                "the combined RowMetrics must include the 2 pinned frozen rows for this scenario to be meaningful");

            // Pre-fix formula (RowMetrics.Count - 1, includes the frozen rows) vs. the corrected
            // formula (scrollable rows only, excluding the frozen rows) — these must differ, or the
            // fixture doesn't actually exercise the bug.
            var oldBuggyPageRows = Math.Max(1, viewport.RowMetrics.Count - 1);
            var correctPageRows = Math.Max(1, scrollableRowCount - 1);
            correctPageRows.Should().BeLessThan(oldBuggyPageRows,
                "excluding the 2 frozen rows must shrink the page size versus the old RowMetrics.Count - 1 formula");

            var start = new CellAddress(sheet.Id, 5, 3);
            window.Session.SelectCell(start);

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.PageDown, KeyModifiers = KeyModifiers.None });

            window.Session.ActiveCell.Row.Should().Be((uint)(start.Row + correctPageRows),
                "PageDown must page by the scrollable rows only, excluding the 2 frozen rows, matching Excel and the WPF host");
            window.Session.ActiveCell.Row.Should().NotBe((uint)(start.Row + oldBuggyPageRows),
                "before the fix PageDown over-paged by including the frozen rows in the jump distance");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PageDown_WithNoFrozenRows_StillPagesByFullViewportRowCount()
    {
        // Sibling no-regression guard: with no frozen panes, CountScrollableRows must return the
        // exact same count as the raw RowMetrics.Count (every row satisfies Row > 0), so ordinary
        // (non-frozen) PageDown paging is unaffected by the fix.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.FrozenRows.Should().Be(0u, "sanity: fresh sheet has no frozen panes");

            var pageRows = Math.Max(1, window.Session.Viewport.RowMetrics.Count - 1);
            var start = new CellAddress(sheet.Id, 1, 1);
            window.Session.SelectCell(start);

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.PageDown, KeyModifiers = KeyModifiers.None });

            window.Session.ActiveCell.Row.Should().Be((uint)(start.Row + pageRows),
                "with no frozen rows, PageDown must still page by the full viewport row count, unchanged by the frozen-row fix");
            window.Session.ActiveCell.Col.Should().Be(1u, "plain PageDown must not move horizontally");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
