using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R60-io-pivot-layout-6: pivot table "Repeat All Item Labels" / subtotal-placement wire-format fixes.
/// - 6-1: "Repeat All Item Labels" must be persisted as the real x14 per-field extension
///   (&lt;pivotField&gt;&lt;extLst&gt;&lt;ext uri="{2946ED86-A175-432A-8AC1-64E0C546D7DE}"&gt;
///   &lt;x14:pivotField fillDownLabels="1"/&gt;&lt;/ext&gt;&lt;/extLst&gt;&lt;/pivotField&gt;) that real Excel
///   actually reads/writes, not only FreeX's own private fx:tableProps repeatItemLabels attribute.
/// - 6-2: subtotalTop must be explicitly written "0" for Bottom placement (the OOXML schema default when
///   omitted is TRUE/Top), and an omitted subtotalTop attribute must be READ back as Top, not Bottom.
/// </summary>
public partial class FileAdapterSmokeTests
{
    private static MemoryStream BuildX14PivotSourcePackage()
    {
        var workbook = new Workbook("R60PivotX14Test");
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

    private static (Workbook Workbook, PivotTableModel Pivot) BuildX14WorkbookWithPivot()
    {
        var workbook = new Workbook("R60PivotX14WriteTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B2"
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Category"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount"));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 1));
        sheet.PivotTables.Add(pivot);
        return (workbook, pivot);
    }

    // --- R60-io-pivot-layout-6-1: real x14 fillDownLabels extension ---

    [Fact]
    public void XlsxAdapter_Save_RepeatItemLabelsTrue_EmitsRealX14FillDownLabelsExtension()
    {
        // Before the fix, "Repeat All Item Labels" was only ever persisted as FreeX's own private
        // fx:tableProps repeatItemLabels attribute -- real Excel's actual wire format (the x14 pivotField
        // extension with the ext uri "{2946ED86-A175-432A-8AC1-64E0C546D7DE}") was never emitted at all,
        // so opening the file in real Excel never showed repeated item labels.
        var (workbook, pivot) = BuildX14WorkbookWithPivot();
        pivot.RepeatItemLabels = true;

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var pivotXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!).ToString();

        pivotXml.Should().Contain("{2946ED86-A175-432A-8AC1-64E0C546D7DE}");
        pivotXml.Should().Contain("http://schemas.microsoft.com/office/spreadsheetml/2009/9/main");
        pivotXml.Should().Contain("fillDownLabels=\"1\"");
    }

    [Fact]
    public void XlsxAdapter_Save_RepeatItemLabelsFalse_EmitsNoX14FillDownLabelsExtension()
    {
        // Sibling no-regression: when the flag is off (matching the x14 schema default of not filling
        // down), no x14 extension should be written at all -- the writer must not emit fillDownLabels
        // unconditionally on every axis field regardless of the model's setting.
        var (workbook, pivot) = BuildX14WorkbookWithPivot();
        pivot.RepeatItemLabels = false;

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var pivotXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!).ToString();

        pivotXml.Should().NotContain("fillDownLabels");
    }

    private const string PivotTableDefinitionWithX14FillDownLabelsOverridingLegacyFxXml = """
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
          <location ref="D3:E5" firstHeaderRow="1" firstDataRow="2" firstDataCol="1"/>
          <pivotFields count="2">
            <pivotField axis="axisRow" showAll="0">
              <extLst>
                <ext uri="{2946ED86-A175-432A-8AC1-64E0C546D7DE}" xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">
                  <x14:pivotField fillDownLabels="1"/>
                </ext>
              </extLst>
            </pivotField>
            <pivotField dataField="1" showAll="0"/>
          </pivotFields>
          <rowFields count="1">
            <field x="0"/>
          </rowFields>
          <dataFields count="1">
            <dataField name="Sum of Amount" fld="1" subtotal="sum" numFmtId="0"/>
          </dataFields>
          <extLst>
            <ext uri="{FREEX-PIVOT-TABLE-EXT}" xmlns:fx="urn:freex:pivot:2026">
              <fx:tableProps repeatItemLabels="0"/>
            </ext>
          </extLst>
        </pivotTableDefinition>
        """;

    [Fact]
    public void XlsxAdapter_Load_RealX14FillDownLabels_TakesPrecedenceOverLegacyFxAttribute()
    {
        // Before the fix, XlsxPivotTableReader only ever consulted the private fx extension (and the
        // legacy root attribute), never the real x14 extension -- so a genuine Excel-authored file that
        // set fillDownLabels="1" via the x14 extension had the setting silently dropped on load. This
        // deliberately sets the legacy fx attribute to "0" (false) to prove the real x14 value ("1"/true)
        // wins, matching how real Excel actually resolves this.
        var source = BuildX14PivotSourcePackage();
        AddMinimalPivotTablePackage(source, pivotTableDefinitionXml: PivotTableDefinitionWithX14FillDownLabelsOverridingLegacyFxXml);

        source.Position = 0;
        var loaded = new XlsxFileAdapter().Load(source);

        var pivotTable = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        pivotTable.RepeatItemLabels.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_Load_NoX14Extension_StillFallsBackToLegacyDefaultTrue()
    {
        // Sibling no-regression: an ordinary file with neither the x14 extension nor the legacy fx
        // extension must still fall back to the schema/legacy default of true, unaffected by the new
        // x14-reading code path.
        var source = BuildX14PivotSourcePackage();
        AddMinimalPivotTablePackage(source);

        source.Position = 0;
        var loaded = new XlsxFileAdapter().Load(source);

        var pivotTable = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        pivotTable.RepeatItemLabels.Should().BeTrue();
    }

    // --- R60-io-pivot-layout-6-2: subtotalTop write/read default direction ---

    [Fact]
    public void XlsxAdapter_Save_SubtotalPlacementBottom_EmitsExplicitSubtotalTopZero()
    {
        // Before the fix, Bottom placement simply omitted the subtotalTop attribute entirely. Since the
        // OOXML schema default for an omitted subtotalTop is TRUE (Top), this made a Bottom-placed pivot
        // schema-identical to a Top-placed one -- real Excel (or any correct OOXML consumer) would show
        // subtotals at the TOP, silently reverting the user's Bottom choice.
        var (workbook, pivot) = BuildX14WorkbookWithPivot();
        pivot.ShowSubtotals = true;
        pivot.SubtotalPlacement = PivotSubtotalPlacement.Bottom;

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var pivotXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!).ToString();

        pivotXml.Should().Contain("subtotalTop=\"0\"");
    }

    [Fact]
    public void XlsxAdapter_Save_SubtotalPlacementTop_StillEmitsSubtotalTopOne()
    {
        // Sibling no-regression: Top placement must still explicitly emit subtotalTop="1", unchanged by
        // adding the Bottom branch.
        var (workbook, pivot) = BuildX14WorkbookWithPivot();
        pivot.ShowSubtotals = true;
        pivot.SubtotalPlacement = PivotSubtotalPlacement.Top;

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var pivotXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!).ToString();

        pivotXml.Should().Contain("subtotalTop=\"1\"");
    }

    private const string PivotTableDefinitionWithDefaultSubtotalButNoSubtotalTopXml = """
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
          <location ref="D3:E5" firstHeaderRow="1" firstDataRow="2" firstDataCol="1"/>
          <pivotFields count="2">
            <pivotField axis="axisRow" showAll="0" defaultSubtotal="1"/>
            <pivotField dataField="1" showAll="0"/>
          </pivotFields>
          <rowFields count="1">
            <field x="0"/>
          </rowFields>
          <dataFields count="1">
            <dataField name="Sum of Amount" fld="1" subtotal="sum" numFmtId="0"/>
          </dataFields>
        </pivotTableDefinition>
        """;

    [Fact]
    public void XlsxAdapter_Load_OmittedSubtotalTopAttribute_DefaultsToTopNotBottom()
    {
        // Before the fix, ReadBoolAttribute's implicit "false" default made an omitted subtotalTop
        // attribute read back as Bottom -- backwards from the true OOXML schema default of Top. This is
        // the overwhelmingly common case for real Excel files (Top is the default and is rarely written
        // out explicitly for every field), so this silently corrupted the vast majority of interop files.
        var source = BuildX14PivotSourcePackage();
        AddMinimalPivotTablePackage(source, pivotTableDefinitionXml: PivotTableDefinitionWithDefaultSubtotalButNoSubtotalTopXml);

        source.Position = 0;
        var loaded = new XlsxFileAdapter().Load(source);

        var pivotTable = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        pivotTable.ShowSubtotals.Should().BeTrue();
        pivotTable.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Top);
    }

    [Fact]
    public void XlsxAdapter_Load_ExplicitSubtotalTopZero_StillReadsBottom()
    {
        // Sibling no-regression: an explicit subtotalTop="0" must still read back as Bottom, unaffected by
        // changing only the default used when the attribute is absent.
        var source = BuildX14PivotSourcePackage();
        AddMinimalPivotTablePackage(
            source,
            pivotTableDefinitionXml: PivotTableDefinitionWithDefaultSubtotalButNoSubtotalTopXml.Replace(
                "<pivotField axis=\"axisRow\" showAll=\"0\" defaultSubtotal=\"1\"/>",
                "<pivotField axis=\"axisRow\" showAll=\"0\" defaultSubtotal=\"1\" subtotalTop=\"0\"/>",
                StringComparison.Ordinal));

        source.Position = 0;
        var loaded = new XlsxFileAdapter().Load(source);

        var pivotTable = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        pivotTable.ShowSubtotals.Should().BeTrue();
        pivotTable.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Bottom);
    }
}
