using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R82-io-cell-rich-metadata-5-2: the fast byte-patch save path (RewriteFormulaCachedValue) used to
/// unconditionally strip BOTH vm and cm off a formula cell whenever its cached value changed, even
/// when the formula text (and therefore its dynamic-array-ness) never changed. That is correct for
/// vm (always a value-dependent rich-value pointer) and for cm when it accompanies vm (part of the
/// same rich-value binding), but wrong for a lone cm backing an XLDAPR dynamic-array marker: that
/// marker describes the FORMULA's nature (it spills), not the specific cached value being replaced,
/// so an ordinary recalculation of a spilling formula (e.g. =SORT(...) re-sorting after an input
/// changes elsewhere) must not silently drop the cell's dynamic-array compatibility marker.
///
/// Mirrors XlsxFormulaCachedValueRichValueMetadataPatchSaveTests's structure/technique (R43-io-
/// richvalue-linkeddata-3-1), but for the cm-alone dynamic-array case instead of the vm rich-value
/// case.
/// </summary>
public sealed class R82_DynamicArrayCellMetadataPatchSaveTests
{
    private static void PrepareLoadedWorkbookForEdit(Workbook workbook) =>
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

    // A1 is a dynamic-array spill formula carrying cm="1" (no vm) -- a real XLDAPR marker, backed by
    // a genuine xl/metadata.xml <cellMetadata> block naming the metadataType "XLDAPR" -- alongside a
    // plain literal sibling cell B1 that carries no metadata at all.
    private static byte[] CreateSourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/metadata.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheetMetadata+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                  <calcPr calcId="191029"/>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sheetMetadata" Target="metadata.xml"/>
                </Relationships>
                """),
            (
                // A minimal but structurally valid dynamic-array metadata part: <cellMetadata count="1">
                // backs cm="1" on A1 below (cm indexes are 1-based into this list), naming the
                // metadataType "XLDAPR" the same way real Excel marks a dynamic-array spill formula.
                "xl/metadata.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <metadata xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:xda="http://schemas.microsoft.com/office/spreadsheetml/2017/dynamicarray">
                  <metadataTypes count="1">
                    <metadataType name="XLDAPR" minSupportedVersion="120000" copy="1" pasteAll="1" pasteValues="1" merge="1" splitFirst="1" rowColShift="1" clearFormats="1" clearComments="1" assign="1" coerce="1" cellMeta="1"/>
                  </metadataTypes>
                  <futureMetadata name="XLDAPR" count="1">
                    <bk>
                      <extLst>
                        <ext uri="{bdbb8cdc-fa1e-496e-a857-3c3f30c029c3}">
                          <xda:dynamicArrayProperties fDynamic="1" fCollapsed="0"/>
                        </ext>
                      </extLst>
                    </bk>
                  </futureMetadata>
                  <cellMetadata count="1">
                    <bk><rc t="1" v="0"/></bk>
                  </cellMetadata>
                </metadata>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="TableStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <dimension ref="A1:B1"/>
                  <sheetData>
                    <row r="1">
                      <c r="A1" cm="1"><f>SORT(B1:B1)</f><v>7</v></c>
                      <c r="B1"><v>5</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """));

        return package.ToArray();
    }

    [Fact]
    public void Save_PatchesFormulaCachedValueOnDynamicArrayCell_PreservesXldaprCmMarker()
    {
        var sourceBytes = CreateSourcePackage();
        ReadCellAttribute(sourceBytes, "A1", "cm").Should().Be("1");

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText.Should().Be("SORT(B1:B1)");
        // The spill formula's cached value changes (7 -> 12, e.g. because an input elsewhere forced a
        // recalculation) while the formula text -- and therefore its dynamic-array-ness -- is
        // unchanged. This is exactly the scenario RewriteFormulaCachedValue byte-patches in place.
        cell.Value = new NumberValue(12);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        // Must still be the cheap fast patch-save path -- the bug (and this fix) only apply there.
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);

        ReadCellText(savedBytes, "A1").Should().Be("12");
        ReadCellAttribute(savedBytes, "A1", "cm").Should().Be(
            "1",
            "the formula text (and its dynamic-array nature) never changed -- only the cached value did " +
            "-- so the XLDAPR cm marker is still valid and must survive an ordinary recalculation " +
            "(R82-io-cell-rich-metadata-5-2)");
        ReadCellAttribute(savedBytes, "A1", "vm").Should().BeNull("this cell never had a vm rich-value pointer");
    }

    [Fact]
    public void Save_PatchesFormulaCachedValueOnPairedRichValueCell_StillDropsBothVmAndCm_NoRegression()
    {
        // Sibling no-regression case: when cm genuinely accompanies vm as part of the SAME rich-value
        // binding (not a lone XLDAPR marker), both must still be dropped once the cached value they
        // describe changes -- the fix must be scoped to a lone cm, not a blanket "never touch cm".
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cell = new XElement(
            worksheetNs + "c",
            new XAttribute("r", "A1"),
            new XAttribute("t", "e"),
            new XAttribute("vm", "7"),
            new XAttribute("cm", "8"),
            new XElement(worksheetNs + "f", "STOCKHISTORY(\"MSFT\",TODAY())"),
            new XElement(worksheetNs + "v", "#VALUE!"));

        InvokeRewriteFormulaCachedCellValue(cell, worksheetNs, new NumberValue(42));

        cell.Attribute("vm").Should().BeNull("the vm rich-value pointer described the OLD cached value");
        cell.Attribute("cm").Should().BeNull(
            "cm accompanied vm as part of the SAME rich-value binding, so it is value-dependent too " +
            "and must be dropped alongside vm");
    }

    private static void InvokeRewriteFormulaCachedCellValue(XElement cell, XNamespace worksheetNs, ScalarValue value)
    {
        var method = FindPrivateStaticMethod("RewriteFormulaCachedCellValue");
        method.Invoke(null, [cell, worksheetNs, value]);
    }

    private static System.Reflection.MethodInfo FindPrivateStaticMethod(string name)
    {
        var stack = new Stack<Type>();
        stack.Push(typeof(XlsxFileAdapter));
        var seen = new HashSet<Type>();
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            if (!seen.Add(type))
                continue;
            var method = type.GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (method is not null)
                return method;
            foreach (var nested in type.GetNestedTypes(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                stack.Push(nested);
        }

        throw new MissingMethodException($"Private static method '{name}' not found on XlsxFileAdapter or its nested types.");
    }

    private static string? ReadCellText(byte[] packageBytes, string reference)
    {
        var cell = ReadCellElement(packageBytes, reference);
        var ns = cell.Name.Namespace;
        return cell.Element(ns + "v")?.Value;
    }

    private static string? ReadCellAttribute(byte[] packageBytes, string reference, string attributeName) =>
        ReadCellElement(packageBytes, reference).Attribute(attributeName)?.Value;

    private static XElement ReadCellElement(byte[] packageBytes, string reference)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var ns = document.Root!.Name.Namespace;
        var cell = document
            .Descendants(ns + "c")
            .SingleOrDefault(element => string.Equals(element.Attribute("r")?.Value, reference, StringComparison.Ordinal));
        cell.Should().NotBeNull();
        return cell!;
    }
}
