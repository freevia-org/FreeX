using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R86-io-shared-array-formula-5-1: ClosedXML's IXLCell.FormulaA1 for a
/// What-If Data Table cell (&lt;f t="dataTable" .../&gt;) comes back as a syntactically malformed
/// string -- e.g. "{TABLE(C1,B1}" (unbalanced brace, missing the closing ')') -- because
/// ClosedXML's own internal placeholder-text template has a bug. XlsxClosedXmlCellMapper
/// .NormalizeFormulaText must repair that shape into a well-formed "TABLE(...)" string instead of
/// ever surfacing (and, on a later full save, re-emitting) the broken text.
/// </summary>
public sealed class R86_DataTableFormulaLoadTests
{
    private static byte[] CreateDataTableSourcePackage()
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
                  <dimension ref="A1:C3"/>
                  <sheetData>
                    <row r="1"><c r="B1"><v>10</v></c><c r="C1"><v>20</v></c></row>
                    <row r="2"><c r="A2"><v>1</v></c></row>
                    <row r="3"><c r="B3"><f t="dataTable" ref="B3:C3" dt2D="1" dtr="0" r1="B1" r2="C1">A2</f><v>2</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        return package.ToArray();
    }

    [Fact]
    public void Load_WhatIfDataTableCell_ProducesWellFormedFormulaText()
    {
        var sourceBytes = CreateDataTableSourcePackage();
        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream(sourceBytes, writable: false);
        var workbook = adapter.Load(source);

        var cell = workbook.GetSheetAt(0).GetCell(3, 2)!; // B3

        cell.HasFormula.Should().BeTrue();
        // Pre-fix this was ClosedXML's malformed placeholder text "{TABLE(C1,B1}" -- unbalanced
        // brace, no leading '=', no closing ')'. The fix must repair it to a well-formed string.
        cell.FormulaText.Should().Be("TABLE(C1,B1)");
        cell.FormulaText.Should().NotContain("{");
        cell.FormulaText.Should().NotContain("}");
    }

    [Fact]
    public void Save_AfterUnrelatedEdit_DoesNotWriteMalformedDataTableFormula()
    {
        var sourceBytes = CreateDataTableSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        // Force the full (ClosedXML-regenerate) save path via an unrelated edit elsewhere.
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(99)); // A1

        using var savedStream = new MemoryStream();
        adapter.Save(workbook, savedStream);
        var savedBytes = savedStream.ToArray();

        using var archive = new ZipArchive(new MemoryStream(savedBytes, writable: false), ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var ns = document.Root!.Name.Namespace;
        var b3 = document.Descendants(ns + "c")
            .Single(element => string.Equals(element.Attribute("r")?.Value, "B3", StringComparison.Ordinal));
        var formulaText = b3.Element(ns + "f")?.Value;

        formulaText.Should().NotBeNull();
        // Pre-fix this was "{TABLE(C1,B1}" -- unbalanced braces that make Excel flag the file for
        // repair. The saved formula must be syntactically valid XML/formula text.
        formulaText.Should().NotContain("{");
        formulaText.Should().NotContain("}");
    }
}
