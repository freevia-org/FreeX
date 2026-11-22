using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// Excel parity for a banded structured table carrying DIRECT cell formats after Sort (A-Z, Z-A,
// custom list, by cell color) and AutoFilter apply/clear. Expected rows were recorded from real
// Excel over COM (Table "T1" A1:C9, TableStyleMedium2, banded rows; direct formats: yellow fill on
// A3/C5/C8, orange fill on A6, bold+red font on A4, "0.00" number format on B7) using
// Range.DisplayFormat.Interior.Color for the effective fill and Interior.ColorIndex for direct
// fills. What Excel does, and these tests pin:
//   * the FIRST visible data row carries the style's stripe (tinted), the next is unstriped, ...;
//   * banding re-flows by VISIBLE row position after every sort/filter (hidden rows do not consume
//     a stripe slot);
//   * direct formats (fill, bold, font color, number format) travel with their row when sorted and
//     are never replaced by a re-flowing stripe;
//   * Sort/Filter only permute/hide rows; a sort with an active filter reorders the visible rows
//     only, among the visible slots.
// Fill codes: T = style stripe (tint), W = unstriped, Y = direct yellow, O = direct orange.
public sealed class ExcelParity_TableSortFilterDirectFormatTests
{
    private static readonly CellColor Yellow = new(255, 255, 0);
    private static readonly CellColor Orange = new(255, 165, 0);
    private static readonly CellColor Red = new(255, 0, 0);

    private static readonly string[] Baseline =
    [
        "Delta TTT",
        "alpha YWW",
        "Charlie TTT bold",
        "bravo WWY",
        "Echo OTT",
        "Foxtrot WWW fmt",
        "Golf TTY",
        "Hotel WWW",
    ];

    [Fact]
    public void Baseline_FirstDataRowCarriesTheStripe()
    {
        var (workbook, sheet, _) = Build();
        Describe(workbook, sheet).Should().Equal(Baseline);
    }

    [Fact]
    public void SortAtoZ_MovesDirectFormatsWithRows_AndReflowsBanding()
    {
        var (workbook, sheet, ctx) = Build();
        Sort(ctx, sheet, new SortKey(0, true));

        Describe(workbook, sheet).Should().Equal(
            "alpha YTT",
            "bravo WWY",
            "Charlie TTT bold",
            "Delta WWW",
            "Echo OTT",
            "Foxtrot WWW fmt",
            "Golf TTY",
            "Hotel WWW");
    }

    [Fact]
    public void SortZtoA_MovesDirectFormatsWithRows_AndReflowsBanding()
    {
        var (workbook, sheet, ctx) = Build();
        Sort(ctx, sheet, new SortKey(0, false));

        Describe(workbook, sheet).Should().Equal(
            "Hotel TTT",
            "Golf WWY",
            "Foxtrot TTT fmt",
            "Echo OWW",
            "Delta TTT",
            "Charlie WWW bold",
            "bravo TTY",
            "alpha YWW");
    }

    [Fact]
    public void SortByCustomList_MovesDirectFormatsWithRows_AndReflowsBanding()
    {
        var (workbook, sheet, ctx) = Build();
        CustomSortOrder.TryParse("High,Medium,Low", out var order).Should().BeTrue();
        Sort(ctx, sheet, new SortKey(2, true, SortOn.CellValues, CustomOrder: order));

        Describe(workbook, sheet).Should().Equal(
            "alpha YTT",
            "bravo WWY",
            "Golf TTY",
            "Charlie WWW bold",
            "Foxtrot TTT fmt",
            "Delta WWW",
            "Echo OTT",
            "Hotel WWW");
    }

    [Fact]
    public void SortByCellColor_MovesDirectFormatsWithRows_AndReflowsBanding()
    {
        var (workbook, sheet, ctx) = Build();
        Sort(ctx, sheet, new SortKey(2, true, SortOn.CellColor, TargetColor: Yellow));

        Describe(workbook, sheet).Should().Equal(
            "bravo TTY",
            "Golf WWY",
            "Delta TTT",
            "alpha YWW",
            "Charlie TTT bold",
            "Echo OWW",
            "Foxtrot TTT fmt",
            "Hotel WWW");
    }

    [Fact]
    public void ApplyFilter_BandsByVisibleRow_AndKeepsDirectFormats()
    {
        var (workbook, sheet, ctx) = Build();
        Filter(ctx, sheet, "High");

        Describe(workbook, sheet).Should().Equal(
            "Delta hidden",
            "alpha YTT",
            "Charlie hidden",
            "bravo WWY",
            "Echo hidden",
            "Foxtrot hidden",
            "Golf TTY",
            "Hotel hidden");
    }

    [Fact]
    public void ClearFilter_RestoresPositionalBanding_AndKeepsDirectFormats()
    {
        var (workbook, sheet, ctx) = Build();
        Filter(ctx, sheet, "High");
        Filter(ctx, sheet);

        Describe(workbook, sheet).Should().Equal(Baseline);
    }

    [Fact]
    public void SortWhileFiltered_ReordersVisibleRowsOnly_ThenClearReflowsBanding()
    {
        var (workbook, sheet, ctx) = Build();
        Filter(ctx, sheet, "High");
        Sort(ctx, sheet, new SortKey(0, false));

        Describe(workbook, sheet).Should().Equal(
            "Delta hidden",
            "Golf TTY",
            "Charlie hidden",
            "bravo WWY",
            "Echo hidden",
            "Foxtrot hidden",
            "alpha YTT",
            "Hotel hidden");

        Filter(ctx, sheet);

        Describe(workbook, sheet).Should().Equal(
            "Delta TTT",
            "Golf WWY",
            "Charlie TTT bold",
            "bravo WWY",
            "Echo OTT",
            "Foxtrot WWW fmt",
            "alpha YTT",
            "Hotel WWW");
    }

    [Fact]
    public void RebandKeepsDirectFill_ButStillReplacesStaleBandFill()
    {
        var (workbook, sheet, ctx) = Build();
        var table = sheet.StructuredTables.Single();

        // Reband twice: stripes already correct, direct fills untouched, no drift.
        StructuredTableStyleService.RebandTable(workbook, sheet, table);
        StructuredTableStyleService.RebandTable(workbook, sheet, table);

        Describe(workbook, sheet).Should().Equal(Baseline);
    }

    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Context) Build()
    {
        var workbook = new Workbook("TableSortFilterParity");
        var sheet = workbook.AddSheet("Data");
        string[][] rows =
        [
            ["Name", "Qty", "Prio"],
            ["Delta", "4", "Low"],
            ["alpha", "1", "High"],
            ["Charlie", "3", "Medium"],
            ["bravo", "2", "High"],
            ["Echo", "5", "Low"],
            ["Foxtrot", "6", "Medium"],
            ["Golf", "7", "High"],
            ["Hotel", "8", "Low"],
        ];
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                var address = new CellAddress(sheet.Id, (uint)(r + 1), (uint)(c + 1));
                sheet.SetCell(address, r > 0 && c == 1
                    ? new NumberValue(double.Parse(rows[r][c]))
                    : new TextValue(rows[r][c]));
            }
        }

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "T1",
            DisplayName = "T1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 9, 3)),
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true,
        });
        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();

        var ctx = new TestCommandContext(workbook);
        Style(ctx, sheet, 3, 1, new StyleDiff(FillColor: Yellow));
        Style(ctx, sheet, 5, 3, new StyleDiff(FillColor: Yellow));
        Style(ctx, sheet, 8, 3, new StyleDiff(FillColor: Yellow));
        Style(ctx, sheet, 6, 1, new StyleDiff(FillColor: Orange));
        Style(ctx, sheet, 4, 1, new StyleDiff(Bold: true, FontColor: Red));
        Style(ctx, sheet, 7, 2, new StyleDiff(NumberFormat: "0.00"));
        return (workbook, sheet, ctx);
    }

    private static void Style(TestCommandContext ctx, Sheet sheet, uint row, uint col, StyleDiff diff)
    {
        var address = new CellAddress(sheet.Id, row, col);
        new ApplyStyleCommand(sheet.Id, new GridRange(address, address), diff).Apply(ctx).Success.Should().BeTrue();
    }

    private static void Sort(TestCommandContext ctx, Sheet sheet, SortKey key)
    {
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 9, 3));
        new SortCommand(sheet.Id, range, [key]).Apply(ctx).Success.Should().BeTrue();
    }

    private static void Filter(TestCommandContext ctx, Sheet sheet, params string[] allowedPrio)
    {
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 9, 3));
        new FilterCommand(sheet.Id, range, filterColOffset: 2, allowedPrio).Apply(ctx).Success.Should().BeTrue();
    }

    private static IReadOnlyList<string> Describe(Workbook workbook, Sheet sheet)
    {
        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);
        var rows = new List<string>();
        for (uint row = 2; row <= 9; row++)
        {
            var name = ((TextValue)sheet.GetValue(row, 1)).Value;
            if (sheet.IsRowEffectivelyHidden(row))
            {
                rows.Add($"{name} hidden");
                continue;
            }

            var fills = string.Concat(Enumerable.Range(1, 3).Select(col => FillCode(workbook, sheet, row, (uint)col, banding)));
            var nameStyle = workbook.GetStyle(sheet.GetCell(row, 1)!.StyleId);
            var qtyStyle = workbook.GetStyle(sheet.GetCell(row, 2)!.StyleId);
            rows.Add($"{name} {fills}" +
                     (nameStyle.Bold && nameStyle.FontColor == Red ? " bold" : "") +
                     (qtyStyle.NumberFormat == "0.00" ? " fmt" : ""));
        }

        return rows;
    }

    private static char FillCode(Workbook workbook, Sheet sheet, uint row, uint col, StructuredTableStyleBanding banding)
    {
        var fill = workbook.GetStyle(sheet.GetCell(row, col)!.StyleId).FillColor;
        if (fill == Yellow) return 'Y';
        if (fill == Orange) return 'O';
        if (fill == banding.OddRowFill) return 'T';
        if (fill == banding.EvenRowFill) return 'W';
        return '?';
    }
}
