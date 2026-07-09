using System.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R17-chart-3d-combo-secondary-3: an exploded pie/doughnut where EVERY data point carries
/// &lt;c:explosion&gt; used to lose all but one explosion on XLSX round-trip, because the reader
/// broke on the first exploded &lt;c:dPt&gt; and the model only stored a single scalar
/// (ExplodedSliceIndex/ExplodedSliceDistance). Verifies the new per-point
/// <see cref="ChartModel.ExplodedSlices"/> list preserves every exploded slice, while the
/// legacy scalar single-explosion path still round-trips correctly.
/// </summary>
public class R17_exploded_pie_Tests
{
    [Fact]
    public void FullyExplodedPie_RoundTrip_KeepsExplosionOnEveryPoint()
    {
        var workbook = new Workbook("FullyExplodedPieRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Share"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("D"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("E"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new NumberValue(20));

        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            Title = "Share",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 6, 2)),
            ExplodedSlices =
            [
                new ChartPointExplosion(0, 0, 0.25),
                new ChartPointExplosion(0, 1, 0.25),
                new ChartPointExplosion(0, 2, 0.25),
                new ChartPointExplosion(0, 3, 0.25),
                new ChartPointExplosion(0, 4, 0.25)
            ]
        };
        sheet.Charts.Add(chart);

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        loadedChart.Type.Should().Be(ChartType.Pie);
        loadedChart.ExplodedSlices.Should().HaveCount(5,
            "every exploded slice must survive the round-trip, not just the first");
        foreach (var pointIndex in Enumerable.Range(0, 5))
        {
            var slice = loadedChart.ExplodedSlices.Should()
                .ContainSingle(point => point.PointIndex == pointIndex).Subject;
            slice.Distance.Should().BeApproximately(0.25, 0.001);
        }
    }

    [Fact]
    public void SingleExplodedSlice_RoundTrip_KeepsOneExplosion()
    {
        var workbook = new Workbook("SingleExplodedSliceRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Share"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(35));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(25));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            Title = "Share",
            ExplodedSliceIndex = 1,
            ExplodedSliceDistance = 0.25,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        loadedChart.ExplodedSliceIndex.Should().Be(1);
        loadedChart.ExplodedSliceDistance.Should().BeApproximately(0.25, 0.001);
        loadedChart.ExplodedSlices.Should().ContainSingle();
        var onlySlice = loadedChart.ExplodedSlices[0];
        onlySlice.PointIndex.Should().Be(1);
        onlySlice.Distance.Should().BeApproximately(0.25, 0.001);
    }
}
