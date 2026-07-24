using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R84-order-guard-invented-sweep-2: eight ChartModel fields added across the r80-r83 waves
/// (LegendBold, LegendItalic, LegendPositionExplicit, SecondaryAxisMajorUnit,
/// SecondaryAxisMinorUnit, SeriesOrderOverrides, MultiLevelCategoryXml, PointMarkerFormats) all
/// round-trip through XLSX (dedicated reader/writer pairs exist for each), but ChartDto
/// (NativeJsonAdapter.ChartDto.cs) never carried them, so TryLoadChart/ToChartDto never
/// read/wrote them either -- every one of these settings was silently dropped on a native .fxl
/// save+reload, even though .fxl is FreeX's own supposedly-lossless persistence format.
/// </summary>
public sealed class R84_NativeJsonChartLegendBoldSecondaryAxisUnitSeriesOrderMarkerTests
{
    private static Workbook BuildWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("ChartR84Fields");
        sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Cost"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(4));
        return workbook;
    }

    // Failing before the fix: ChartDto had no properties for any of these eight fields, so
    // TryLoadChart/ToChartDto silently dropped every one of them on a native .fxl save+reload --
    // the legend reverted to non-bold/non-italic with a recomputed position, the secondary axis
    // units reverted to "not captured" (writer falls back to cloning the primary axis), the
    // series plot-order override was lost, the multi-level category XML was lost, and the
    // per-point marker override was lost.
    [Fact]
    public void NativeJsonAdapter_RoundTrip_LegendBoldSecondaryAxisUnitSeriesOrderAndMarkerFields()
    {
        var workbook = BuildWorkbook(out var sheet);
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            LegendBold = true,
            LegendItalic = true,
            LegendPositionExplicit = true,
            LegendPosition = ChartLegendPosition.Right,
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            SecondaryAxisMajorUnit = 25,
            SecondaryAxisMinorUnit = 5,
            SeriesOrderOverrides = [new ChartSeriesOrderOverride(SeriesIndex: 0, Order: 1)],
            MultiLevelCategoryXml = [new ChartSeriesRawXmlEntry(SeriesIndex: 0, RawXml: "<c:cat><c:multiLvlStrRef/></c:cat>")],
            PointMarkerFormats =
            [
                new ChartPointMarkerFormat(
                    SeriesIndex: 0,
                    PointIndex: 1,
                    MarkerStyle: ChartMarkerStyle.Diamond,
                    MarkerSize: 9,
                    FillColor: new CellColor(255, 0, 0),
                    BorderColor: new CellColor(0, 0, 0),
                    BorderThickness: 1.5)
            ]
        };
        sheet.Charts.Add(chart);

        using var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        loaded.LegendBold.Should().Be(true, "a bold legend must survive a native .fxl save+reload");
        loaded.LegendItalic.Should().Be(true, "an italic legend must survive a native .fxl save+reload");
        loaded.LegendPositionExplicit.Should().Be(true,
            "an explicit legend position must survive a native .fxl save+reload");
        loaded.SecondaryAxisMajorUnit.Should().Be(25,
            "the secondary axis's own major unit must survive a native .fxl save+reload");
        loaded.SecondaryAxisMinorUnit.Should().Be(5,
            "the secondary axis's own minor unit must survive a native .fxl save+reload");
        loaded.SeriesOrderOverrides.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesOrderOverride(SeriesIndex: 0, Order: 1));
        loaded.MultiLevelCategoryXml.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesRawXmlEntry(SeriesIndex: 0, RawXml: "<c:cat><c:multiLvlStrRef/></c:cat>"));
        loaded.PointMarkerFormats.Should().ContainSingle().Which.Should().Be(
            new ChartPointMarkerFormat(
                SeriesIndex: 0,
                PointIndex: 1,
                MarkerStyle: ChartMarkerStyle.Diamond,
                MarkerSize: 9,
                FillColor: new CellColor(255, 0, 0),
                BorderColor: new CellColor(0, 0, 0),
                BorderThickness: 1.5));
    }

    // No-regression sibling: a chart that never set any of these eight fields keeps round-tripping
    // with their ordinary defaults (null scalars, empty lists), and unrelated chart metadata is
    // unaffected.
    [Fact]
    public void NativeJsonAdapter_RoundTrip_ChartWithoutR84Fields_KeepsDefaultsAndUnrelatedMetadata()
    {
        var workbook = BuildWorkbook(out var sheet);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            Title = "Revenue"
        });

        using var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        loaded.LegendBold.Should().BeNull();
        loaded.LegendItalic.Should().BeNull();
        loaded.LegendPositionExplicit.Should().BeNull();
        loaded.SecondaryAxisMajorUnit.Should().BeNull();
        loaded.SecondaryAxisMinorUnit.Should().BeNull();
        loaded.SeriesOrderOverrides.Should().BeEmpty();
        loaded.MultiLevelCategoryXml.Should().BeEmpty();
        loaded.PointMarkerFormats.Should().BeEmpty();
        loaded.Title.Should().Be("Revenue", "unrelated chart metadata must be unaffected by the new fields");
    }
}
