using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R52-io-pivot-layout-3: pivot table row/column-field layout round-trip fixes.
/// - 3-1: the OOXML x="-2" "Σ Values" axis placeholder must not be modeled as a real row/column field.
/// - 3-2: the table-wide subtotal on/off + top/bottom setting must be read from an actual row/column
///   axis field, not from whichever <pivotField> happens to be first in cache/document order.
/// - 3-4: per-pivotField compact/outline attributes must be emitted to match the table's chosen
///   report layout (compact/outline/tabular), not just the pivotTableDefinition-level attributes.
/// </summary>
public partial class FileAdapterSmokeTests
{
    private const string PivotTableDefinitionWithValuesAxisPlaceholderXml = """
        <pivotTableDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                              name="PivotTable1"
                              cacheId="1"
                              dataOnRows="1"
                              applyNumberFormats="0"
                              applyBorderFormats="0"
                              applyFontFormats="0"
                              applyPatternFormats="0"
                              applyAlignmentFormats="0"
                              applyWidthHeightFormats="1">
          <location ref="D3:E6" firstHeaderRow="1" firstDataRow="2" firstDataCol="1"/>
          <pivotFields count="2">
            <pivotField axis="axisRow" showAll="0"/>
            <pivotField dataField="1" showAll="0"/>
          </pivotFields>
          <rowFields count="2">
            <field x="0"/>
            <field x="-2"/>
          </rowFields>
          <dataFields count="2">
            <dataField name="Sum of Amount" fld="1" subtotal="sum" numFmtId="0"/>
            <dataField name="Count of Amount" fld="1" subtotal="count" numFmtId="0"/>
          </dataFields>
        </pivotTableDefinition>
        """;

    private static MemoryStream BuildPivotSourcePackage()
    {
        var workbook = new Workbook("PivotLayoutFixesTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;
        return source;
    }

    [Fact]
    public void XlsxAdapter_Load_SkipsValuesAxisPlaceholderFieldInRowFields()
    {
        // R52-io-pivot-layout-3-1: a real-Excel-authored pivot with 2+ data fields always carries an
        // explicit <field x="-2"/> entry in rowFields/colFields marking where the "Σ Values" pseudo-row
        // sits. Reading it as a normal field would corrupt RowFields with a bogus SourceFieldIndex=-2
        // entry that downstream per-cache-field-indexed consumers cannot handle.
        var source = BuildPivotSourcePackage();
        AddMinimalPivotTablePackage(source, pivotTableDefinitionXml: PivotTableDefinitionWithValuesAxisPlaceholderXml);

        source.Position = 0;
        var loaded = new XlsxFileAdapter().Load(source);

        var pivotTable = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        pivotTable.RowFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(0);
        pivotTable.RowFields.Should().NotContain(field => field.SourceFieldIndex == -2);
        pivotTable.DataFields.Should().HaveCount(2);
    }

    [Fact]
    public void XlsxAdapter_Load_OrdinaryRowFieldWithoutValuesPlaceholder_StillRoundTrips()
    {
        // Sibling no-regression: a pivot with no x="-2" placeholder (the ordinary, far more common case
        // of a single data field) must be unaffected by the -2 skip logic.
        var source = BuildPivotSourcePackage();
        AddMinimalPivotTablePackage(source);

        source.Position = 0;
        var loaded = new XlsxFileAdapter().Load(source);

        var pivotTable = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        pivotTable.RowFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(0);
        pivotTable.DataFields.Should().ContainSingle().Which.Name.Should().Be("Sum of Amount");
    }

    private const string PivotCacheDefinitionWithFilterAndRowFieldXml = """
        <pivotCacheDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                              refreshedBy="FreeX Test"
                              refreshOnLoad="0"
                              recordCount="2">
          <cacheSource type="worksheet">
            <worksheetSource ref="A1:C3" sheet="Data"/>
          </cacheSource>
          <cacheFields count="3">
            <cacheField name="Region" numFmtId="0">
              <sharedItems count="1">
                <s v="East"/>
              </sharedItems>
            </cacheField>
            <cacheField name="Product" numFmtId="0">
              <sharedItems count="2">
                <s v="Widget"/>
                <s v="Gadget"/>
              </sharedItems>
            </cacheField>
            <cacheField name="Amount" numFmtId="0">
              <sharedItems containsNumber="1" count="2">
                <n v="10"/>
                <n v="20"/>
              </sharedItems>
            </cacheField>
          </cacheFields>
        </pivotCacheDefinition>
        """;

    // Region (cache field 0) is a page/report filter -- subtotals do not apply to it, so its pivotField
    // carries no defaultSubtotal/subtotalTop attributes at all. Product (cache field 1) is the real row
    // field and explicitly carries defaultSubtotal="1" subtotalTop="1" (shown, at the top).
    private const string PivotTableDefinitionWithFilterFieldFirstXml = """
        <pivotTableDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                              name="PivotTable1"
                              cacheId="1"
                              dataOnRows="0"
                              applyNumberFormats="0"
                              applyBorderFormats="0"
                              applyFontFormats="0"
                              applyPatternFormats="0"
                              applyAlignmentFormats="0"
                              applyWidthHeightFormats="1">
          <location ref="D3:E6" firstHeaderRow="1" firstDataRow="2" firstDataCol="1"/>
          <pivotFields count="3">
            <pivotField axis="axisPage" showAll="0"/>
            <pivotField axis="axisRow" showAll="0" defaultSubtotal="1" subtotalTop="1"/>
            <pivotField dataField="1" showAll="0"/>
          </pivotFields>
          <rowFields count="1">
            <field x="1"/>
          </rowFields>
          <pageFields count="1">
            <pageField fld="0"/>
          </pageFields>
          <dataFields count="1">
            <dataField name="Sum of Amount" fld="2" subtotal="sum" numFmtId="0"/>
          </dataFields>
        </pivotTableDefinition>
        """;

    [Fact]
    public void XlsxAdapter_Load_ReadsSubtotalSettingFromRealRowFieldNotLeadingFilterField()
    {
        // R52-io-pivot-layout-3-2: the leading <pivotField> in cache/document order (Region, a page
        // filter) must not be the source of the table-wide subtotal flag; the real row field (Product)
        // carries the actual defaultSubtotal="1" subtotalTop="1" setting and must win.
        var source = BuildPivotSourcePackage();
        AddMinimalPivotTablePackage(
            source,
            pivotCacheDefinitionXml: PivotCacheDefinitionWithFilterAndRowFieldXml,
            pivotTableDefinitionXml: PivotTableDefinitionWithFilterFieldFirstXml);

        source.Position = 0;
        var loaded = new XlsxFileAdapter().Load(source);

        var pivotTable = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        pivotTable.ShowSubtotals.Should().BeTrue();
        pivotTable.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Top);
    }

    [Fact]
    public void XlsxAdapter_Load_SingleRowFieldSubtotal_StillRoundTrips()
    {
        // Sibling no-regression: the ordinary single-row-field case (no leading unrelated filter field)
        // must still read its own defaultSubtotal/subtotalTop correctly.
        var source = BuildPivotSourcePackage();
        AddMinimalPivotTablePackage(
            source,
            pivotTableDefinitionXml: MinimalPivotTableDefinitionXml.Replace(
                "<pivotField axis=\"axisRow\" showAll=\"0\"/>",
                "<pivotField axis=\"axisRow\" showAll=\"0\" defaultSubtotal=\"1\" subtotalTop=\"1\"/>",
                StringComparison.Ordinal));

        source.Position = 0;
        var loaded = new XlsxFileAdapter().Load(source);

        var pivotTable = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        pivotTable.ShowSubtotals.Should().BeTrue();
        pivotTable.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Top);
    }

    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) BuildWorkbookWithTwoRowFieldPivot()
    {
        var workbook = new Workbook("PivotReportLayoutFieldAttributeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:C2"
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Region"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Product"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount"));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 9, 3))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum", 2));
        sheet.PivotTables.Add(pivot);

        return (workbook, sheet, pivot);
    }

    [Fact]
    public void XlsxAdapter_Save_OutlineReportLayout_EmitsCompactOutlineOnEachAxisPivotField()
    {
        // R52-io-pivot-layout-3-4: real Excel renders each field's own header form from that field's own
        // compact/outline attributes on <pivotField> -- the table-level attributes on
        // <pivotTableDefinition> only seed defaults for newly-added fields. Choosing Outline layout must
        // therefore stamp compact="0" outline="1" onto every existing row/column pivotField, not leave
        // them at the schema default (Compact).
        var (workbook, _, pivot) = BuildWorkbookWithTwoRowFieldPivot();
        pivot.ReportLayout = PivotReportLayout.Outline;

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var pivotXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!);

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var pivotFields = pivotXml.Root!.Element(ns + "pivotFields")!.Elements(ns + "pivotField").ToList();
        pivotFields.Should().HaveCount(3);

        // Fields 0 and 1 (Region, Product) are the row axis fields.
        pivotFields[0].Attribute("compact")!.Value.Should().Be("0");
        pivotFields[0].Attribute("outline")!.Value.Should().Be("1");
        pivotFields[1].Attribute("compact")!.Value.Should().Be("0");
        pivotFields[1].Attribute("outline")!.Value.Should().Be("1");
    }

    [Fact]
    public void XlsxAdapter_Save_CompactReportLayout_EmitsCompactOutlineOnEachAxisPivotField()
    {
        // Sibling no-regression: Compact layout (the schema default) must still be stamped explicitly
        // per-field as compact="1" outline="1" rather than silently regressing to omitted attributes.
        var (workbook, _, pivot) = BuildWorkbookWithTwoRowFieldPivot();
        pivot.ReportLayout = PivotReportLayout.Compact;

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var pivotXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!);

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var pivotFields = pivotXml.Root!.Element(ns + "pivotFields")!.Elements(ns + "pivotField").ToList();
        pivotFields[0].Attribute("compact")!.Value.Should().Be("1");
        pivotFields[0].Attribute("outline")!.Value.Should().Be("1");
        pivotFields[1].Attribute("compact")!.Value.Should().Be("1");
        pivotFields[1].Attribute("outline")!.Value.Should().Be("1");
    }
}
