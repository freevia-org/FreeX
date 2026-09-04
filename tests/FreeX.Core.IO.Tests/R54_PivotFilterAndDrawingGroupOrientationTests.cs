using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R54-io-pivot-filter-3-1: value/label/top-N pivot filter edits (add/change/clear) made to a loaded
/// <see cref="PivotTableModel"/> were silently dropped on save whenever the workbook was loaded from an
/// existing .xlsx -- <c>XlsxPivotTableWriter.Save</c> (the only code that emitted filter XML) is gated
/// behind <c>!hasSourcePackage</c>, and nothing on the preserved-part path ever wrote
/// <c>pivot.ValueFilters</c>/<c>LabelFilters</c> back. Fixed by
/// <c>XlsxFileAdapter.RewritePreservedPivotValueAndLabelFilters</c> (SavePostProcessing.cs), which patches
/// the preserved part in place but ONLY when the model actually differs from what is currently encoded
/// there, so an untouched file (including one with a genuine native, Excel-authored &lt;filters&gt;
/// element) stays byte-stable.
///
/// R54-io-pivot-filter-3-3: a manual per-item field filter (unchecked values in a field's filter
/// dropdown, <see cref="PivotFieldModel.SelectedItems"/>) was dropped entirely on a brand-new pivot
/// table's FIRST save (never loaded from disk) -- <c>XlsxPivotTableWriter.ToPivotFieldsXml</c> always
/// emitted a single placeholder &lt;items count="1"&gt;&lt;item t="default"/&gt;&lt;/items&gt;, never
/// reading <c>SelectedItems</c> or enumerating the pivot cache's shared items. Fixed by
/// <c>XlsxPivotTableWriter.ToPivotFieldItemsXml</c>.
///
/// R54-io-drawing-group-transform-4-1: a grouped shape/picture/connector's own rendered orientation
/// ignored the ancestor &lt;xdr:grpSp&gt;'s own rotation/flipH/flipV -- only its POSITION was composed
/// (via <c>ComputeGroupTransform</c>), never its FACING direction, so a shape inside a rotated or
/// flipped group round-tripped with the wrong (uncomposed) <c>RotationDegrees</c>/
/// <c>FlipHorizontal</c>/<c>FlipVertical</c>. Fixed by composing a separate, scale-free
/// rotation/flip-only 2x2 matrix across the ancestor chain (<c>ComputeGroupTransform</c>'s new
/// <c>OrientationA..D</c>) and folding the shape's own local rotation/flip into it via
/// <c>ComposeShapeOrientationWithGroups</c>.
/// </summary>
public sealed class R54_PivotFilterAndDrawingGroupOrientationTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string SpreadsheetDrawingNsUri = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const string DrawingNsUri = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string RelNsUri = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // ── R54-io-pivot-filter-3-1 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SaveThenReload_ValueFilterAddedToLoadedWorkbook_SurvivesSourcePreservedSave()
    {
        // Simulates: open an existing .xlsx (no pivot filter yet), apply a "Top 5" value filter via
        // ConfigurePivotTableFieldFiltersCommand, then Save the SAME file. Before the fix, nothing on
        // this preserved-part path ever wrote pivot.ValueFilters back -- the filter silently vanished.
        using var source = XlsxPackageTestHelper.SaveWorkbook(CreateRegionPivotWorkbook());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        pivot.ValueFilters.Should().BeEmpty();

        pivot.ValueFilters.Add(new PivotValueFilterModel(DataFieldIndex: 0, PivotValueFilterKind.Top, Count: 5));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        // R83-order-guard-invented-sweep-1: Top has a real ST_PivotFilterType token, so this now
        // round-trips through the native <filters> element (mirroring the fresh-workbook path fixed in
        // r82) instead of the invented <valueFilters> shape. CT_PivotFilter's "fld" attribute is
        // required, so an unspecified SourceFieldIndex normalizes to 0 on reload, exactly as it already
        // does on the fresh-save path (see ToPivotFiltersXml / R82_PivotNativeFilterAndSortRoundTripTests).
        reloaded.GetSheetAt(0).PivotTables.Single().ValueFilters.Should().ContainSingle()
            .Which.Should().Be(new PivotValueFilterModel(0, PivotValueFilterKind.Top, 5, SourceFieldIndex: 0),
                "the Top-5 value filter applied after Load() must survive a save of the same file");
    }

    [Fact]
    public void SaveThenReload_UnrelatedCellEdit_PreservesExistingNativeValueFilterByteIdentical()
    {
        // Sibling/no-regression: an Excel-authored native <filters> element the user never touched must
        // NOT be converted into FreeX's own (non-native) <valueFilters> shape just because SOME
        // unrelated part of the workbook was edited and saved -- only an actual filter edit should ever
        // trigger a rewrite.
        using var source = XlsxPackageTestHelper.SaveWorkbook(CreateRegionPivotWorkbook());
        InjectNativeValueFilter(source);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        pivot.ValueFilters.Should().ContainSingle(
            "the injected native <filters><filter type=\"valueGreaterThan\".../></filters> must load correctly");

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var pivotXml = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotTables/pivotTable1.xml");
        pivotXml.Root!.Element(WorkbookNs + "filters").Should().NotBeNull(
            "an untouched native value filter must survive a resave that never mutated the pivot model");
        pivotXml.Root!.Element(WorkbookNs + "valueFilters").Should().BeNull(
            "an unrelated edit must not convert a valid native filter into FreeX's own invented shape");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).PivotTables.Single().ValueFilters.Should().ContainSingle();
    }

    private static void InjectNativeValueFilter(MemoryStream package)
    {
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            document.Root!.Add(new XElement(
                WorkbookNs + "filters",
                new XAttribute("count", "1"),
                new XElement(
                    WorkbookNs + "filter",
                    new XAttribute("fld", "0"),
                    new XAttribute("iMeasureFld", "0"),
                    new XAttribute("type", "valueGreaterThan"),
                    new XAttribute("stringValue1", "15"))));
        });
    }

    // ── R54-io-pivot-filter-3-3 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_FreshPivotTableWithManualItemFilter_WritesNativePerItemHiddenFlags()
    {
        // Brand-new pivot table (never loaded from disk), user unchecks "West" in the Region field's
        // filter dropdown, then saves for the first time. Before the fix, ToPivotFieldsXml always
        // emitted a single placeholder <items count="1"><item t="default"/></items>, never reading
        // SelectedItems at all -- the manual filter was completely absent from the saved file.
        var workbook = CreateRegionPivotWorkbook();
        var pivot = workbook.GetSheetAt(0).PivotTables.Single();
        pivot.RowFields[0] = pivot.RowFields[0] with { SelectedItems = ["East"] };

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var itemsXml = ReadPivotFieldItems(saved, fieldIndex: 0);
        itemsXml.Should().HaveCount(3, "one <item> per shared item (East, West) plus the trailing default marker");
        itemsXml[0].Attribute("x")!.Value.Should().Be("0");
        itemsXml[0].Attribute("hidden").Should().BeNull("East is selected/visible");
        itemsXml[1].Attribute("x")!.Value.Should().Be("1");
        itemsXml[1].Attribute("hidden")!.Value.Should().Be("1", "West was unchecked, so it must be hidden");
        itemsXml[2].Attribute("t")!.Value.Should().Be("default");

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.GetSheetAt(0).PivotTables.Single().RowFields.Single().SelectedItems.Should().Equal("East");
    }

    [Fact]
    public void Save_FreshPivotTableFieldWithNoExplicitItemFilter_StillEmitsPlaceholderItemsElement()
    {
        // Sibling/no-regression: a field the user never touched (SelectedItems == null) must keep
        // emitting the pre-existing schema-minimum placeholder, not a per-item list with everything
        // marked visible.
        using var saved = XlsxPackageTestHelper.SaveWorkbook(CreateRegionPivotWorkbook());

        var itemsXml = ReadPivotFieldItems(saved, fieldIndex: 0);
        itemsXml.Should().ContainSingle();
        itemsXml[0].Attribute("t")!.Value.Should().Be("default");
        itemsXml[0].Attribute("x").Should().BeNull();
    }

    private static List<XElement> ReadPivotFieldItems(MemoryStream package, int fieldIndex)
    {
        var pivotFields = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotTables/pivotTable1.xml")
            .Root!.Element(WorkbookNs + "pivotFields")!.Elements(WorkbookNs + "pivotField").ToList();
        return pivotFields[fieldIndex].Element(WorkbookNs + "items")!.Elements(WorkbookNs + "item").ToList();
    }

    private static Workbook CreateRegionPivotWorkbook()
    {
        var workbook = new Workbook("R54PivotFilterWorkbook");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
        };
        cache.Fields.Add(new PivotCacheFieldModel(
            "Region",
            SharedItemCount: 2,
            ContainsString: true,
            SharedItems: ["East", "West"],
            SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    // ── R54-io-drawing-group-transform-4-1 ─────────────────────────────────────────────────────

    [Fact]
    public void ReadShapeParts_ShapeInsideRotatedGroup_ComposesGroupRotationIntoOwnFacingDirection()
    {
        // Group xfrm: rot=5400000 (90 degrees), off=(0,0) ext=(1000000,1000000) chOff=(0,0)
        // chExt=(1000000,1000000) -- a pure rotation, no scale, no flip.
        // Child sp: prstGeom rightArrow, own local xfrm has NO rot attribute (own rotation 0),
        // off=(700000,0) ext=(300000,300000).
        // Real Excel renders the arrow rotated 90 degrees total (own 0 + group 90) -- pointing down
        // instead of right. Before the fix, ReadSpElement read ONLY the shape's own <a:xfrm> rot (0),
        // never composing the ancestor group's rotation, so RotationDegrees came back 0.
        var drawingXml = $"""
            <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNsUri}" xmlns:a="{DrawingNsUri}" xmlns:r="{RelNsUri}">
              <xdr:twoCellAnchor>
                <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:to><xdr:col>10</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>20</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                <xdr:grpSp>
                  <xdr:nvGrpSpPr>
                    <xdr:cNvPr id="10" name="Rotated Group" />
                    <xdr:cNvGrpSpPr />
                  </xdr:nvGrpSpPr>
                  <xdr:grpSpPr>
                    <a:xfrm rot="5400000">
                      <a:off x="0" y="0" />
                      <a:ext cx="1000000" cy="1000000" />
                      <a:chOff x="0" y="0" />
                      <a:chExt cx="1000000" cy="1000000" />
                    </a:xfrm>
                  </xdr:grpSpPr>
                  <xdr:sp>
                    <xdr:nvSpPr>
                      <xdr:cNvPr id="11" name="Arrow" />
                      <xdr:cNvSpPr />
                    </xdr:nvSpPr>
                    <xdr:spPr>
                      <a:xfrm><a:off x="700000" y="0" /><a:ext cx="300000" cy="300000" /></a:xfrm>
                      <a:prstGeom prst="rightArrow"><a:avLst /></a:prstGeom>
                    </xdr:spPr>
                  </xdr:sp>
                </xdr:grpSp>
                <xdr:clientData />
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """;

        var (_, shapes) = XlsxWorksheetDrawingPartReader.ReadShapeParts(XDocument.Parse(drawingXml));

        var shape = shapes.Should().ContainSingle().Subject;
        shape.RotationDegrees.Should().BeApproximately(90, 0.01,
            "the shape's own local rotation (0) must compose with the enclosing group's own 90-degree " +
            "rotation to yield the shape's TRUE rendered facing direction, matching real Excel");
        shape.FlipHorizontal.Should().BeFalse();
        shape.FlipVertical.Should().BeFalse();
    }

    [Fact]
    public void ReadShapeParts_ShapeWithOwnRotationInsideUnrotatedUnflippedGroup_KeepsItsOwnRotationUnaffected()
    {
        // Sibling/no-regression: a group with NO rotation/flip (the overwhelming common case) must
        // leave a child shape's own local rotation exactly as read from its own <a:xfrm> -- the new
        // orientation composition must reduce to a no-op when the ancestor chain's orientation is
        // identity.
        var drawingXml = $"""
            <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNsUri}" xmlns:a="{DrawingNsUri}" xmlns:r="{RelNsUri}">
              <xdr:twoCellAnchor>
                <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:to><xdr:col>10</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>20</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                <xdr:grpSp>
                  <xdr:nvGrpSpPr>
                    <xdr:cNvPr id="10" name="Plain Group" />
                    <xdr:cNvGrpSpPr />
                  </xdr:nvGrpSpPr>
                  <xdr:grpSpPr>
                    <a:xfrm>
                      <a:off x="0" y="0" />
                      <a:ext cx="1000000" cy="1000000" />
                      <a:chOff x="0" y="0" />
                      <a:chExt cx="1000000" cy="1000000" />
                    </a:xfrm>
                  </xdr:grpSpPr>
                  <xdr:sp>
                    <xdr:nvSpPr>
                      <xdr:cNvPr id="11" name="Rotated Shape" />
                      <xdr:cNvSpPr />
                    </xdr:nvSpPr>
                    <xdr:spPr>
                      <a:xfrm rot="2700000"><a:off x="0" y="0" /><a:ext cx="300000" cy="300000" /></a:xfrm>
                      <a:prstGeom prst="rightArrow"><a:avLst /></a:prstGeom>
                    </xdr:spPr>
                  </xdr:sp>
                </xdr:grpSp>
                <xdr:clientData />
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """;

        var (_, shapes) = XlsxWorksheetDrawingPartReader.ReadShapeParts(XDocument.Parse(drawingXml));

        var shape = shapes.Should().ContainSingle().Subject;
        shape.RotationDegrees.Should().BeApproximately(45, 0.01,
            "with no ancestor group rotation/flip, the shape's own local 45-degree rotation must pass through unchanged");
        shape.FlipHorizontal.Should().BeFalse();
        shape.FlipVertical.Should().BeFalse();
    }
}
