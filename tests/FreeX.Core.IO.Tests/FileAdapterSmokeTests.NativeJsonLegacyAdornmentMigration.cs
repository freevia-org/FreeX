using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

// Files saved before per-series trendlines/error bars existed carry only the chart-wide members (a single
// series' adornment) and no per-series lists; loading must keep them on that one series, not every series.
public partial class FileAdapterSmokeTests
{
    private static Workbook LoadAsLegacyChartFile(ChartModel chart, Func<string, bool> dropProperty, params (string Name, int Value)[] extraProperties)
    {
        var workbook = new Workbook("LegacyChart");
        var sheet = workbook.AddSheet("Data");
        chart.DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4));
        sheet.Charts.Add(chart);

        var adapter = new NativeJsonAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var root = JsonNode.Parse(Encoding.UTF8.GetString(saved.ToArray()))!;
        RewriteLegacy(root, dropProperty, extraProperties);
        return adapter.Load(new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    private static void RewriteLegacy(JsonNode? node, Func<string, bool> dropProperty, (string Name, int Value)[] extra)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToList())
            {
                if (dropProperty(key))
                    obj.Remove(key);
                else
                    RewriteLegacy(obj[key], dropProperty, extra);
            }

            if (obj.Any(p => p.Key.Equals("showLinearTrendline", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var (name, value) in extra)
                {
                    var existing = obj.Select(p => p.Key).FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
                    obj[existing ?? name] = value;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                RewriteLegacy(item, dropProperty, extra);
        }
    }

    private static bool IsPerSeriesListKey(string key) =>
        key.Equals("seriesTrendlines", StringComparison.OrdinalIgnoreCase)
        || key.Equals("seriesErrorBars", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void NativeJsonAdapter_LegacyChartWideTrendline_MigratesToTheRecordedSeriesOnly()
    {
        var loaded = LoadAsLegacyChartFile(
            new ChartModel { Type = ChartType.Line, ShowLinearTrendline = true, TrendlineType = ChartTrendlineType.Polynomial, TrendlineOrder = 3 },
            IsPerSeriesListKey,
            ("trendlineSeriesIndex", 2));

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.SeriesTrendlines.Should().ContainSingle().Which.SeriesIndex.Should().Be(2);
        chart.SeriesTrendlines[0].Type.Should().Be(ChartTrendlineType.Polynomial);
        chart.SeriesTrendlines[0].Order.Should().Be(3);
        chart.HasTrendline(2).Should().BeTrue();
        chart.HasTrendline(0).Should().BeFalse();
        chart.HasTrendline(1).Should().BeFalse();
    }

    [Fact]
    public void NativeJsonAdapter_LegacyChartWideTrendlineWithoutIndex_MigratesToFirstSeries()
    {
        var loaded = LoadAsLegacyChartFile(
            new ChartModel { Type = ChartType.Line, ShowLinearTrendline = true },
            key => IsPerSeriesListKey(key) || key.Equals("trendlineSeriesIndex", StringComparison.OrdinalIgnoreCase));

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.SeriesTrendlines.Should().ContainSingle().Which.SeriesIndex.Should().Be(0);
        chart.HasTrendline(1).Should().BeFalse();
    }

    [Fact]
    public void NativeJsonAdapter_LegacyChartWideErrorBars_MigrateToTheRecordedSeriesOnly()
    {
        var loaded = LoadAsLegacyChartFile(
            new ChartModel { Type = ChartType.Line, ShowErrorBars = true, ErrorBarKind = ChartErrorBarKind.FixedValue, ErrorBarValue = 7 },
            key => IsPerSeriesListKey(key) || key.Equals("errorBarSeriesIndex", StringComparison.OrdinalIgnoreCase));
        var legacyIndexed = LoadAsLegacyChartFile(
            new ChartModel { Type = ChartType.Line, ShowErrorBars = true, ErrorBarSeriesIndex = 1 },
            IsPerSeriesListKey);

        var first = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        first.SeriesErrorBars.Should().ContainSingle().Which.Value.Should().Be(7);
        first.SeriesErrorBars[0].SeriesIndex.Should().Be(0);

        var chart = legacyIndexed.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.SeriesErrorBars.Should().ContainSingle().Which.SeriesIndex.Should().Be(1);
        chart.HasErrorBars(1).Should().BeTrue();
        chart.HasErrorBars(0).Should().BeFalse();
    }

    [Fact]
    public void NativeJsonAdapter_CurrentFileWithEmptyPerSeriesLists_KeepsEverySeriesSemantics()
    {
        var workbook = new Workbook("Current");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            ShowLinearTrendline = true,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
        });
        var adapter = new NativeJsonAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var chart = adapter.Load(ms).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        chart.SeriesTrendlines.Should().BeEmpty();
        chart.HasTrendline(0).Should().BeTrue();
        chart.HasTrendline(2).Should().BeTrue();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_PerSeriesTrendlinesAreNotRemigrated()
    {
        var workbook = new Workbook("PerSeries");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            ShowLinearTrendline = true,
            SeriesTrendlines = [new ChartSeriesTrendline { SeriesIndex = 1 }, new ChartSeriesTrendline { SeriesIndex = 2 }],
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
        });
        var adapter = new NativeJsonAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var chart = adapter.Load(ms).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        chart.SeriesTrendlines.Select(t => t.SeriesIndex).Should().Equal(1, 2);
    }
}
