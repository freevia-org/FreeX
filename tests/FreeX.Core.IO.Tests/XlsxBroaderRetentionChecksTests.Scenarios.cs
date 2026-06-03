using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxBroaderRetentionChecksTests
{
    [Fact]
    public void LoadEditSave_RetainsWorkbookDocumentStylesAndPackageMetadata()
    {
        using var source = CreateBasePackage();
        PatchPackage(source, AddWorkbookDocumentStylesAndPackageMetadata);

        using var saved = LoadEditSave(source, workbook =>
        {
            workbook.Uses1904DateSystem.Should().BeTrue();
            workbook.FileSharing.Should().NotBeNull();
            workbook.Uses1904DateSystem = false;
            workbook.FileSharing!.UserName = "EditedUser";
        });

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        AssertDocumentPropertiesWereRetained(archive);
        AssertWorkbookMetadataWasRetainedWithoutOverridingModeledState(archive);
        AssertStyleAndPackagePartsWereRetained(archive);
    }

    [Fact]
    public void LoadEditSave_RetainsWorksheetXmlAndPrinterMetadataMatrix()
    {
        using var source = CreateBasePackage();
        PatchPackage(source, AddWorksheetXmlAndPrinterMetadata);

        using var saved = LoadEditSave(source, workbook =>
        {
            var sheet = workbook.GetSheetAt(0);
            sheet.PrintGridlines.Should().BeTrue();
            sheet.PrintGridlines = false;
        });

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = LoadXml(archive, "xl/worksheets/sheet1.xml");
        var worksheetText = worksheetXml.ToString(SaveOptions.DisableFormatting);

        var savedDimensionRef = worksheetXml.Root!.Element(MainNs + "dimension")!.Attribute("ref")!.Value;
        savedDimensionRef.Should().NotBe("A1:C2");
        savedDimensionRef.Should().EndWith("5");
        worksheetXml.Root!.Element(MainNs + "dimension")!.Attribute("nativeDimensionAttr")!.Value.Should().Be("kept");

        var sheetPr = worksheetXml.Root.Element(MainNs + "sheetPr");
        sheetPr.Should().NotBeNull();
        sheetPr!.Attribute("filterMode")!.Value.Should().Be("1");
        sheetPr.Element(FxNs + "sheetPrNativeChild")!.Attribute("id")!.Value.Should().Be("sheet-pr");

        var sheetFormat = worksheetXml.Root.Element(MainNs + "sheetFormatPr");
        sheetFormat.Should().NotBeNull();
        sheetFormat!.Attribute("baseColWidth")!.Value.Should().Be("12");
        sheetFormat.Attribute("nativeSheetFormatAttr")!.Value.Should().Be("kept");
        sheetFormat.Element(FxNs + "sheetFormatNativeChild")!.Attribute("id")!.Value.Should().Be("sheet-format");

        var printOptions = worksheetXml.Root.Element(MainNs + "printOptions");
        printOptions.Should().NotBeNull();
        printOptions!.Attribute("gridLines")?.Value.Should().NotBe("1");
        printOptions.Attribute("gridLinesSet")!.Value.Should().Be("1");
        printOptions.Attribute("nativePrintOptionsAttr")!.Value.Should().Be("kept");

        var row2 = worksheetXml.Root.Element(MainNs + "sheetData")!
            .Elements(MainNs + "row")
            .Single(row => row.Attribute("r")?.Value == "2");
        row2.Attribute("customRowAttr")!.Value.Should().Be("row-native");
        row2.Element(FxNs + "rowNativeChild")!.Attribute("id")!.Value.Should().Be("row-child");
        row2.Element(MainNs + "extLst")!.ToString(SaveOptions.DisableFormatting).Should().Contain("{FREEX-ROW-EXT}");

        var cellA2 = row2.Elements(MainNs + "c").Single(cell => cell.Attribute("r")?.Value == "A2");
        cellA2.Attribute("cm")!.Value.Should().Be("2");
        cellA2.Attribute("vm")!.Value.Should().Be("1");
        cellA2.Attribute("customCellAttr")!.Value.Should().Be("cell-native");
        cellA2.Element(FxNs + "cellNativeChild")!.Attribute("id")!.Value.Should().Be("cell-child");
        cellA2.Element(MainNs + "extLst")!.ToString(SaveOptions.DisableFormatting).Should().Contain("{FREEX-CELL-EXT}");
        var formula = cellA2.Element(MainNs + "f");
        formula.Should().NotBeNull();
        formula!.Attribute("t")!.Value.Should().Be("array");
        formula.Attribute("ref")!.Value.Should().Be("A2:A2");
        formula.Attribute("ca")!.Value.Should().Be("1");
        formula.Attribute("customFormulaAttr")!.Value.Should().Be("formula-native");

        worksheetText.Should().Contain("protectedRanges");
        worksheetText.Should().Contain("name=\"EditableInput\"");
        worksheetText.Should().Contain("password=\"ABCD\"");
        worksheetText.Should().Contain("{FREEX-PROTECTED-RANGE}");
        worksheetText.Should().Contain("sqref=\"A1 B1\"");
        worksheetText.Should().Contain("nativeUnsupportedRange=\"kept\"");

        worksheetText.Should().Contain("ignoredErrors");
        worksheetText.Should().Contain("nativeIgnoredErrorsAttr=\"kept\"");
        worksheetText.Should().Contain("twoDigitTextYear=\"1\"");
        worksheetText.Should().Contain("cellWatches");
        worksheetText.Should().Contain("nativeCellWatchesAttr=\"kept\"");
        worksheetText.Should().Contain("nativeWatchAttr=\"kept\"");
        worksheetText.Should().Contain("{FREEX-WORKSHEET-EXT}");

        var worksheetRels = LoadXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
        worksheetRels.ToString(SaveOptions.DisableFormatting).Should().Contain("printerSettings/printerSettings1.bin");
        worksheetRels.ToString(SaveOptions.DisableFormatting).Should().Contain("/printerSettings");
        worksheetXml.Root.Element(MainNs + "pageSetup")!.Attribute(RelNs + "id").Should().NotBeNull();
        ReadEntryBytes(archive, "xl/printerSettings/printerSettings1.bin").Should().Equal(0x46, 0x58, 0x50, 0x52, 0x4E);
    }

    [Fact]
    public void LoadEditSave_RetainsRichSharedStringsInlineStringsAndLegacyComments()
    {
        using var source = CreateBasePackage();
        PatchPackage(source, AddTextAndCommentPayloadMetadata);

        using var saved = LoadEditSave(source);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var sharedStrings = LoadXml(archive, "xl/sharedStrings.xml");
        var richSharedString = sharedStrings.Root!
            .Elements(MainNs + "si")
            .Single(item => ReadSharedStringPlainText(item) == "Rich phonetic");
        richSharedString.Elements(MainNs + "r").Should().HaveCount(2);
        richSharedString.Element(MainNs + "rPh").Should().NotBeNull();
        richSharedString.Element(MainNs + "phoneticPr")!.Attribute("type")!.Value.Should().Be("noConversion");

        var worksheetXml = LoadXml(archive, "xl/worksheets/sheet1.xml");
        var inlineCell = worksheetXml.Root!
            .Element(MainNs + "sheetData")!
            .Descendants(MainNs + "c")
            .Single(cell => cell.Attribute("r")?.Value == "A1");
        inlineCell.Attribute("t")!.Value.Should().Be("inlineStr");
        inlineCell.Element(MainNs + "is")!.Element(MainNs + "rPh").Should().NotBeNull();
        inlineCell.Element(MainNs + "is")!.Element(MainNs + "phoneticPr")!.Attribute("type")!.Value.Should().Be("noConversion");

        var commentsEntry = archive.Entries.Single(entry =>
            entry.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        var commentsXml = LoadXml(commentsEntry);
        commentsXml.ToString(SaveOptions.DisableFormatting).Should().Contain("FreeXBold");
        commentsXml.ToString(SaveOptions.DisableFormatting).Should().Contain("Check ");
        commentsXml.ToString(SaveOptions.DisableFormatting).Should().Contain("this input");

        archive.Entries.Should().Contain(entry =>
            entry.FullName.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".vml", StringComparison.OrdinalIgnoreCase));
        worksheetXml.ToString(SaveOptions.DisableFormatting).Should().Contain("legacyDrawing");
    }
}
