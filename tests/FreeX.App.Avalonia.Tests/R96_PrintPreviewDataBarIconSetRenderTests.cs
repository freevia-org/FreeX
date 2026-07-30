using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;

using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

using FluentAssertions;

using AvaloniaEllipseShape = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaPolygonShape = Avalonia.Controls.Shapes.Polygon;
using AvaloniaRectangleShape = Avalonia.Controls.Shapes.Rectangle;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R96-render-cf-databar-iconset-preview-1: this round's PDF-export fix (R96-render-cf-databar-iconset-1
/// in FreeX.App.Services) taught <c>WorkbookPdfContentBuilder</c> to paint data-bar and icon-set
/// conditional formats, but the Avalonia interactive Print Preview renderer
/// (<see cref="MainWindow.BuildPreviewPageView"/> / <see cref="PrintPreviewInstructionBuilder"/>) still
/// silently dropped both, even though the shared <c>PageContentRenderModelBuilder</c> already computed
/// them onto <c>PageCellBlock.DataBar</c>/<c>IconSet</c> -- that record's own doc comment said so
/// ("no current renderer paints them yet"). These tests drive the real product entry point --
/// <see cref="PrintPreviewPaginationContext.TryCreate"/> -&gt; <see cref="MainWindow.BuildPreviewPageView"/>,
/// the same call chain <c>ShowPrintPreviewWindowCoreAsync</c>'s <c>Render()</c> uses -- and assert the
/// actual Avalonia shapes (fill rectangle / ellipse glyph) land in the canvas, not merely that the
/// portable layout model carries the data.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R96_PrintPreviewDataBarIconSetRenderTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task BuildPreviewPageView_DataBarRule_PaintsBarRectanglesAtZeroMidAndFullFraction()
    {
        await Session.Dispatch(() =>
        {
            var (workbook, sheet) = CreateWorkbook();
            var a1 = new CellAddress(sheet.Id, 1, 1); // 0%   -> Excel/ConditionalFormatEvaluator reports no bar
            var a2 = new CellAddress(sheet.Id, 2, 1); // 50%  -> half-width bar
            var a3 = new CellAddress(sheet.Id, 3, 1); // 100% -> full-width bar
            sheet.SetCell(a1, new NumberValue(0));
            sheet.SetCell(a2, new NumberValue(50));
            sheet.SetCell(a3, new NumberValue(100));

            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                RuleType = CfRuleType.DataBar,
                AppliesTo = new GridRange(a1, a3),
                DataBarMinThresholdType = CfThresholdType.AutoMin,
                DataBarMaxThresholdType = CfThresholdType.AutoMax,
            });
            sheet.PrintArea = new GridRange(a1, a3);

            var canvas = BuildPreviewCanvas(workbook, sheet);

            // Data bars have no raw cell fill or text-matched CF style, so any non-background-white
            // Rectangle shape must be a data-bar fill -- before the fix, zero such shapes existed
            // anywhere on the canvas regardless of the DataBar rule.
            var barRects = canvas.Children.OfType<AvaloniaRectangleShape>()
                .Where(r => r.Fill is not null && !IsWhiteFill(r))
                .ToList();

            barRects.Should().HaveCount(2,
                "the mid-value (50%) and max-value (100%) cells each draw one bar rectangle; " +
                "the 0%-value cell legitimately draws none (ConditionalFormatEvaluator.EvaluateDataBar reports a zero-length bar as absent)");

            var widths = barRects.Select(r => r.Width).OrderBy(w => w).ToList();
            widths[0].Should().BeGreaterThan(0, "the 50% bar must have positive width");
            widths[1].Should().BeGreaterThan(widths[0], "the 100% bar must be wider than the 50% bar");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BuildPreviewPageView_SheetWithNoDataBarOrIconSetIsUnaffected()
    {
        // No-regression sibling: an ordinary sheet with no CF at all must not gain any Ellipse/Polygon/
        // extra-Rectangle ink from this change.
        await Session.Dispatch(() =>
        {
            var (workbook, sheet) = CreateWorkbook();
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

            var canvas = BuildPreviewCanvas(workbook, sheet);

            canvas.Children.OfType<AvaloniaEllipseShape>().Should().BeEmpty();
            canvas.Children.OfType<AvaloniaPolygonShape>().Should().BeEmpty();
            // Only the white page-background rectangle should exist (no cell fill, no data bar).
            var whiteRects = canvas.Children.OfType<AvaloniaRectangleShape>()
                .Where(IsWhiteFill)
                .ToList();
            whiteRects.Should().ContainSingle();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BuildPreviewPageView_IconSetRule_PaintsOneGlyphPerCell()
    {
        await Session.Dispatch(() =>
        {
            var (workbook, sheet) = CreateWorkbook();
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var a2 = new CellAddress(sheet.Id, 2, 1);
            var a3 = new CellAddress(sheet.Id, 3, 1);
            sheet.SetCell(a1, new NumberValue(0));
            sheet.SetCell(a2, new NumberValue(50));
            sheet.SetCell(a3, new NumberValue(100));

            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                RuleType = CfRuleType.IconSet,
                AppliesTo = new GridRange(a1, a3),
                IconSetStyle = "3TrafficLights1",
            });
            sheet.PrintArea = new GridRange(a1, a3);

            var canvas = BuildPreviewCanvas(workbook, sheet);

            // "3TrafficLights1" draws a filled ellipse glyph per cell -- before the fix, zero Ellipse
            // shapes existed anywhere on the print-preview canvas regardless of the IconSet rule.
            var glyphs = canvas.Children.OfType<AvaloniaEllipseShape>().Where(e => e.Fill is not null).ToList();
            glyphs.Should().HaveCount(3, "each of the three rows resolves its own traffic-light bucket");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BuildPreviewPageView_IconSetArrowStyle_PaintsPolygonGlyph()
    {
        // A different icon-set family (arrows) resolves to a filled Polygon primitive rather than an
        // Ellipse, exercising the other new paint kind this fix adds.
        await Session.Dispatch(() =>
        {
            var (workbook, sheet) = CreateWorkbook();
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var a2 = new CellAddress(sheet.Id, 2, 1);
            var a3 = new CellAddress(sheet.Id, 3, 1);
            sheet.SetCell(a1, new NumberValue(0));
            sheet.SetCell(a2, new NumberValue(50));
            sheet.SetCell(a3, new NumberValue(100));

            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                RuleType = CfRuleType.IconSet,
                AppliesTo = new GridRange(a1, a3),
                IconSetStyle = "3Arrows",
            });
            sheet.PrintArea = new GridRange(a1, a3);

            var canvas = BuildPreviewCanvas(workbook, sheet);

            canvas.Children.OfType<AvaloniaPolygonShape>().Where(p => p.Fill is not null)
                .Should().HaveCount(3, "each row's arrow glyph is a filled polygon");
        }, CancellationToken.None);
    }

    private static bool IsWhiteFill(AvaloniaRectangleShape rect) =>
        rect.Fill is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 255, 255);

    private static Canvas BuildPreviewCanvas(Workbook workbook, Sheet sheet)
    {
        PrintPreviewPaginationContext.TryCreate(
                workbook, sheet, new AvaloniaTextMeasurer(), out var context)
            .Should().BeTrue("the print area / used range must resolve to at least one printable page");

        var pageView = MainWindow.BuildPreviewPageView(context, pageIndex: 0);
        var border = pageView.Should().BeOfType<Border>().Subject;
        return border.Child.Should().BeOfType<Canvas>().Subject;
    }

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook(string name = "Book1.xlsx")
    {
        var workbook = new Workbook { Name = name };
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }
}
