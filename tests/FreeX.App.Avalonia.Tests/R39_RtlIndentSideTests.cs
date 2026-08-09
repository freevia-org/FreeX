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

using CellHAlign = FreeX.Core.Model.HorizontalAlignment;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard for round-39 finding R39-render-rtl-bidi-2-2 (bucket rtl-indent):
///
///   Format Cells ▸ Alignment ▸ Indent had zero visual effect on right-anchored text —
///   <c>CreateCellBorder</c> (MainWindow.cs) unconditionally added the indent to the TextBlock's
///   LEFT margin, regardless of which physical edge the text was actually anchored to. For a
///   right-aligned cell (explicit Right alignment, or General-aligned text content mirrored to
///   TextAlignment.Right by a right-to-left sheet — see MapCellTextAlignment), Excel insets the
///   indent from the RIGHT edge, not the left, so the old code produced no visible change at all
///   for those cells. Fixed by keying the margin side off the cell's effective (post
///   Fill/rotation-override) TextAlignment: Right-anchored cells now get the indent added to the
///   right margin instead of the left; Left/Center-anchored cells (including the common LTR case)
///   are unaffected.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R39_RtlIndentSideTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private const double InitialViewportHeightForTests = 880;
    private const double InitialViewportWidthForTests = 1440;

    // ── Fix: indent must inset from the RIGHT edge for right-anchored text ──────────────────────

    [Fact]
    public async Task Indent_InsetsFromRightMargin_WhenCellIsExplicitlyRightAligned()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("Hello"));
            var style = new CellStyle { HorizontalAlignment = CellHAlign.Right, IndentLevel = 3 };
            sheet.GetCell(address)!.StyleId = window.Session.Workbook.RegisterStyle(style);
            ForceViewportRefresh(window);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;
            var border = FindCellsCoveringSlot(grid, headerOffset, headerOffset).Single();
            var textBlock = FindDescendants(border).OfType<TextBlock>().First();

            // Baseline (no indent) horizontal padding is symmetric; a non-zero IndentLevel must widen
            // the RIGHT margin term beyond the left one for a right-aligned cell — the opposite of the
            // pre-fix behavior, which always widened the left margin regardless of alignment.
            textBlock.Margin.Right.Should().BeGreaterThan(textBlock.Margin.Left,
                "indent on a right-aligned cell must inset from the right edge, matching Excel");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Indent_InsetsFromRightMargin_WhenGeneralAlignedTextMirrorsRightInRtlSheet()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.IsRightToLeft = true;
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("مرحبا"));
            var style = new CellStyle { IndentLevel = 2 }; // General alignment (default)
            sheet.GetCell(address)!.StyleId = window.Session.Workbook.RegisterStyle(style);
            ForceViewportRefresh(window);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;
            var border = FindCellsCoveringSlot(grid, headerOffset, headerOffset).Single();
            var textBlock = FindDescendants(border).OfType<TextBlock>().First();

            textBlock.TextAlignment.Should().Be(TextAlignment.Right,
                "General-aligned text content in a right-to-left sheet mirrors to a right anchor");
            textBlock.Margin.Right.Should().BeGreaterThan(textBlock.Margin.Left,
                "indent must follow the mirrored right anchor in an RTL sheet, not stay hardcoded to the left");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Sibling no-regression: the common LTR/left-aligned case keeps insetting from the left ───

    [Fact]
    public async Task Indent_InsetsFromLeftMargin_WhenCellIsLeftAlignedOrDefaultLtr()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.IsRightToLeft.Should().BeFalse("a freshly-added sheet must default to left-to-right");
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("Hello"));
            var style = new CellStyle { HorizontalAlignment = CellHAlign.Left, IndentLevel = 3 };
            sheet.GetCell(address)!.StyleId = window.Session.Workbook.RegisterStyle(style);
            ForceViewportRefresh(window);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;
            var border = FindCellsCoveringSlot(grid, headerOffset, headerOffset).Single();
            var textBlock = FindDescendants(border).OfType<TextBlock>().First();

            textBlock.Margin.Left.Should().BeGreaterThan(textBlock.Margin.Right,
                "the original left-aligned/LTR indent behavior must be unchanged by this fix");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Indent_HasNoSideBias_WhenIndentLevelIsZero()
    {
        // Guards against the fix accidentally introducing an asymmetric margin when there is no
        // indent at all (IndentLevel 0) — left and right padding terms must stay equal.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("Hello"));
            var style = new CellStyle { HorizontalAlignment = CellHAlign.Right, IndentLevel = 0 };
            sheet.GetCell(address)!.StyleId = window.Session.Workbook.RegisterStyle(style);
            ForceViewportRefresh(window);

            var grid = FindInnerGrid(window.RebuildSheetGridForTest());
            var headerOffset = window.Session.ActiveSheet.ShowHeadings ? 1 : 0;
            var border = FindCellsCoveringSlot(grid, headerOffset, headerOffset).Single();
            var textBlock = FindDescendants(border).OfType<TextBlock>().First();

            textBlock.Margin.Left.Should().Be(textBlock.Margin.Right);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Shared helpers (mirroring AvaloniaMainWindowSplitPaneRtlTests conventions) ────────────────

    private static void ForceViewportRefresh(MainWindow window) =>
        window.Session.UpdateViewportSize(InitialViewportHeightForTests + 1, InitialViewportWidthForTests);

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
}
