using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R71-io-sharedstrings-4-1/4-2: a formula cell (or dynamic-array spill member) whose cached
/// result is a string with leading/trailing whitespace must be saved with
/// <c>xml:space="preserve"</c> on the <c>&lt;v&gt;</c>/<c>&lt;t&gt;</c> element carrying it, exactly
/// like the sibling literal-cell path (XlsxFileAdapter.CreateInlineTextElement) already does —
/// otherwise a strict OOXML/Excel parser strips the padding on load. Covers all three write sites:
/// XlsxFileAdapter.SourcePackageSnapshot.RewriteFormulaCachedValue (byte-patch save), and
/// XlsxWorksheetFormulaCachedValueWriter.WriteCachedValue / WriteSpillMemberCachedValue (full save).
/// </summary>
public sealed class XlsxFormulaCachedValueXmlSpacePreservationTests
{
    private static readonly XNamespace XmlNs = XNamespace.Xml;

    // ---- XlsxWorksheetFormulaCachedValueWriter.WriteCachedValue (full-save formula-cache path) ----

    [Fact]
    public void FullSave_FormulaCachedPaddedText_WritesXmlSpacePreserveOnCachedValue()
    {
        var workbook = new Workbook("FormulaCachedPaddedText");
        var sheet = workbook.AddSheet("Sheet1");

        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(address, "A2");
        sheet.GetCell(address)!.Value = new TextValue(" Total ");

        var cell = SaveAndReadCellElement(workbook, "A1");
        var valueElement = cell.Element(cell.Name.Namespace + "v");
        valueElement.Should().NotBeNull();
        valueElement!.Value.Should().Be(" Total ");
        valueElement.Attribute(XmlNs + "space").Should().NotBeNull();
        valueElement.Attribute(XmlNs + "space")!.Value.Should().Be("preserve");
    }

    [Fact]
    public void FullSave_FormulaCachedNonPaddedText_EmitsNoXmlSpaceAttribute()
    {
        var workbook = new Workbook("FormulaCachedNonPaddedText");
        var sheet = workbook.AddSheet("Sheet1");

        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(address, "A2");
        sheet.GetCell(address)!.Value = new TextValue("Total");

        var cell = SaveAndReadCellElement(workbook, "A1");
        var valueElement = cell.Element(cell.Name.Namespace + "v");
        valueElement.Should().NotBeNull();
        valueElement!.Value.Should().Be("Total");
        valueElement.Attribute(XmlNs + "space").Should().BeNull(
            "a non-padded cached string needs no xml:space override");
    }

    // ---- XlsxWorksheetFormulaCachedValueWriter.WriteSpillMemberCachedValue (spill-member path) ----

    [Fact]
    public void FullSave_SpillMemberPaddedText_WritesXmlSpacePreserveOnInlineStringText()
    {
        var workbook = new Workbook("SpillMemberPaddedText");
        var sheet = workbook.AddSheet("Data");

        var anchor = new CellAddress(sheet.Id, 3, 1);
        sheet.SetFormula(anchor, "SomeTextArrayFormula()");
        sheet.GetCell(anchor)!.Value = new TextValue("Draft");

        var cells = new ScalarValue[1, 2]
        {
            { new TextValue("Draft"), new TextValue(" Draft") }
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells, anchor.Row, anchor.Col));

        var spillMemberCell = SaveAndReadCellElement(workbook, "B3");
        var ns = spillMemberCell.Name.Namespace;
        var inlineText = spillMemberCell.Element(ns + "is")?.Element(ns + "t");
        inlineText.Should().NotBeNull();
        inlineText!.Value.Should().Be(" Draft");
        inlineText.Attribute(XmlNs + "space").Should().NotBeNull();
        inlineText.Attribute(XmlNs + "space")!.Value.Should().Be("preserve");
    }

    [Fact]
    public void FullSave_SpillMemberNonPaddedText_EmitsNoXmlSpaceAttribute()
    {
        var workbook = new Workbook("SpillMemberNonPaddedText");
        var sheet = workbook.AddSheet("Data");

        var anchor = new CellAddress(sheet.Id, 3, 1);
        sheet.SetFormula(anchor, "SomeTextArrayFormula()");
        sheet.GetCell(anchor)!.Value = new TextValue("Draft");

        var cells = new ScalarValue[1, 2]
        {
            { new TextValue("Draft"), new TextValue("Final") }
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells, anchor.Row, anchor.Col));

        var spillMemberCell = SaveAndReadCellElement(workbook, "B3");
        var ns = spillMemberCell.Name.Namespace;
        var inlineText = spillMemberCell.Element(ns + "is")?.Element(ns + "t");
        inlineText.Should().NotBeNull();
        inlineText!.Value.Should().Be("Final");
        inlineText.Attribute(XmlNs + "space").Should().BeNull(
            "a non-padded spill member value needs no xml:space override");
    }

    private static XElement SaveAndReadCellElement(Workbook workbook, string reference)
    {
        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        using var stream = new MemoryStream(savedBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var ns = document.Root!.Name.Namespace;
        var cell = document
            .Descendants(ns + "c")
            .SingleOrDefault(element => string.Equals(element.Attribute("r")?.Value, reference, StringComparison.Ordinal));
        cell.Should().NotBeNull();
        return cell!;
    }

    // ---- XlsxFileAdapter.SourcePackageSnapshot.RewriteFormulaCachedValue (byte-patch save path) ----

    private static void PrepareLoadedWorkbookForEdit(Workbook workbook) =>
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

    private static byte[] CreatePatchSourcePackage()
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
                </Relationships>
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
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
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
                      <c r="A1" t="str"><f>A2</f><v>Old</v></c>
                      <c r="B1"><v>5</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """));

        return package.ToArray();
    }

    [Fact]
    public void PatchSave_FormulaCachedPaddedText_WritesXmlSpacePreserveOnCachedValue()
    {
        var sourceBytes = CreatePatchSourcePackage();

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText.Should().Be("A2");
        cell.Value = new TextValue(" Total ");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);

        var (text, xmlSpace) = ReadCachedValueAndXmlSpace(savedBytes, "A1");
        text.Should().Be(" Total ");
        xmlSpace.Should().Be("preserve");
    }

    [Fact]
    public void PatchSave_FormulaCachedNonPaddedText_EmitsNoXmlSpaceAttribute()
    {
        var sourceBytes = CreatePatchSourcePackage();

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.Value = new TextValue("Total");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);

        var (text, xmlSpace) = ReadCachedValueAndXmlSpace(savedBytes, "A1");
        text.Should().Be("Total");
        xmlSpace.Should().BeNull("a non-padded cached string needs no xml:space override");
    }

    private static (string? Text, string? XmlSpace) ReadCachedValueAndXmlSpace(byte[] packageBytes, string reference)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var ns = document.Root!.Name.Namespace;
        var cell = document
            .Descendants(ns + "c")
            .SingleOrDefault(element => string.Equals(element.Attribute("r")?.Value, reference, StringComparison.Ordinal));
        cell.Should().NotBeNull();
        var valueElement = cell!.Element(ns + "v");
        valueElement.Should().NotBeNull();
        return (valueElement!.Value, valueElement.Attribute(XmlNs + "space")?.Value);
    }
}
