using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// freex-theme-border-color-F1 (round-175 meta-F3 follow-up): <see cref="CellBorder"/> carries an
/// optional <see cref="CellBorder.ThemeColor"/> alongside the RGB baked in at load time, and
/// <see cref="CellBorder.ResolveColor"/> exists specifically to re-resolve it against the CURRENT
/// workbook theme -- mirroring <c>CellStyle.ResolveFontColor</c>/<c>ResolveFillColor</c>.
/// <c>PageContentRenderModelBuilder.ResolveBorders</c>/<c>ResolveEdge</c> read <c>border.Color</c>
/// raw, so a theme change recolored every fill and font on the print-preview / portable-PDF page
/// model (both of which already resolve through <c>ResolveFill</c>/<c>ResolveFont</c>) but left the
/// borders stranded on the old palette.
/// </summary>
public sealed class R175_PageContentRenderModelBuilderThemeBorderColorTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    // Deliberately unlike the stock Office Accent1 (21, 96, 130) so "followed the new theme" and
    // "kept the baked-in load-time color" can never be confused for one another.
    private static readonly CellColor SwappedAccent1 = new(7, 200, 111);

    [Fact]
    public void Build_ThemeBackedBorder_FollowsSwappedThemeAccentColor()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);

        var themeRef = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1);
        // Color = the RGB resolved against the ORIGINAL theme at load time (what the buggy code read).
        var bakedIn = themeRef.Resolve(workbook.Theme);
        var style = new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin, bakedIn, themeRef),
            BorderRight = new CellBorder(BorderStyle.Thin, bakedIn, themeRef),
            BorderBottom = new CellBorder(BorderStyle.Thin, bakedIn, themeRef),
            BorderLeft = new CellBorder(BorderStyle.Thin, bakedIn, themeRef),
        };
        var cell = Cell.FromValue(new TextValue("Total"));
        cell.StyleId = workbook.RegisterStyle(style);
        sheet.SetCell(address, cell);

        workbook.Theme = workbook.Theme.WithColor(WorkbookThemeColorSlot.Accent1, SwappedAccent1);

        // Ground truth is the model's own resolver, not a hard-coded RGB.
        var expected = PresentationRgb.FromCellColor(
            style.BorderTop.ResolveColor(workbook.Theme));
        expected.Should().NotBe(PresentationRgb.FromCellColor(bakedIn),
            "the swapped accent must actually differ from the load-time baked color, or this test proves nothing");

        var block = BuildFirstPage(workbook, sheet)!.Cells.Single(c => c.Row == 1 && c.Column == 1);

        block.Borders.Top.Color.Should().Be(expected);
        block.Borders.Right.Color.Should().Be(expected);
        block.Borders.Bottom.Color.Should().Be(expected);
        block.Borders.Left.Color.Should().Be(expected);
    }

    [Fact]
    public void Build_ExplicitRgbBorder_KeepsItsOwnColorAcrossThemeChange()
    {
        // No-regression sibling: a border with no ThemeColor is pinned to its authored RGB and must
        // be completely unaffected by a theme swap (CellBorder.ResolveColor falls back to Color).
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);

        var explicitColor = new CellColor(200, 0, 0);
        var style = new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin, explicitColor),
            BorderBottom = new CellBorder(BorderStyle.Thin, explicitColor),
        };
        var cell = Cell.FromValue(new TextValue("Total"));
        cell.StyleId = workbook.RegisterStyle(style);
        sheet.SetCell(address, cell);

        workbook.Theme = workbook.Theme.WithColor(WorkbookThemeColorSlot.Accent1, SwappedAccent1);

        var block = BuildFirstPage(workbook, sheet)!.Cells.Single(c => c.Row == 1 && c.Column == 1);

        block.Borders.Top.Color.Should().Be(PresentationRgb.FromCellColor(explicitColor));
        block.Borders.Bottom.Color.Should().Be(PresentationRgb.FromCellColor(explicitColor));
    }

    [Fact]
    public void Build_ThemeBackedConditionalFormatBorder_FollowsSwappedThemeAccentColor()
    {
        // ApplyConditionalBorderDelta resolves the matched CF rule's per-edge border through the very
        // same ResolveEdge helper, so a dxf border carrying a ThemeColor must track the theme too.
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);

        var themeRef = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1);
        var bakedIn = themeRef.Resolve(workbook.Theme);
        var cfBorder = new CellBorder(BorderStyle.Medium, bakedIn, themeRef);

        var cell = Cell.FromValue(new NumberValue(150));
        cell.StyleId = workbook.RegisterStyle(new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin, new CellColor(1, 2, 3)),
        });
        sheet.SetCell(address, cell);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            AppliesTo = new GridRange(address, address),
            FormatIfTrue = new CellStyle { BorderTop = cfBorder },
        });

        workbook.Theme = workbook.Theme.WithColor(WorkbookThemeColorSlot.Accent1, SwappedAccent1);

        var expected = PresentationRgb.FromCellColor(cfBorder.ResolveColor(workbook.Theme));
        var block = BuildFirstPage(workbook, sheet)!.Cells.Single(c => c.Row == 1 && c.Column == 1);

        block.Borders.Top.Style.Should().Be(BorderStyle.Medium, "the matched CF rule's edge must win over the raw style's");
        block.Borders.Top.Color.Should().Be(expected);
    }

    private static PageContentLayout? BuildFirstPage(Workbook workbook, Sheet sheet) =>
        PageContentRenderModelBuilder.Build(workbook, sheet, Paginate(sheet), 0, Measurer, new DateTime(2026, 1, 1));

    private static PagePaginationResult Paginate(Sheet sheet)
    {
        var printRange = sheet.PrintArea ?? sheet.GetUsedRange()
            ?? new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        return PagePaginationPlanner.Paginate(
            printRange,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);
    }

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook()
    {
        var workbook = new Workbook { Name = "Book1.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }
}
