using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R98: <see cref="RemoveChartSeriesCommand"/> remapped every other SeriesIndex-keyed per-series
/// list (SeriesOrderOverrides, PointMarkerFormats, etc. -- see
/// <see cref="R92_RemoveChartSeriesCommandTests"/>) but never touched
/// <see cref="ChartModel.VerbatimSeriesFormulas"/>, which holds the raw formula strings for any
/// series whose source could not be parsed as a rectangular range (named range, multi-area union,
/// or external-workbook reference -- see <see cref="ChartSeriesVerbatimFormulas"/>). Because the
/// XLSX writer's <c>GetVerbatimFormulas</c> looks this list up by the CURRENT (post-removal,
/// re-indexed) SeriesIndex, a stale entry silently binds to whichever unrelated series shifted
/// into the vacated index -- e.g. removing series 0 would make a named-range formula that used to
/// belong to series 1 attach to the series that is now at index 0 instead. This mirrors the exact
/// handling <see cref="ChangeChartSourceCommand"/> already performs for this same list (see
/// ChartCommands.Mutate.cs's clearing of VerbatimSeriesFormulas alongside SeriesColumnMappings).
/// </summary>
public sealed class R98_RemoveChartSeriesCommandVerbatimFormulasTests
{
    private static GridRange ThreeSeriesRange(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));

    private static (Sheet Sheet, TestCommandContext Ctx, ChartModel Chart) CreateThreeSeriesChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = ThreeSeriesRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        return (sheet, ctx, chart);
    }

    [Fact]
    public void RemoveChartSeriesCommand_RemapsVerbatimSeriesFormulasAboveRemovedIndexAndDropsRemovedEntry()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        // SeriesIndex 0: untouched, ordinary named-range series below the removed index.
        // SeriesIndex 1: the series being removed -- its verbatim entry must be dropped entirely.
        // SeriesIndex 2: above the removed index -- must shift down to SeriesIndex 1 so the XLSX
        // writer's GetVerbatimFormulas(chart, 1) lookup (post-removal) still finds THIS series'
        // formula rather than falling through to nothing or picking up a stale/wrong entry.
        chart.VerbatimSeriesFormulas =
        [
            new ChartSeriesVerbatimFormulas(0, "Sheet1!NamedRangeA", null, "Sheet1!$B$1"),
            new ChartSeriesVerbatimFormulas(1, "Sheet1!$C$1:$C$5,Sheet1!$C$8:$C$10", null, "Sheet1!$C$1"),
            new ChartSeriesVerbatimFormulas(2, "Sheet1!NamedRangeC", null, "Sheet1!$D$1")
        ];

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.VerbatimSeriesFormulas.Should().BeEquivalentTo(
        [
            new ChartSeriesVerbatimFormulas(0, "Sheet1!NamedRangeA", null, "Sheet1!$B$1"),
            new ChartSeriesVerbatimFormulas(1, "Sheet1!NamedRangeC", null, "Sheet1!$D$1") // was SeriesIndex 2
        ]);
    }

    [Fact]
    public void RemoveChartSeriesCommand_UndoRestoresVerbatimSeriesFormulasAtOriginalIndexes()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        var original = new List<ChartSeriesVerbatimFormulas>
        {
            new(0, "Sheet1!NamedRangeA", null, "Sheet1!$B$1"),
            new(1, "Sheet1!$C$1:$C$5,Sheet1!$C$8:$C$10", null, "Sheet1!$C$1"),
            new(2, "Sheet1!NamedRangeC", null, "Sheet1!$D$1")
        };
        chart.VerbatimSeriesFormulas = new List<ChartSeriesVerbatimFormulas>(original);
        var command = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1);

        command.Apply(ctx).Success.Should().BeTrue();
        chart.VerbatimSeriesFormulas.Should().HaveCount(2);

        command.Revert(ctx);

        chart.VerbatimSeriesFormulas.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void RemoveChartSeriesCommand_LeavesNullVerbatimSeriesFormulasNull()
    {
        // Sibling/no-regression case: most charts have no verbatim formulas at all (the field is
        // null, not an empty list, until XlsxChartSeriesRangeReader populates it for an
        // unparseable series). Removing a series must not spuriously allocate an empty list here.
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.VerbatimSeriesFormulas.Should().BeNull();

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.VerbatimSeriesFormulas.Should().BeNull();

        new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1).Revert(ctx);
        chart.VerbatimSeriesFormulas.Should().BeNull();
    }
}
