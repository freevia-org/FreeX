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

        AssertPackageHasNoHealthIssues(saved);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        AssertDocumentPropertiesWereRetained(archive);
        AssertRootPackageRelationshipsWereRetained(archive);
        AssertContentTypeOverridesWereRetained(archive);
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
        worksheetXml.Root!.Element(MainNs + "dimension")!.Attribute("nativeDimensionAttr").Should().BeNull();

        var sheetPr = worksheetXml.Root.Element(MainNs + "sheetPr");
        sheetPr.Should().NotBeNull();
        sheetPr!.Attribute("filterMode")!.Value.Should().Be("1");
        sheetPr.Element(FxNs + "sheetPrNativeChild").Should().BeNull();

        var sheetFormat = worksheetXml.Root.Element(MainNs + "sheetFormatPr");
        sheetFormat.Should().NotBeNull();
        sheetFormat!.Attribute("baseColWidth")!.Value.Should().Be("12");
        sheetFormat.Attribute("nativeSheetFormatAttr").Should().BeNull();
        sheetFormat.Element(FxNs + "sheetFormatNativeChild").Should().BeNull();

        var printOptions = worksheetXml.Root.Element(MainNs + "printOptions");
        printOptions.Should().NotBeNull();
        printOptions!.Attribute("gridLines")?.Value.Should().NotBe("1");
        printOptions.Attribute("gridLinesSet")!.Value.Should().Be("1");
        printOptions.Attribute("nativePrintOptionsAttr").Should().BeNull();

        var row2 = worksheetXml.Root.Element(MainNs + "sheetData")!
            .Elements(MainNs + "row")
            .Single(row => row.Attribute("r")?.Value == "2");
        row2.Attribute("customRowAttr").Should().BeNull();
        row2.Element(FxNs + "rowNativeChild").Should().BeNull();
        row2.Element(MainNs + "extLst")!.ToString(SaveOptions.DisableFormatting).Should().Contain("{FREEX-ROW-EXT}");

        var cellA2 = row2.Elements(MainNs + "c").Single(cell => cell.Attribute("r")?.Value == "A2");
        cellA2.Attribute("cm").Should().BeNull();
        cellA2.Attribute("vm").Should().BeNull();
        cellA2.Attribute("customCellAttr").Should().BeNull();
        cellA2.Element(FxNs + "cellNativeChild").Should().BeNull();
        cellA2.Element(MainNs + "extLst")!.ToString(SaveOptions.DisableFormatting).Should().Contain("{FREEX-CELL-EXT}");
        var formula = cellA2.Element(MainNs + "f");
        formula.Should().NotBeNull();
        formula!.Attribute("t")!.Value.Should().Be("array");
        formula.Attribute("ref")!.Value.Should().Be("A2:A2");
        formula.Attribute("ca")!.Value.Should().Be("1");
        formula.Attribute("customFormulaAttr").Should().BeNull();

        worksheetText.Should().Contain("protectedRanges");
        worksheetText.Should().Contain("name=\"EditableInput\"");
        worksheetText.Should().Contain("password=\"ABCD\"");
        worksheetText.Should().Contain("{FREEX-PROTECTED-RANGE}");
        // The native "NativeMultiArea" range's sqref ("C1 D1") is modeled as one AllowEditRange per
        // area (so CommandGuards.CanEditCell enforces both, not just the first) and re-emitted from
        // the model on save, rather than round-tripped as an inert, unenforced native-only
        // passthrough copy of the original element.
        worksheetText.Should().Contain("sqref=\"C1:C1\"");
        worksheetText.Should().Contain("sqref=\"D1:D1\"");
        worksheetText.Should().NotContain("nativeUnsupportedRange");

        worksheetText.Should().Contain("ignoredErrors");
        worksheetText.Should().NotContain("nativeIgnoredErrorsAttr=\"kept\"");
        worksheetText.Should().Contain("twoDigitTextYear=\"1\"");
        worksheetText.Should().Contain("cellWatches");
        worksheetText.Should().NotContain("nativeCellWatchesAttr=\"kept\"");
        worksheetText.Should().NotContain("nativeWatchAttr=\"kept\"");
        worksheetText.Should().Contain("{FREEX-WORKSHEET-EXT}");

        var worksheetRels = LoadXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
        worksheetRels.ToString(SaveOptions.DisableFormatting).Should().Contain("printerSettings/printerSettings1.bin");
        worksheetRels.ToString(SaveOptions.DisableFormatting).Should().Contain("/printerSettings");
        AssertInternalRelationshipTargetWasRetained(
            archive,
            "xl/worksheets/_rels/sheet1.xml.rels",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings",
            "../printerSettings/printerSettings1.bin");
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
        var legacyDrawing = worksheetXml.Root!.Element(MainNs + "legacyDrawing");
        legacyDrawing.Should().NotBeNull();

        var legacyDrawingRelationshipId = legacyDrawing!.Attribute(RelNs + "id")!.Value;
        var legacyDrawingRelationship = LoadXml(archive, "xl/worksheets/_rels/sheet1.xml.rels")
            .Root!
            .Elements(PackageRelNs + "Relationship")
            .Single(relationship => string.Equals(
                relationship.Attribute("Id")?.Value,
                legacyDrawingRelationshipId,
                StringComparison.Ordinal));
        legacyDrawingRelationship.Attribute("Type")!.Value.Should().Be(
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing");
        legacyDrawingRelationship.Attribute("TargetMode")?.Value.Should().NotBe("External");

        var legacyDrawingTarget = legacyDrawingRelationship.Attribute("Target")!.Value;
        legacyDrawingTarget.Should().EndWith(".vml");
        var legacyDrawingPart = legacyDrawingTarget.StartsWith("/", StringComparison.Ordinal)
            ? legacyDrawingTarget.TrimStart('/')
            : "xl/" + legacyDrawingTarget["../".Length..];
        legacyDrawingPart.Should().StartWith("xl/drawings/", "worksheet legacy drawing should target a retained drawing part");
        archive.GetEntry(legacyDrawingPart)
            .Should()
            .NotBeNull("worksheet legacy drawing relationship should resolve to the retained VML part");
    }
}
