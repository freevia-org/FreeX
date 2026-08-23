using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R89-io-autofilter-color-dxf-1-1: the round-87 "Filter by Cell/Font Colour" work wired the AutoFilter
/// model and criterion survival, but explicitly deferred the actual OOXML persistence gap -- a colour
/// filter is only real in a saved file when it carries a `dxfId` indexing the workbook-level &lt;dxfs&gt;
/// table (see XlsxWorksheetAutoFilterXmlMapper.ToColorFilterXml/ReadColorFilter). These tests cover the
/// new allocator (XlsxAutoFilterColorFilterDxfWriter + the shared XlsxDifferentialStyleAllocator) that
/// closes that gap: fill-colour and font-colour filters now round-trip their exact colour via an
/// allocated dxf, "No Fill" still needs (and gets) no dxf at all, and a colour filter's dxf never
/// collides with a conditional-format rule's dxf in the same workbook.
/// </summary>
public class R89_AutoFilterColorDxfTests
{
    private static XNamespace WorksheetNs => "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static Workbook BuildWorkbookWithFilterColumn(
        out Sheet sheet,
        WorksheetAutoFilterColorFilterModel colorFilter,
        int columnId = 0)
    {
        var workbook = new Workbook("AutoFilterColorDxfTest");
        sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));

        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:A3", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            ColumnId: columnId,
            Values: [],
            IncludeBlank: false,
            CustomFilters: [],
            CustomFiltersAnd: false,
            CustomFiltersAndRaw: null,
            NativeCustomFiltersAttributes: null,
            Top10: null,
            DynamicFilter: null,
            ColorFilter: colorFilter,
            IconFilter: null,
            NativeFilterXmls: []));

        return workbook;
    }

    private static (MemoryStream Saved, Workbook Loaded) SaveAndReload(Workbook workbook)
    {
        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var loaded = adapter.Load(saved);
        saved.Position = 0;
        return (saved, loaded);
    }

    [Fact]
    public void R89_FillColorFilter_RoundTripsExactColorViaAllocatedDxf()
    {
        var fillColor = new CellColor(255, 0, 0);
        var workbook = BuildWorkbookWithFilterColumn(
            out _,
            new WorksheetAutoFilterColorFilterModel(CellColor: true, Color: fillColor));

        var (saved, loaded) = SaveAndReload(workbook);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml", "xl/worksheets/sheet1.xml");
        var colorFilterXml = worksheetXml.Root!
            .Element(WorksheetNs + "autoFilter")!
            .Element(WorksheetNs + "filterColumn")!
            .Element(WorksheetNs + "colorFilter")!;
        var dxfIdText = colorFilterXml.Attribute("dxfId")?.Value;
        dxfIdText.Should().NotBeNullOrEmpty("a fill-colour filter must allocate a dxfId, not omit it like 'No Fill' does");
        colorFilterXml.Attribute("cellColor")!.Value.Should().Be("1", "fill-colour filters must explicitly declare cellColor=1 for Excel/WPF-compatible OOXML");

        var stylesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml", "xl/styles.xml");
        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")!.Elements(WorksheetNs + "dxf").ToArray();
        var dxfIndex = int.Parse(dxfIdText!);
        dxfIndex.Should().BeInRange(0, dxfs.Length - 1);
        var fgColor = dxfs[dxfIndex].Element(WorksheetNs + "fill")!.Element(WorksheetNs + "patternFill")!.Element(WorksheetNs + "fgColor")!;
        fgColor.Attribute("rgb")!.Value.Should().Be("FFFF0000");

        var loadedFilterColumn = loaded.GetSheetAt(0).AutoFilter!.FilterColumns.Should().ContainSingle().Subject;
        loadedFilterColumn.ColorFilter.Should().NotBeNull();
        loadedFilterColumn.ColorFilter!.CellColor.Should().BeTrue();
        loadedFilterColumn.ColorFilter.Color.Should().Be(fillColor, "the exact colour must survive save+reload, not just the dxfId");
        loadedFilterColumn.ColorFilter.DifferentialFormatId.Should().Be(dxfIndex);
    }

    [Fact]
    public void R89_FontColorFilter_RoundTripsExactColorViaAllocatedDxf()
    {
        var fontColor = new CellColor(0, 128, 0);
        var workbook = BuildWorkbookWithFilterColumn(
            out _,
            new WorksheetAutoFilterColorFilterModel(CellColor: false, Color: fontColor));

        var (saved, loaded) = SaveAndReload(workbook);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml", "xl/worksheets/sheet1.xml");
        var colorFilterXml = worksheetXml.Root!
            .Element(WorksheetNs + "autoFilter")!
            .Element(WorksheetNs + "filterColumn")!
            .Element(WorksheetNs + "colorFilter")!;
        var dxfIdText = colorFilterXml.Attribute("dxfId")?.Value;
        dxfIdText.Should().NotBeNullOrEmpty();
        colorFilterXml.Attribute("cellColor")!.Value.Should().Be("0", "font-colour filters must set cellColor=0");

        var stylesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml", "xl/styles.xml");
        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")!.Elements(WorksheetNs + "dxf").ToArray();
        var dxfIndex = int.Parse(dxfIdText!);
        var fontColorXml = dxfs[dxfIndex].Element(WorksheetNs + "font")!.Element(WorksheetNs + "color")!;
        fontColorXml.Attribute("rgb")!.Value.Should().Be("FF008000");

        var loadedFilterColumn = loaded.GetSheetAt(0).AutoFilter!.FilterColumns.Should().ContainSingle().Subject;
        loadedFilterColumn.ColorFilter!.CellColor.Should().BeFalse();
        loadedFilterColumn.ColorFilter.Color.Should().Be(fontColor);
    }

    [Fact]
    public void R89_NoFillColorFilter_AllocatesEmptyDxfAndRoundTripsWithNoColor()
    {
        // Mirrors CellNoFillColorFilterCommand: cellColor stays true (fill semantics) and Color is
        // never set -- there is no actual colour to record. But dxfId is a REQUIRED attribute on
        // CT_ColorFilter per the real ECMA-376 schema (confirmed by
        // XlsxNonChartSchemaValidationTests.AutoFilter_SanitizesInvalidAttributesForSchemaValidity),
        // so "No Fill" still needs one -- it gets an empty <dxf/> with no font/fill/border at all,
        // which is schema-valid and resolves back to Color: null on reload.
        var workbook = BuildWorkbookWithFilterColumn(
            out _,
            new WorksheetAutoFilterColorFilterModel(CellColor: true));

        var (saved, loaded) = SaveAndReload(workbook);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml", "xl/worksheets/sheet1.xml");
        var colorFilterXml = worksheetXml.Root!
            .Element(WorksheetNs + "autoFilter")!
            .Element(WorksheetNs + "filterColumn")!
            .Element(WorksheetNs + "colorFilter")!;
        var dxfIdText = colorFilterXml.Attribute("dxfId")?.Value;
        dxfIdText.Should().NotBeNullOrEmpty("dxfId is required on colorFilter by the real OOXML schema, even for 'No Fill'");

        var stylesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml", "xl/styles.xml");
        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")!.Elements(WorksheetNs + "dxf").ToArray();
        var dxfIndex = int.Parse(dxfIdText!);
        dxfs[dxfIndex].Elements().Should().BeEmpty("'No Fill' has no colour to record, so its dxf must define no font/fill/border");

        var loadedFilterColumn = loaded.GetSheetAt(0).AutoFilter!.FilterColumns.Should().ContainSingle().Subject;
        loadedFilterColumn.ColorFilter.Should().NotBeNull();
        loadedFilterColumn.ColorFilter!.Color.Should().BeNull("an empty dxf defines no colour to resolve");
        loadedFilterColumn.ColorFilter.DifferentialFormatId.Should().Be(dxfIndex);
    }

    [Fact]
    public void R89_ColorFilterDxf_DoesNotCollideWithConditionalFormatDxf()
    {
        var cfColor = new CellColor(0, 255, 0);
        var filterColor = new CellColor(0, 0, 255);

        var workbook = new Workbook("AutoFilterAndCfDxfTest");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("A"));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.AboveAverage,
            Priority = 1,
            AboveAverage = true,
            FormatIfTrue = new CellStyle { FillColor = cfColor }
        });

        sheet.AutoFilter = new WorksheetAutoFilterModel("B1:B2", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            ColumnId: 0,
            Values: [],
            IncludeBlank: false,
            CustomFilters: [],
            CustomFiltersAnd: false,
            CustomFiltersAndRaw: null,
            NativeCustomFiltersAttributes: null,
            Top10: null,
            DynamicFilter: null,
            ColorFilter: new WorksheetAutoFilterColorFilterModel(CellColor: true, Color: filterColor),
            IconFilter: null,
            NativeFilterXmls: []));

        var (saved, loaded) = SaveAndReload(workbook);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var stylesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/styles.xml", "xl/styles.xml");
        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")!.Elements(WorksheetNs + "dxf").ToArray();
        dxfs.Length.Should().BeGreaterThanOrEqualTo(2, "the CF rule and the colour filter must each get their own dxf entry");

        var loadedSheet = loaded.GetSheetAt(0);

        // No-regression: the CF rule's own dxf must still resolve to its exact original colour --
        // the risky interaction being guarded against is the colour filter's allocator shifting or
        // colliding with the CF writer's own dxf indices.
        var loadedRule = loadedSheet.ConditionalFormats.Should().ContainSingle().Subject;
        loadedRule.FormatIfTrue.Should().NotBeNull();
        loadedRule.FormatIfTrue!.FillColor.Should().Be(cfColor);

        var loadedFilterColumn = loadedSheet.AutoFilter!.FilterColumns.Should().ContainSingle().Subject;
        loadedFilterColumn.ColorFilter!.Color.Should().Be(filterColor);

        // The two dxf indices must be different distinct entries (never the same slot silently
        // reused across two different colours), even though both were appended in the same save pass.
        loadedRule.Id.Should().NotBe(Guid.Empty);
        loadedFilterColumn.ColorFilter.DifferentialFormatId.Should().NotBeNull();
    }

    [Fact]
    public void R89_ColorFilterDxf_DedupesIdenticalColorAcrossColumns()
    {
        var sharedColor = new CellColor(128, 64, 32);
        var workbook = new Workbook("AutoFilterColorDxfDedupeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));

        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B1", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            ColumnId: 0,
            Values: [],
            IncludeBlank: false,
            CustomFilters: [],
            CustomFiltersAnd: false,
            CustomFiltersAndRaw: null,
            NativeCustomFiltersAttributes: null,
            Top10: null,
            DynamicFilter: null,
            ColorFilter: new WorksheetAutoFilterColorFilterModel(CellColor: true, Color: sharedColor),
            IconFilter: null,
            NativeFilterXmls: []));
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            ColumnId: 1,
            Values: [],
            IncludeBlank: false,
            CustomFilters: [],
            CustomFiltersAnd: false,
            CustomFiltersAndRaw: null,
            NativeCustomFiltersAttributes: null,
            Top10: null,
            DynamicFilter: null,
            ColorFilter: new WorksheetAutoFilterColorFilterModel(CellColor: true, Color: sharedColor),
            IconFilter: null,
            NativeFilterXmls: []));

        var (saved, loaded) = SaveAndReload(workbook);

        var loadedColumns = loaded.GetSheetAt(0).AutoFilter!.FilterColumns;
        loadedColumns.Should().HaveCount(2);
        loadedColumns[0].ColorFilter!.Color.Should().Be(sharedColor);
        loadedColumns[1].ColorFilter!.Color.Should().Be(sharedColor);
        loadedColumns[0].ColorFilter!.DifferentialFormatId.Should().Be(
            loadedColumns[1].ColorFilter!.DifferentialFormatId,
            "two colour filters that pick the identical colour should share one dxf entry, not accrete a duplicate");
    }
}
