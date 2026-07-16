using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 47 chart-axis findings (io-a-chart-axis bucket): for a horizontal Bar chart the category
/// axis is physically VERTICAL and the value axis is physically HORIZONTAL — the reverse of every
/// other chart family. XlsxChartAxisReader/XlsxChartXmlWriter already routed min/max/majorUnit/
/// minorUnit/logScale/logBase/reverseOrder correctly for this swap (see R16-meta-1), but four more
/// per-axis properties were NOT routed and so were clobbered/lost on every Bar-chart round-trip:
///  - R47-io-chart-axis-scaling-3-1: crosses/crossesAt
///  - R47-io-chart-axis-scaling-3-2: dispUnits/dispUnitsLbl (display units)
///  - R47-io-chart-axis-scaling-3-3: major/minor gridlines (color/thickness/on-off)
///  - R47-io-chart-axis-scaling-3-4: tickLblPos (label visibility/position)
/// Fixed in XlsxChartAxisReader.ApplyCategoryAxisProperties (route by categoryAxisOnY) and
/// XlsxChartXmlWriter.Axes.cs's ToCategoryAxisXml / main ToValueAxisXml call (route by
/// valueAxisOnX/IsHorizontalBarChart), mirroring the existing reverse-order routing pattern.
/// </summary>
public sealed class R47_ChartAxisBarSwapTests
{
    [Fact]
    public void XlsxAdapter_RoundTrip_HorizontalBarChart_KeepsEachAxisOwnCrossesGridlinesDispUnitsAndTickLabelPos()
    {
        var workbook = new Workbook("R47ChartAxisBarSwap");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));

        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2)),

            // Category axis is physically on the LEFT (Y) for a Bar chart — its own crosses/
            // gridlines/labels must land here and stay here.
            YAxisCrosses = ChartAxisCrosses.Maximum,
            YAxisCrossesAt = null,
            ShowYAxisMajorGridlines = false,
            ShowYAxisLabels = true,
            YAxisTickLabelPosition = ChartAxisTickLabelPosition.NextTo,

            // Value axis is physically on the BOTTOM (X) for a Bar chart — its own crossesAt/
            // gridlines/hidden-labels/display-units must land here and stay here, not bleed onto
            // (or get overwritten by) the category axis's Y* fields above.
            XAxisCrosses = ChartAxisCrosses.Custom,
            XAxisCrossesAt = 50,
            ShowXAxisMajorGridlines = true,
            XAxisMajorGridlineColor = new CellColor(255, 0, 0),
            XAxisGridlineThickness = 2,
            ShowXAxisLabels = false,
            XAxisDisplayUnit = ChartAxisDisplayUnit.Thousands,
            ShowXAxisDisplayUnitLabel = true,
        };
        sheet.Charts.Add(chart);

        var adapter = new XlsxFileAdapter();
        using var ms1 = new MemoryStream();
        adapter.Save(workbook, ms1);
        var savedBytes = ms1.ToArray();

        SchemaErrors(savedBytes).Should().BeEmpty("the round-tripped chart XML must remain schema-valid");

        ms1.Position = 0;
        var loaded1 = adapter.Load(ms1);
        var afterFirst = loaded1.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        AssertAxesKeepOwnProperties(afterFirst, "first");

        // Re-save/reload what was just loaded — a true idempotent round-trip must not swap or drop
        // these on a SECOND pass either.
        using var ms2 = new MemoryStream();
        adapter.Save(loaded1, ms2);
        ms2.Position = 0;
        var loaded2 = adapter.Load(ms2);
        var afterSecond = loaded2.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        AssertAxesKeepOwnProperties(afterSecond, "second");
    }

    private static void AssertAxesKeepOwnProperties(ChartModel chart, string pass)
    {
        // R47-io-chart-axis-scaling-3-1: category axis (Y) keeps its own "max" crossing; the value
        // axis's custom crossesAt=50 must not have clobbered it.
        chart.YAxisCrosses.Should().Be(ChartAxisCrosses.Maximum,
            $"the category axis's own crosses must survive the {pass} round-trip");
        // The value axis (X) must keep its own custom crossing point, not the category axis's "max"
        // (and not silently revert to autoZero).
        chart.XAxisCrosses.Should().Be(ChartAxisCrosses.Custom,
            $"the value axis's own crosses must survive the {pass} round-trip");
        chart.XAxisCrossesAt.Should().Be(50,
            $"the value axis's own crossesAt must survive the {pass} round-trip");

        // R47-io-chart-axis-scaling-3-3: value axis (X) keeps its own red/2pt gridlines; the category
        // axis (Y) must NOT have inherited them (it was authored with gridlines off).
        chart.ShowXAxisMajorGridlines.Should().BeTrue(
            $"the value axis's own gridlines must survive the {pass} round-trip");
        chart.XAxisMajorGridlineColor.Should().Be(new CellColor(255, 0, 0),
            $"the value axis's own gridline color must survive the {pass} round-trip");
        chart.ShowYAxisMajorGridlines.Should().BeFalse(
            $"the category axis must not inherit the value axis's gridlines after the {pass} round-trip");

        // R47-io-chart-axis-scaling-3-4: value axis (X) labels stay hidden; category axis (Y) labels
        // stay visible — the hide/show state must not swap axes.
        chart.ShowXAxisLabels.Should().BeFalse(
            $"the value axis's own hidden tick labels must survive the {pass} round-trip");
        chart.ShowYAxisLabels.Should().BeTrue(
            $"the category axis's own visible tick labels must survive the {pass} round-trip");

        // R47-io-chart-axis-scaling-3-2: value axis (X) display units survive.
        chart.XAxisDisplayUnit.Should().Be(ChartAxisDisplayUnit.Thousands,
            $"the value axis's own display units must survive the {pass} round-trip");
        chart.ShowXAxisDisplayUnitLabel.Should().BeTrue(
            $"the value axis's own display-unit label flag must survive the {pass} round-trip");
    }

    /// <summary>
    /// Sibling no-regression test: for a NON-bar chart (Column), the category axis is physically X
    /// and the value axis is physically Y — the routing this fix introduces must be a no-op here,
    /// exactly like it already was before the fix (categoryAxisOnY/valueAxisOnX both false).
    /// </summary>
    [Fact]
    public void XlsxAdapter_RoundTrip_ColumnChart_KeepsEachAxisOwnCrossesGridlinesDispUnitsAndTickLabelPos()
    {
        var workbook = new Workbook("R47ChartAxisColumnNoRegression");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2)),

            // Category axis is physically on the BOTTOM (X) for a Column chart.
            XAxisCrosses = ChartAxisCrosses.Maximum,
            XAxisCrossesAt = null,
            ShowXAxisMajorGridlines = false,
            ShowXAxisLabels = true,

            // Value axis is physically on the LEFT (Y) for a Column chart.
            YAxisCrosses = ChartAxisCrosses.Custom,
            YAxisCrossesAt = 50,
            ShowYAxisMajorGridlines = true,
            YAxisMajorGridlineColor = new CellColor(0, 128, 0),
            YAxisGridlineThickness = 1.5,
            ShowYAxisLabels = false,
            YAxisDisplayUnit = ChartAxisDisplayUnit.Millions,
            ShowYAxisDisplayUnitLabel = true,
        };
        sheet.Charts.Add(chart);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var afterRoundTrip = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        afterRoundTrip.XAxisCrosses.Should().Be(ChartAxisCrosses.Maximum,
            "the category axis's own crosses must be unaffected by the bar-chart routing fix");
        afterRoundTrip.YAxisCrosses.Should().Be(ChartAxisCrosses.Custom,
            "the value axis's own crosses must be unaffected by the bar-chart routing fix");
        afterRoundTrip.YAxisCrossesAt.Should().Be(50,
            "the value axis's own crossesAt must be unaffected by the bar-chart routing fix");

        afterRoundTrip.ShowYAxisMajorGridlines.Should().BeTrue(
            "the value axis's own gridlines must be unaffected by the bar-chart routing fix");
        afterRoundTrip.YAxisMajorGridlineColor.Should().Be(new CellColor(0, 128, 0));
        afterRoundTrip.ShowXAxisMajorGridlines.Should().BeFalse(
            "the category axis must not inherit the value axis's gridlines");

        afterRoundTrip.ShowXAxisLabels.Should().BeTrue();
        afterRoundTrip.ShowYAxisLabels.Should().BeFalse();

        afterRoundTrip.YAxisDisplayUnit.Should().Be(ChartAxisDisplayUnit.Millions);
        afterRoundTrip.ShowYAxisDisplayUnitLabel.Should().BeTrue();
    }

    private static System.Collections.Generic.List<string> SchemaErrors(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }
}
