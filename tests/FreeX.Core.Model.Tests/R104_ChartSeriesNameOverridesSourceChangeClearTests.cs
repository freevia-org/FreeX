using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R104: <see cref="ChangeChartSourceCommand"/> clears every other SeriesIndex-keyed collection
/// (SeriesColumnMappings, SeriesOrderOverrides, SeriesPlotOrder, LegendEntries, etc.) when the
/// chart's data range or orientation changes, per its own explanatory comment -- but was missing
/// <see cref="ChartModel.SeriesNameOverrides"/> (a per-series custom "Series name" cell-reference
/// captured from a &lt;c:tx&gt; formula on read, R103-io-chart-series-tx-1). Leaving a stale entry
/// behind lets <c>XlsxChartXmlWriter.Series.cs</c>'s <c>ResolveSeriesTitleXml</c> (which
/// unconditionally prefers a SeriesNameOverrides entry for the CURRENT positional SeriesIndex)
/// silently attach an old custom series name to whichever unrelated series now sits at that index
/// after a routine "Select Data" range edit widens/narrows/reorients the source.
/// </summary>
public sealed class R104_ChartSeriesNameOverridesSourceChangeClearTests
{
    [Fact]
    public void ChangeChartSourceCommand_ClearsSeriesNameOverridesOnDataRangeChange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var originalRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));
        new AddChartCommand(sheet.Id, originalRange, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        // Series at SeriesIndex 1 has a custom "Series name" cell reference (Select Data > Edit
        // Series > Series name), captured verbatim as a SeriesNameOverrides entry by the reader.
        chart.SeriesNameOverrides = [new ChartSeriesNameOverride(1, "Sheet1!$Z$1")];
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 3));

        // Widen the source range via "Select Data" -- a new column is inserted in front of the
        // existing series span, so what is now SeriesIndex 1 is a DIFFERENT physical series than
        // before the edit.
        var outcome = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.SeriesNameOverrides.Should().BeEmpty(
            "the stale SeriesIndex-keyed override must not silently attach the old custom name to whichever series now sits at index 1");
    }

    [Fact]
    public void ChangeChartSourceCommand_IsUndoable_RestoresSeriesNameOverrides()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var originalRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));
        new AddChartCommand(sheet.Id, originalRange, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.SeriesNameOverrides = [new ChartSeriesNameOverride(1, "Sheet1!$Z$1")];
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 3));
        var command = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange);

        command.Apply(ctx).Success.Should().BeTrue();
        chart.SeriesNameOverrides.Should().BeEmpty();

        command.Revert(ctx);

        chart.SeriesNameOverrides.Should().ContainSingle()
            .Which.Should().Be(new ChartSeriesNameOverride(1, "Sheet1!$Z$1"));
    }

    [Fact]
    public void ChangeChartSourceCommand_NoRangeOrOrientationChange_LeavesSeriesNameOverridesIntact()
    {
        // No-regression sibling: when Apply is a no-op re-save of the SAME range/orientation (the
        // `if` guard's condition is false), the clearing block must not run at all -- the override
        // still refers to the correct series and must survive untouched.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.SeriesNameOverrides = [new ChartSeriesNameOverride(1, "Sheet1!$Z$1")];

        // Same range, same orientation -- e.g. re-confirming the Select Data dialog without
        // actually changing anything.
        var outcome = new ChangeChartSourceCommand(sheet.Id, chart.Id, range).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.SeriesNameOverrides.Should().ContainSingle()
            .Which.Should().Be(new ChartSeriesNameOverride(1, "Sheet1!$Z$1"));
    }
}
