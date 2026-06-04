using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxLoadedWorkbookPatchSaveTests
{
    public static TheoryData<ScalarValue, string?, string?> FormulaCachedValueCases => new()
    {
        { new NumberValue(99.5), null, "99.5" },
        { new TextValue("cached text"), "str", "cached text" },
        { new BoolValue(true), "b", "1" },
        { new ErrorValue("#N/A"), "e", "#N/A" },
        { BlankValue.Instance, null, null }
    };

    [Fact]
    public void Save_LoadedWorkbookWithExistingLiteralCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("  patched value  "));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("  patched value  ");
        ReadCellTextSpaceMode(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("preserve");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream)
            .GetSheetAt(0)
            .GetCell(1, 1)!
            .Value
            .Should()
            .Be(new TextValue("  patched value  "));
    }

    [Fact]
    public void Save_LoadedWorkbookWithNewLiteralCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("new value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadWorksheetDimension(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Be("A1:D4");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("new value");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream)
            .GetSheetAt(0)
            .GetCell(4, 4)!
            .Value
            .Should()
            .Be(new TextValue("new value"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithExistingCellStyleEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateStyledSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var sourceStyleCell = sheet.GetCell(1, 2);
        sourceStyleCell.Should().NotBeNull();
        sourceStyleCell!.StyleId.Should().NotBe(StyleId.Default);
        var patchedCell = sheet.GetCell(1, 1);
        patchedCell.Should().NotBeNull();
        patchedCell!.StyleId = sourceStyleCell.StyleId;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadCellStyleIndex(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be(ReadCellStyleIndex(sourceBytes, "xl/worksheets/sheet1.xml", "B1"));

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.GetCell(1, 1)!.Value
            .Should()
            .Be(new TextValue("plain"));
        reloaded.GetStyle(reloadedSheet.GetCell(1, 1)!.StyleId)
            .Should()
            .Be(reloaded.GetStyle(reloadedSheet.GetCell(1, 2)!.StyleId));
    }

    [Fact]
    public void Save_LoadedWorkbookWithExistingStyleOnlyStyleEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateStyledSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var styleOnlyStyleId = sheet.GetStyleOnly(1, 4);
        styleOnlyStyleId.Should().NotBeNull();
        var patchedCell = sheet.GetCell(1, 1);
        patchedCell.Should().NotBeNull();
        patchedCell!.StyleId = styleOnlyStyleId!.Value;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadCellStyleIndex(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be(ReadCellStyleIndex(sourceBytes, "xl/worksheets/sheet1.xml", "D1"));

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedStyleOnlyStyleId = reloadedSheet.GetStyleOnly(1, 4);
        reloadedStyleOnlyStyleId.Should().NotBeNull();
        reloaded.GetStyle(reloadedSheet.GetCell(1, 1)!.StyleId)
            .Should()
            .Be(reloaded.GetStyle(reloadedStyleOnlyStyleId!.Value));
    }

    [Fact]
    public void Save_LoadedWorkbookWithNewCellStyleEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(221, 235, 247)
        });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/styles.xml"));

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        reloaded.GetStyle(reloaded.GetSheetAt(0).GetCell(1, 1)!.StyleId)
            .Bold
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Save_LoadedWorkbookWithClearedLiteralCell_PatchesSourcePackage()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.ClearCell(2, 2);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        TryReadCellElement(savedBytes, "xl/worksheets/sheet1.xml", "B2")
            .Should()
            .BeNull();

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream)
            .GetSheetAt(0)
            .GetCell(2, 2)
            .Should()
            .BeNull();
    }

    [Theory]
    [MemberData(nameof(FormulaCachedValueCases))]
    public void Save_LoadedWorkbookWithFormulaCachedValueEdit_PatchesFormulaCache(
        ScalarValue cachedValue,
        string? expectedCellType,
        string? expectedRawValue)
    {
        var sourceBytes = CreateFormulaSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText.Should().Be("1+1");
        cell.Value = cachedValue;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/calcChain.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/calcChain.xml"));
        ReadCellFormula(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("1+1");
        ReadCellType(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be(expectedCellType);
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be(expectedRawValue);
    }

    [Fact]
    public void Save_LoadedWorkbookWithFormulaTextEdit_PatchesFormulaAndDropsCalcChain()
    {
        var sourceBytes = CreateFormulaSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText = "1+2";
        cell.Value = new NumberValue(3);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse();
        ReadContentTypeOverrides(savedBytes).Should().NotContain("/xl/calcChain.xml");
        ReadWorkbookRelationshipTypes(savedBytes)
            .Should()
            .NotContain("http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain");
        ReadCellFormula(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("1+2");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("3");
    }

    [Fact]
    public void Save_LoadedWorkbookWithClearedFormulaCell_PatchesSourcePackageAndDropsCalcChain()
    {
        var sourceBytes = CreateFormulaSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.ClearCell(1, 1);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse();
        ReadContentTypeOverrides(savedBytes).Should().NotContain("/xl/calcChain.xml");
        TryReadCellElement(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .BeNull();
    }

    [Fact]
    public void Save_LoadedWorkbookWithAttributedFormulaTextEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateFormulaSourcePackage("""<f t="shared" si="0">1+1</f>""");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText = "1+2";
        cell.Value = new NumberValue(3);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadCellFormula(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("1+2");
    }

    [Fact]
    public void Save_LoadedWorkbookWithWorksheetMetadataEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.ShowGridlines = false;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved).GetSheetAt(0);
        reloaded.ShowGridlines.Should().BeFalse();
        reloaded.GetCell(1, 1)!.Value.Should().Be(new TextValue("patched value"));
    }

    private static byte[] CreateSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "original value";
            sheet.Cell("B2").Value = 123.45;
            sheet.Cell("C3").Value = true;
            workbook.SaveAs(stream);
        }

        return stream.ToArray();
    }

    private static byte[] CreateStyledSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "plain";
            sheet.Cell("B1").Value = "styled";
            sheet.Cell("B1").Style.Font.Bold = true;
            sheet.Cell("B1").Style.Fill.BackgroundColor = XLColor.FromArgb(221, 235, 247);
            sheet.Cell("D1").Style.Font.Italic = true;
            workbook.SaveAs(stream);
        }

        return stream.ToArray();
    }

    private static byte[] CreateFormulaSourcePackage(string formulaElement = "<f>1+1</f>")
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
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <dimension ref="A1:A1"/>
                  <sheetData>
                    <row r="1"><c r="A1">FORMULA_ELEMENT<v>2</v></c></row>
                  </sheetData>
                </worksheet>
                """.Replace("FORMULA_ELEMENT", formulaElement, StringComparison.Ordinal)));

        return package.ToArray();
    }

    private static byte[] ReadPackageEntry(byte[] packageBytes, string path)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        using var bytes = new MemoryStream();
        entryStream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static bool PackageHasEntry(byte[] packageBytes, string path)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.GetEntry(path) is not null;
    }

    private static IReadOnlyList<string> ReadContentTypeOverrides(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        var ns = document.Root!.Name.Namespace;
        return document.Root!
            .Elements(ns + "Override")
            .Select(element => element.Attribute("PartName")?.Value ?? "")
            .ToList();
    }

    private static IReadOnlyList<string> ReadWorkbookRelationshipTypes(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
        var ns = document.Root!.Name.Namespace;
        return document.Root!
            .Elements(ns + "Relationship")
            .Select(element => element.Attribute("Type")?.Value ?? "")
            .ToList();
    }

    private static string? ReadCellText(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = ReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell.Name.Namespace;
        if (string.Equals(cell.Attribute("t")?.Value, "inlineStr", StringComparison.Ordinal))
            return cell.Element(ns + "is")?.Element(ns + "t")?.Value;

        return cell.Element(ns + "v")?.Value;
    }

    private static string? ReadCellFormula(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = ReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell.Name.Namespace;
        return cell.Element(ns + "f")?.Value;
    }

    private static string? ReadCellType(byte[] packageBytes, string worksheetPath, string reference) =>
        ReadCellElement(packageBytes, worksheetPath, reference).Attribute("t")?.Value;

    private static string? ReadCellStyleIndex(byte[] packageBytes, string worksheetPath, string reference) =>
        ReadCellElement(packageBytes, worksheetPath, reference).Attribute("s")?.Value;

    private static string? ReadCellTextSpaceMode(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = ReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell.Name.Namespace;
        return cell.Element(ns + "is")?.Element(ns + "t")?.Attribute(XNamespace.Xml + "space")?.Value;
    }

    private static string? ReadWorksheetDimension(byte[] packageBytes, string worksheetPath)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(worksheetPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        var ns = document.Root!.Name.Namespace;
        return document.Root.Element(ns + "dimension")?.Attribute("ref")?.Value;
    }

    private static XElement ReadCellElement(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = TryReadCellElement(packageBytes, worksheetPath, reference);
        cell.Should().NotBeNull();
        return cell!;
    }

    private static XElement? TryReadCellElement(byte[] packageBytes, string worksheetPath, string reference)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(worksheetPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        var ns = document.Root!.Name.Namespace;
        var cell = document
            .Descendants(ns + "c")
            .SingleOrDefault(element => string.Equals(element.Attribute("r")?.Value, reference, StringComparison.Ordinal));

        return cell;
    }
}
