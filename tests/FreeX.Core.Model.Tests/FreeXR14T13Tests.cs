using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-14 bucket T13 regression tests (Core.Model.Tests half, chart commands live here). One
/// focused test per finding.
/// </summary>
public sealed class FreeXR14T13Tests
{
    // R14-chart-editing-2: ChangeChartSourceCommand ("Select Chart Data") must clear
    // VerbatimSeriesFormulas (and the column-based SeriesColumnMappings) whenever the DataRange
    // itself changes, not only when the row/column orientation flips. Otherwise the XLSX writer
    // (XlsxChartXmlWriter.Series.cs, verbatim?.ValFormula ?? <range-computed>) keeps emitting the
    // OLD verbatim series formulas after a plain "move the source range" edit, silently reverting
    // the edit on reload.
    [Fact]
    public void ChangeChartSourceCommand_ClearsVerbatimSeriesFormulasOnPlainDataRangeChange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var originalRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 2));
        new AddChartCommand(sheet.Id, originalRange, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        // Simulate a reader-populated verbatim series formula for a multi-area source, as
        // XlsxChartPartReader.ApplyVerbatimSeriesFormulasIfNeeded would after loading an XLSX whose
        // chart has an unparsable series formula.
        chart.SeriesColumnMappings.Add(new ChartSeriesColumnMapping(0, originalRange.Start.Col + 1));
        chart.VerbatimSeriesFormulas = [new ChartSeriesVerbatimFormulas(0, "Sheet1!$B$2:$B$10", null, null)];
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 10, 5));

        // Same orientation (seriesInRows omitted/unchanged) -- only the source range moves, exactly
        // like "Select Chart Data" moving A1:B10 to D1:E10.
        var command = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.DataRange.Should().Be(newRange);
        chart.VerbatimSeriesFormulas.Should().BeNull(
            "the writer prefers verbatim formulas over the range-computed ones, so a stale verbatim " +
            "formula would silently revert the Select-Data edit on reload");
        chart.SeriesColumnMappings.Should().BeEmpty(
            "the old column-based mapping references columns from the OLD range and no longer applies");

        command.Revert(ctx);

        chart.DataRange.Should().Be(originalRange);
        chart.VerbatimSeriesFormulas.Should().ContainSingle(
            "undo must restore the pre-edit verbatim formulas exactly as it already does for orientation flips");
        chart.SeriesColumnMappings.Should().ContainSingle();
    }
}
