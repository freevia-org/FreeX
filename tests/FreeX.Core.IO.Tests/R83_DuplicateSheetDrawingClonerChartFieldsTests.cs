using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R83-meta-2: DuplicateSheetDrawingCloner.CloneChart's object initializer never referenced the
/// three ChartModel fields the SAME r82 commit added -- SeriesOrderOverrides (an explicit
/// &lt;c:order&gt; divergent from &lt;c:idx&gt;, captured after the user reorders series),
/// MultiLevelCategoryXml (a grouped/multi-level &lt;c:cat&gt;&lt;c:multiLvlStrRef&gt; category
/// axis), and PointMarkerFormats (a per-point &lt;c:dPt&gt;&lt;c:marker&gt; override) -- so Duplicate
/// Sheet silently dropped all three onto an empty list even though the source chart carried them.
/// A follow-up full-property diff against the CloneChart initializer additionally found
/// LegendBold/LegendItalic (R45), LegendPositionExplicit (R36), and
/// SecondaryAxisMajorUnit/SecondaryAxisMinorUnit (R62) missing the same way; those are covered here
/// too since they are the identical "new field, stale cloner consumer" self-regression class.
/// Verifies each field now survives Duplicate Sheet, plus sibling no-regression cases confirming a
/// chart with none of these fields set still duplicates cleanly.
/// </summary>
public sealed class R83_DuplicateSheetDrawingClonerChartFieldsTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static Sheet CreateChartSheet(Workbook workbook, out GridRange range)
    {
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        return sheet;
    }

    // R83-meta-2 (bug case a): a manually reordered series (whose <c:order> diverges from its
    // <c:idx>) must keep its explicit order override on the duplicate, or the copy silently reverts
    // to positional idx order.
    [Fact]
    public void DuplicateSheet_ChartWithSeriesOrderOverride_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartCloneSeriesOrderOverride");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = range,
            SeriesOrderOverrides = [new ChartSeriesOrderOverride(0, 3)]
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.SeriesOrderOverrides.Should().ContainSingle(
            "a series' explicit plot-order override must not be dropped by Duplicate Sheet")
            .Which.Should().Be(new ChartSeriesOrderOverride(0, 3));
    }

    // R83-meta-2 (bug case b): a multi-level/grouped category axis's verbatim <c:cat> XML must
    // survive the duplicate, or the copy falls back to a flat computed category list and silently
    // discards the outer grouping level.
    [Fact]
    public void DuplicateSheet_ChartWithMultiLevelCategoryXml_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartCloneMultiLevelCategoryXml");
        var sheet = CreateChartSheet(workbook, out var range);
        const string rawXml = "<c:cat><c:multiLvlStrRef>...</c:multiLvlStrRef></c:cat>";
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            MultiLevelCategoryXml = [new ChartSeriesRawXmlEntry(0, rawXml)]
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.MultiLevelCategoryXml.Should().ContainSingle(
            "a grouped multi-level category axis's verbatim XML must not be dropped by Duplicate Sheet")
            .Which.Should().Be(new ChartSeriesRawXmlEntry(0, rawXml));
    }

    // R83-meta-2 (bug case c): a per-point custom marker override (Format Data Point > Marker
    // Options) must survive the duplicate, or the copied chart's point silently reverts to the
    // series-level marker.
    [Fact]
    public void DuplicateSheet_ChartWithPointMarkerFormat_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartClonePointMarkerFormat");
        var sheet = CreateChartSheet(workbook, out var range);
        var markerFormat = new ChartPointMarkerFormat(
            SeriesIndex: 0,
            PointIndex: 1,
            MarkerStyle: ChartMarkerStyle.Star,
            MarkerSize: 9);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = range,
            PointMarkerFormats = [markerFormat]
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.PointMarkerFormats.Should().ContainSingle(
            "a per-point marker override must not be dropped by Duplicate Sheet")
            .Which.Should().Be(markerFormat);
    }

    // Sibling no-regression case: a chart with none of the three r82 fields set must still
    // duplicate cleanly, leaving all three at their empty-list default.
    [Fact]
    public void DuplicateSheet_ChartWithoutSeriesOrderCategoryOrMarkerOverrides_LeavesFieldsEmpty()
    {
        var workbook = new Workbook("ChartCloneR82FieldsDefault");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.SeriesOrderOverrides.Should().BeEmpty();
        copiedChart.MultiLevelCategoryXml.Should().BeEmpty();
        copiedChart.PointMarkerFormats.Should().BeEmpty();
    }

    // R83-meta-2 (follow-up sweep, bug case d): the legend's forced Bold/Italic and its "explicit
    // position" gate flag must survive Duplicate Sheet, not silently revert to null/unset.
    [Fact]
    public void DuplicateSheet_ChartWithLegendBoldItalicAndExplicitPosition_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartCloneLegendBoldItalicExplicitPosition");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = range,
            ShowLegend = true,
            LegendBold = true,
            LegendItalic = true,
            LegendPositionExplicit = true
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.LegendBold.Should().BeTrue(
            "the legend's forced-bold flag must not be dropped by Duplicate Sheet");
        copiedChart.LegendItalic.Should().BeTrue(
            "the legend's forced-italic flag must not be dropped by Duplicate Sheet");
        copiedChart.LegendPositionExplicit.Should().BeTrue(
            "the legend's explicit-position gate flag must not be dropped by Duplicate Sheet");
    }

    // R83-meta-2 (follow-up sweep, bug case e): the secondary value axis's own major/minor unit
    // must survive Duplicate Sheet, not silently fall back to the primary axis's unit.
    [Fact]
    public void DuplicateSheet_ChartWithSecondaryAxisMajorMinorUnit_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartCloneSecondaryAxisMajorMinorUnit");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = range,
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            SecondaryAxisMajorUnit = 5,
            SecondaryAxisMinorUnit = 1
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.SecondaryAxisMajorUnit.Should().Be(5,
            "the secondary axis's own major unit must not be dropped by Duplicate Sheet");
        copiedChart.SecondaryAxisMinorUnit.Should().Be(1,
            "the secondary axis's own minor unit must not be dropped by Duplicate Sheet");
    }

    // Sibling no-regression case: a chart with none of the legend/secondary-axis-unit fields set
    // must still duplicate cleanly, leaving them all null.
    [Fact]
    public void DuplicateSheet_ChartWithoutLegendOrSecondaryAxisUnitOverrides_LeavesFieldsNull()
    {
        var workbook = new Workbook("ChartCloneLegendSecondaryAxisUnitDefault");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.LegendBold.Should().BeNull();
        copiedChart.LegendItalic.Should().BeNull();
        copiedChart.LegendPositionExplicit.Should().BeNull();
        copiedChart.SecondaryAxisMajorUnit.Should().BeNull();
        copiedChart.SecondaryAxisMinorUnit.Should().BeNull();
    }
}
