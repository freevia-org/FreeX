using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-11 fix bucket R5 regression tests.
///   - R11-xlsx-pivot-slicer-1: a pivot slicer's selection change must be reflected in the NATIVE
///     slicerCache &lt;data&gt;&lt;tabular&gt;&lt;items&gt;&lt;i x="N" s="1"/&gt; flags on a full save of a
///     source-preserved workbook (Excel reads the selection from these flags, never from FreeX's private
///     extLst), so re-saving after a selection change actually changes what Excel shows.
///   - R11-xlsx-charts-2: per-point and per-series c:dLblPos must be gated to the values valid for the
///     chart's CURRENT plot-group family (same gate as the chart-level dLblPos), so a chart-type change
///     (e.g. clustered column -&gt; area/stacked) that leaves stale per-point/per-series formats behind
///     never emits an invalid dLblPos that makes Excel repair/drop the chart.
/// </summary>
public sealed class FreeXR11B5Tests
{
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // ── R11-xlsx-pivot-slicer-1 ──────────────────────────────────────────────────────────────

    [Fact]
    public void PivotSlicerSelectionChange_ResaveRewritesNativeCacheItemSelectedFlags()
    {
        // Build + save a workbook with a pivot cache field carrying shared items ("East"/"West"/"North"),
        // a pivot slicer bound to that field, and a native <data><tabular><items> selection (as an
        // Excel-authored file would carry) selecting "East" (x=0, s="1"). This simulates loading a real
        // Excel workbook whose slicerCache stores selection ONLY in the native <i s="1"> form.
        var workbook = BuildPivotSlicerWorkbook();
        using var source = SaveWorkbook(workbook);
        InjectNativeTabularSelection(source, selectedIndex: 0); // "East" selected natively.

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var slicer = loaded.Slicers.Should().ContainSingle().Subject;

        // Change the selection in FreeX to "North" (index 2) — this only touches SelectedItems, exactly
        // like SetSlicerSelectionCommand.Apply does; CacheItems/the native flags are never mutated by the
        // command layer.
        slicer.SelectedItems.Clear();
        slicer.SelectedItems.Add("North");

        // Force the full-save (source package preserved) path with a trivial cell edit.
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var items = ReadNativeCacheItems(saved);
        // Before the fix: the preserved native items keep x=0 ("East") selected and every other item
        // unselected, because RewriteSlicerCacheSelection only ever touched the FreeX-private extLst.
        // After the fix: only the item resolving to "North" (x=2) carries s="1".
        items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(2,
            "Excel reads the pivot slicer's selection from the native <i s=\"1\"> flags, so a FreeX " +
            "selection change must be reflected there, not just in the private extLst");
        items.Where(item => item.Index != 2).Should().OnlyContain(item => !item.Selected);
    }

    private static Workbook BuildPivotSlicerWorkbook()
    {
        var workbook = new Workbook("PivotSlicerNativeSelectionR11B5");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B4"
        };
        cache.Fields.Add(new PivotCacheFieldModel(
            "Region",
            ContainsString: true,
            SharedItems: ["East", "West", "North"]));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 6, 1), new CellAddress(sheet.Id, 9, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            StyleName = "SlicerStyleLight2"
        };
        workbook.Slicers.Add(slicer);

        return workbook;
    }

    /// <summary>
    /// Rewrites the freshly-saved slicerCache1.xml to also carry the NATIVE
    /// &lt;data&gt;&lt;tabular&gt;&lt;items&gt;&lt;i x="N" s="1"/&gt; selection form (what a real
    /// Excel-authored file stores), selecting only <paramref name="selectedIndex"/>. The fresh FreeX writer
    /// never emits this native form itself, so this simulates "loaded a real Excel workbook".
    /// </summary>
    private static void InjectNativeTabularSelection(MemoryStream package, int selectedIndex)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/slicerCaches/slicerCache1.xml")!;
            XDocument xml;
            using (var entryStream = entry.Open())
                xml = XDocument.Load(entryStream);

            var root = xml.Root!;
            var data = new XElement(SlicerNs + "data",
                new XElement(SlicerNs + "tabular",
                    new XElement(SlicerNs + "items",
                        new XElement(SlicerNs + "i", new XAttribute("x", 0), selectedIndex == 0 ? new XAttribute("s", "1") : null),
                        new XElement(SlicerNs + "i", new XAttribute("x", 1), selectedIndex == 1 ? new XAttribute("s", "1") : null),
                        new XElement(SlicerNs + "i", new XAttribute("x", 2), selectedIndex == 2 ? new XAttribute("s", "1") : null))));
            root.Add(data);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/slicerCaches/slicerCache1.xml");
            using var writeStream = newEntry.Open();
            xml.Save(writeStream);
        }

        package.Position = 0;
    }

    private static (int Index, bool Selected)[] ReadNativeCacheItems(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/slicerCaches/slicerCache1.xml");
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var xml = XDocument.Load(entryStream);
        return xml.Descendants()
            .Where(element => element.Name.LocalName == "i")
            .Select(element => (
                Index: int.Parse(element.Attribute("x")!.Value),
                Selected: element.Attribute("s")?.Value == "1"))
            .ToArray();
    }

    private static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    // ── R11-xlsx-charts-2 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void PerPointDataLabelPosition_OnAreaChart_OmitsInvalidDLblPos()
    {
        // Area has NO valid c:dLblPos value at all (mirrors the chart-level GateDataLabelPosition gate).
        // A stale per-point format (as if the chart type had just been changed from clustered column,
        // which ChangeChartTypeCommand.Apply leaves untouched) must never emit dLblPos for an area chart.
        using var saved = SaveWorkbookWithPointDataLabel(ChartType.Area, ChartDataLabelPosition.OutsideEnd);

        var seriesDLbls = ReadFirstSeriesDLbls(saved);
        var dLbl = seriesDLbls.Element(ChartNs + "dLbl");
        dLbl.Should().NotBeNull("the per-point dLbl element itself must still be written (idx/other formatting)");
        dLbl!.Element(ChartNs + "dLblPos").Should().BeNull(
            "area charts have no valid c:dLblPos value at all; emitting one makes Excel repair/drop the chart");
    }

    [Fact]
    public void PerPointDataLabelPosition_OnStackedColumnChart_IsGatedToCenter()
    {
        // Stacked column only accepts "ctr"; a stale outEnd per-point position (left over from a prior
        // clustered-column chart type) must be remapped, not passed through verbatim.
        using var saved = SaveWorkbookWithPointDataLabel(ChartType.StackedColumn, ChartDataLabelPosition.OutsideEnd);

        var seriesDLbls = ReadFirstSeriesDLbls(saved);
        var dLblPos = seriesDLbls.Element(ChartNs + "dLbl")!.Element(ChartNs + "dLblPos");
        dLblPos.Should().NotBeNull();
        dLblPos!.Attribute("val")!.Value.Should().Be("ctr",
            "only ctr is a valid c:dLblPos value for stacked column series");
    }

    [Fact]
    public void SeriesDefaultDataLabelPosition_OnAreaChart_OmitsInvalidDLblPos()
    {
        using var saved = SaveWorkbookWithSeriesDataLabelDefault(ChartType.Area, ChartDataLabelPosition.OutsideEnd);

        var seriesDLbls = ReadFirstSeriesDLbls(saved);
        seriesDLbls.Element(ChartNs + "dLblPos").Should().BeNull(
            "area charts have no valid c:dLblPos value at all for the series-level default either");
    }

    [Fact]
    public void SeriesDefaultDataLabelPosition_OnStackedColumnChart_IsGatedToCenter()
    {
        using var saved = SaveWorkbookWithSeriesDataLabelDefault(ChartType.StackedColumn, ChartDataLabelPosition.OutsideEnd);

        var seriesDLbls = ReadFirstSeriesDLbls(saved);
        var dLblPos = seriesDLbls.Element(ChartNs + "dLblPos");
        dLblPos.Should().NotBeNull();
        dLblPos!.Attribute("val")!.Value.Should().Be("ctr",
            "only ctr is a valid c:dLblPos value for stacked column series defaults");
    }

    private static MemoryStream SaveWorkbookWithPointDataLabel(ChartType chartType, ChartDataLabelPosition position)
    {
        var (workbook, chart) = BuildChartWorkbook(chartType);
        chart.PointDataLabelFormats.Add(new ChartPointDataLabelFormat(
            SeriesIndex: 0,
            PointIndex: 0,
            Position: position,
            ShowValue: true));

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static MemoryStream SaveWorkbookWithSeriesDataLabelDefault(ChartType chartType, ChartDataLabelPosition position)
    {
        var (workbook, chart) = BuildChartWorkbook(chartType);
        chart.SeriesDataLabelFormats.Add(new ChartSeriesDataLabelFormat(
            SeriesIndex: 0,
            Position: position,
            ShowValue: true));

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static (Workbook Workbook, ChartModel Chart) BuildChartWorkbook(ChartType chartType)
    {
        var workbook = new Workbook("ChartPointSeriesDataLabelGateR11B5");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var chart = new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Title = chartType.ToString()
        };
        sheet.Charts.Add(chart);
        return (workbook, chart);
    }

    private static XElement ReadFirstSeriesDLbls(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var chartXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/charts/chart1.xml", "xl/charts/chart1.xml");
        var series = chartXml.Descendants(ChartNs + "ser").FirstOrDefault();
        series.Should().NotBeNull("the chart must write at least one c:ser element");
        var dLbls = series!.Element(ChartNs + "dLbls");
        dLbls.Should().NotBeNull("a per-point/per-series data label format must still emit the series-level c:dLbls wrapper");
        return dLbls!;
    }
}
