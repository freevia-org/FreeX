using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void DynamicSpillFormula_ProducesSchemaValidWorkbook()
    {
        var workbook = CreateDynamicSpillWorkbook("AuthoredDynamicSpill");

        using var saved = Save(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        AssertDynamicSpillFormula(saved, "A3", "A3:C3", "A1:C1*2");
    }

    [Fact]
    public void EditedPlainFormulaDynamicSpill_ProducesSchemaValidWorkbook()
    {
        using var source = CreatePlainFormulaWorkbook();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        var anchor = new CellAddress(sheet.Id, 3, 1);
        sheet.GetCell(anchor)!.ArrayMode.Should().Be(FormulaArrayMode.Implicit);

        sheet.SetFormula(anchor, "A1:C1*2");
        SetDynamicSpill(sheet, anchor, 2, 4, 6);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_formula_array_mode");
        SchemaErrors(saved).Should().BeEmpty();
        AssertDynamicSpillFormula(saved, "A3", "A3:C3", "A1:C1*2");

        saved.Position = 0;
        adapter.Load(saved).GetSheetAt(0).GetCell(3, 1)!.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
    }

    [Theory]
    [InlineData("""<f t="array" ref="A1:A1" ca="1">1+1</f>""", "array", "A1:A1", "ca", "1", 4)]
    [InlineData("""<f t="shared" ref="A1:A1" si="0">1+1</f>""", "shared", "A1:A1", "si", "0", 5)]
    public void PatchedAttributedFormulaCachedValue_ProducesSchemaValidWorkbook(
        string formulaElement,
        string expectedFormulaType,
        string expectedFormulaReference,
        string expectedMetadataAttribute,
        string expectedMetadataValue,
        double cachedValue)
    {
        using var source = CreateAttributedFormulaWorkbook(formulaElement);
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText.Should().Be("1+1");
        cell.Value = new NumberValue(cachedValue);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();

        var formula = ReadFormulaElement(saved, "A1");
        formula.Value.Should().Be("1+1");
        formula.Attribute("t")!.Value.Should().Be(expectedFormulaType);
        formula.Attribute("ref")!.Value.Should().Be(expectedFormulaReference);
        formula.Attribute(expectedMetadataAttribute)!.Value.Should().Be(expectedMetadataValue);
    }

    [Fact]
    public void PatchedFormulaCachedValueTypes_ProducesSchemaValidWorkbook()
    {
        using var source = CreateFormulaCachedValueTypesWorkbook();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.GetCell(1, 1)!.Value = new NumberValue(3.5);
        sheet.GetCell(1, 2)!.Value = new TextValue("fresh text");
        sheet.GetCell(1, 3)!.Value = new BoolValue(false);
        sheet.GetCell(1, 4)!.Value = ErrorValue.Value;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        AssertFormulaCachedCell(saved, "A1", null, "3.5", "1+1");
        AssertFormulaCachedCell(saved, "B1", "str", "fresh text", "CONCAT(\"o\",\"ld\")");
        AssertFormulaCachedCell(saved, "C1", "b", "0", "1=1");
        AssertFormulaCachedCell(saved, "D1", "e", "#VALUE!", "1/0");
    }

    [Fact]
    public void PatchedFormulaCachedValue_WithCellExtension_ProducesSchemaValidWorkbook()
    {
        using var source = CreateFormulaCellExtensionValuePatchWorkbook();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.GetCell(1, 1)!.Value = new TextValue("fresh formula cache");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        SchemaErrors(saved).Should().BeEmpty();

        AssertFormulaCachedCell(saved, "A1", "str", "fresh formula cache", "1+1");
        AssertCellChildren(saved, "A1", "f", "v", "extLst");
    }

    private static Workbook CreateDynamicSpillWorkbook(string name)
    {
        var workbook = new Workbook(name);
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));

        var anchor = new CellAddress(sheet.Id, 3, 1);
        sheet.SetFormula(anchor, "A1:C1*2");
        SetDynamicSpill(sheet, anchor, 2, 4, 6);

        return workbook;
    }

    private static MemoryStream CreatePlainFormulaWorkbook()
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell(1, 1).Value = 1;
            sheet.Cell(1, 2).Value = 2;
            sheet.Cell(1, 3).Value = 3;
            sheet.Cell(3, 1).FormulaA1 = "A1:C1*2";
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateAttributedFormulaWorkbook(string formulaElement)
    {
        var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/calcChain.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml"/>
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
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain" Target="calcChain.xml"/>
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
                "xl/calcChain.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <calcChain xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <c r="A1" i="1"/>
                </calcChain>
                """),
            (
                "xl/worksheets/sheet1.xml",
                $$"""
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <dimension ref="A1:A1"/>
                  <sheetData>
                    <row r="1"><c r="A1">{{formulaElement}}<v>2</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreateFormulaCellExtensionValuePatchWorkbook()
    {
        var package = XlsxPackageTestFixtures.CreatePackage(
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
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:x="urn:freex:test">
                  <dimension ref="A1:A1"/>
                  <sheetData>
                    <row r="1">
                      <c r="A1"><f>1+1</f><v>2</v><extLst><ext uri="{FREEX-CELL-FORMULA}"><x:cellExt value="formula-extension"/></ext></extLst></c>
                    </row>
                  </sheetData>
                </worksheet>
                """));

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreateFormulaCachedValueTypesWorkbook()
    {
        var package = XlsxPackageTestFixtures.CreatePackage(
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
                  <dimension ref="A1:D1"/>
                  <sheetData>
                    <row r="1">
                      <c r="A1"><f>1+1</f><v>2</v></c>
                      <c r="B1" t="str"><f>CONCAT("o","ld")</f><v>old</v></c>
                      <c r="C1" t="b"><f>1=1</f><v>1</v></c>
                      <c r="D1" t="e"><f>1/0</f><v>#DIV/0!</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """));

        package.Position = 0;
        return package;
    }

    private static void SetDynamicSpill(Sheet sheet, CellAddress anchor, params double[] values)
    {
        values.Should().NotBeEmpty();
        sheet.GetCell(anchor)!.Value = new NumberValue(values[0]);

        var cells = new ScalarValue[1, values.Length];
        for (var col = 0; col < values.Length; col++)
            cells[0, col] = new NumberValue(values[col]);

        sheet.SetSpillRange(anchor, new RangeValue(cells, anchor.Row, anchor.Col));
    }

    private static void AssertDynamicSpillFormula(
        MemoryStream saved,
        string formulaCellReference,
        string spillReference,
        string formulaText)
    {
        var formula = ReadFormulaElement(saved, formulaCellReference);

        formula.Attribute("t")!.Value.Should().Be("array");
        formula.Attribute("ref")!.Value.Should().Be(spillReference);
        formula.Value.Should().Be(formulaText);
    }

    private static XElement ReadFormulaElement(MemoryStream saved, string reference)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var formula = worksheetXml
            .Descendants(worksheetNs + "c")
            .Single(element => element.Attribute("r")?.Value == reference)
            .Element(worksheetNs + "f");

        formula.Should().NotBeNull();
        return formula!;
    }

    private static void AssertFormulaCachedCell(
        MemoryStream saved,
        string reference,
        string? expectedType,
        string expectedCachedValue,
        string expectedFormulaText)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cell = ReadCellElement(saved, reference);

        if (expectedType is null)
            cell.Attribute("t").Should().BeNull();
        else
            cell.Attribute("t")!.Value.Should().Be(expectedType);

        cell.Element(worksheetNs + "f")!.Value.Should().Be(expectedFormulaText);
        cell.Element(worksheetNs + "v")!.Value.Should().Be(expectedCachedValue);
        cell.Element(worksheetNs + "is").Should().BeNull();
    }

    private static void AssertCellChildren(MemoryStream saved, string reference, params string[] expectedChildNames)
    {
        ReadCellElement(saved, reference)
            .Elements()
            .Select(element => element.Name.LocalName)
            .Should()
            .Equal(expectedChildNames);
    }

    private static XElement ReadCellElement(MemoryStream saved, string reference)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return new XElement(LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!)
            .Descendants(worksheetNs + "c")
            .Single(element => element.Attribute("r")?.Value == reference));
    }
}
