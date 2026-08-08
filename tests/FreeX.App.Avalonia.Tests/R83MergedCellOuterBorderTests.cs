using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless-Avalonia regression tests for R83-render-mergedcell-render-5-1: a merged cell collapses
/// to a single rendered <c>Border</c> spanning the whole region (<c>CreateCell</c> only ever renders
/// the anchor's own Border), so the anchor's own <see cref="CellStyle"/> used to be the ONLY style
/// ever consulted for that Border's four edges (<c>MainWindow.cs</c>'s old
/// <c>var style = cell.Style;</c> feeding <c>CreateInteractiveCellBorder</c>/<c>CreateCellBorder</c>
/// directly). But <c>BorderShortcutService.GetOutlineBorderDiff</c> (Ribbon Home ▸ Borders ▸ Outline)
/// stores the BOTTOM and RIGHT outline edges on the OTHER constituent cells of the merge (the
/// bottom-row and right-column members) via <c>SelectionStyleCommandPlanner.CreatePerCellStyleCommands</c>
/// -- never on the anchor -- so those edges silently never rendered pre-fix. The fix adds
/// <c>MainWindow.ResolveMergedOuterBorderStyle</c>, which pulls the bottom/right edges from the
/// actual owning member cell (mirroring WPF's GridView.Rendering.cs per-constituent-cell border
/// resolution for merges) before the style reaches <c>CreateCellBorder</c>.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R83MergedCellOuterBorderTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static CellStyle? InvokeResolveMergedOuterBorderStyle(
        MainWindow window, CellStyle? anchorStyle, GridRange? mergeRegion)
    {
        var method = typeof(MainWindow).GetMethod(
            "ResolveMergedOuterBorderStyle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (CellStyle?)method.Invoke(window, [anchorStyle, mergeRegion]);
    }

    [Fact]
    public async Task ResolveMergedOuterBorderStyle_PullsBottomAndRightEdgesFromConstituentCells()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var workbook = window.Session.Workbook;

            // Merge B2:D4. Reproduce exactly what BorderShortcutService.GetOutlineBorderDiff +
            // SelectionStyleCommandPlanner.CreatePerCellStyleCommands leave in the model after
            // Ribbon Home > Borders > Outside Borders (Outline) is applied to the merged selection:
            // B2 (anchor) = Top+Left, D2 (top-right) = Top+Right, B4 (bottom-left) = Bottom+Left,
            // D4 (bottom-right) = Bottom+Right.
            var thin = new CellBorder(BorderStyle.Thin, CellColor.Black);

            var anchor = new CellAddress(sheet.Id, 2, 2);
            var topRight = new CellAddress(sheet.Id, 2, 4);
            var bottomLeft = new CellAddress(sheet.Id, 4, 2);
            var bottomRight = new CellAddress(sheet.Id, 4, 4);

            sheet.SetCell(anchor, new TextValue("Merged"));
            sheet.SetCell(topRight, new BlankValue());
            sheet.SetCell(bottomLeft, new BlankValue());
            sheet.SetCell(bottomRight, new BlankValue());

            var anchorStyle = new CellStyle { BorderTop = thin, BorderLeft = thin };
            sheet.GetCell(anchor)!.StyleId = workbook.RegisterStyle(anchorStyle);
            sheet.GetCell(topRight)!.StyleId =
                workbook.RegisterStyle(new CellStyle { BorderTop = thin, BorderRight = thin });
            sheet.GetCell(bottomLeft)!.StyleId =
                workbook.RegisterStyle(new CellStyle { BorderBottom = thin, BorderLeft = thin });
            sheet.GetCell(bottomRight)!.StyleId =
                workbook.RegisterStyle(new CellStyle { BorderBottom = thin, BorderRight = thin });

            sheet.AddMergedRegion(new GridRange(anchor, bottomRight));

            var result = InvokeResolveMergedOuterBorderStyle(
                window, anchorStyle, new GridRange(anchor, bottomRight));

            result.Should().NotBeNull();
            result!.BorderTop.Should().Be(thin, "the anchor's own top edge must still render unchanged");
            result.BorderLeft.Should().Be(thin, "the anchor's own left edge must still render unchanged");
            result.BorderBottom.Should().Be(thin,
                "the bottom edge lives on the bottom-row member cell (B4), never on the anchor -- it must not be dropped");
            result.BorderRight.Should().Be(thin,
                "the right edge lives on the right-column member cell (D2), never on the anchor -- it must not be dropped");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ResolveMergedOuterBorderStyle_NonMergedCell_ReturnsAnchorStyleUnchanged()
    {
        // No-regression sibling: a plain (non-merged) cell must keep using its own style verbatim --
        // no constituent-cell lookups, no cloning, same reference back out.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var thin = new CellBorder(BorderStyle.Thin, CellColor.Black);
            var style = new CellStyle { BorderTop = thin, BorderLeft = thin };

            var resultNoMerge = InvokeResolveMergedOuterBorderStyle(window, style, null);
            resultNoMerge.Should().BeSameAs(style, "a cell with no merge region must pass its own style through unchanged");

            var singleCellAddress = new CellAddress(sheet.Id, 1, 1);
            var resultSingleCellMerge = InvokeResolveMergedOuterBorderStyle(
                window, style, new GridRange(singleCellAddress, singleCellAddress));
            resultSingleCellMerge.Should().BeSameAs(style,
                "a degenerate single-cell merge range (Start == End) must also pass the style through unchanged");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }
}
