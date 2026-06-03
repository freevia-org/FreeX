using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxBroaderRetentionChecksTests
{
    private static void AddWorksheetXmlAndPrinterMetadata(ZipArchive archive)
    {
        var worksheetXml = LoadXml(archive, "xl/worksheets/sheet1.xml");
        var root = worksheetXml.Root!;
        root.SetAttributeValue(XNamespace.Xmlns + "r", RelNs.NamespaceName);

        var sheetPr = root.Element(MainNs + "sheetPr");
        if (sheetPr is null)
        {
            sheetPr = new XElement(MainNs + "sheetPr");
            root.AddFirst(sheetPr);
        }

        sheetPr.SetAttributeValue("filterMode", "1");
        sheetPr.Add(new XElement(FxNs + "sheetPrNativeChild", new XAttribute("id", "sheet-pr")));

        var sheetFormat = root.Element(MainNs + "sheetFormatPr");
        sheetFormat.Should().NotBeNull();
        sheetFormat!.SetAttributeValue("baseColWidth", "12");
        sheetFormat.SetAttributeValue("nativeSheetFormatAttr", "kept");
        sheetFormat.Add(new XElement(FxNs + "sheetFormatNativeChild", new XAttribute("id", "sheet-format")));

        root.Element(MainNs + "dimension")!.SetAttributeValue("nativeDimensionAttr", "kept");

        var printOptions = root.Element(MainNs + "printOptions");
        if (printOptions is null)
        {
            printOptions = new XElement(MainNs + "printOptions");
            root.Add(printOptions);
        }

        printOptions.SetAttributeValue("gridLines", "1");
        printOptions.SetAttributeValue("gridLinesSet", "1");
        printOptions.SetAttributeValue("nativePrintOptionsAttr", "kept");

        var row2 = root.Element(MainNs + "sheetData")!
            .Elements(MainNs + "row")
            .Single(row => row.Attribute("r")?.Value == "2");
        row2.SetAttributeValue("customRowAttr", "row-native");
        row2.Add(
            new XElement(FxNs + "rowNativeChild", new XAttribute("id", "row-child")),
            new XElement(
                MainNs + "extLst",
                new XElement(
                    MainNs + "ext",
                    new XAttribute("uri", "{FREEX-ROW-EXT}"),
                    new XElement(FxNs + "rowExt", new XAttribute("id", "row-ext")))));

        var cellA2 = row2.Elements(MainNs + "c").Single(cell => cell.Attribute("r")?.Value == "A2");
        cellA2.SetAttributeValue("cm", "2");
        cellA2.SetAttributeValue("vm", "1");
        cellA2.SetAttributeValue("customCellAttr", "cell-native");
        cellA2.Add(
            new XElement(FxNs + "cellNativeChild", new XAttribute("id", "cell-child")),
            new XElement(
                MainNs + "extLst",
                new XElement(
                    MainNs + "ext",
                    new XAttribute("uri", "{FREEX-CELL-EXT}"),
                    new XElement(FxNs + "cellExt", new XAttribute("id", "cell-ext")))));

        var formula = cellA2.Element(MainNs + "f");
        formula.Should().NotBeNull();
        formula!.SetAttributeValue("t", "array");
        formula.SetAttributeValue("ref", "A2:A2");
        formula.SetAttributeValue("ca", "1");
        formula.SetAttributeValue("customFormulaAttr", "formula-native");

        var pageSetup = root.Element(MainNs + "pageSetup");
        if (pageSetup is null)
        {
            pageSetup = new XElement(MainNs + "pageSetup");
            root.Add(pageSetup);
        }

        pageSetup.SetAttributeValue("paperSize", "1");
        pageSetup.SetAttributeValue("orientation", "portrait");
        pageSetup.SetAttributeValue(RelNs + "id", "rIdFreeXPrinterSettings");

        root.Add(
            new XElement(
                MainNs + "protectedRanges",
                new XElement(
                    MainNs + "protectedRange",
                    new XAttribute("name", "EditableInput"),
                    new XAttribute("sqref", "A1"),
                    new XAttribute("password", "ABCD"),
                    new XElement(
                        MainNs + "extLst",
                        new XElement(MainNs + "ext", new XAttribute("uri", "{FREEX-PROTECTED-RANGE}")))),
                new XElement(
                    MainNs + "protectedRange",
                    new XAttribute("name", "NativeMultiArea"),
                    new XAttribute("sqref", "A1 B1"),
                    new XAttribute("nativeUnsupportedRange", "kept"))),
            new XElement(
                MainNs + "ignoredErrors",
                new XAttribute("nativeIgnoredErrorsAttr", "kept"),
                new XElement(
                    MainNs + "ignoredError",
                    new XAttribute("sqref", "A1"),
                    new XAttribute("numberStoredAsText", "1"),
                    new XAttribute("twoDigitTextYear", "1"))),
            new XElement(
                MainNs + "cellWatches",
                new XAttribute("nativeCellWatchesAttr", "kept"),
                new XElement(
                    MainNs + "cellWatch",
                    new XAttribute("r", "A2"),
                    new XAttribute("nativeWatchAttr", "kept"))),
            new XElement(
                MainNs + "extLst",
                new XElement(
                    MainNs + "ext",
                    new XAttribute("uri", "{FREEX-WORKSHEET-EXT}"),
                    new XElement(FxNs + "worksheetExt", new XAttribute("id", "worksheet-ext")))));

        ReplaceXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRels = archive.GetEntry(worksheetRelsPath) is null
            ? new XDocument(new XElement(PackageRelNs + "Relationships"))
            : LoadXml(archive, worksheetRelsPath);
        worksheetRels.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", "rIdFreeXPrinterSettings"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings"),
            new XAttribute("Target", "../printerSettings/printerSettings1.bin")));
        ReplaceXml(archive, worksheetRelsPath, worksheetRels);
        WriteEntry(archive, "xl/printerSettings/printerSettings1.bin", new byte[] { 0x46, 0x58, 0x50, 0x52, 0x4E });
        AddContentTypeOverride(
            archive,
            "/xl/printerSettings/printerSettings1.bin",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings");
    }

    private static void AddTextAndCommentPayloadMetadata(ZipArchive archive)
    {
        var worksheetXml = LoadXml(archive, "xl/worksheets/sheet1.xml");
        var cellA1 = worksheetXml.Root!
            .Element(MainNs + "sheetData")!
            .Descendants(MainNs + "c")
            .Single(cell => cell.Attribute("r")?.Value == "A1");
        cellA1.SetAttributeValue("t", "inlineStr");
        cellA1.Elements(MainNs + "v").Remove();
        cellA1.Elements(MainNs + "is").Remove();
        cellA1.Add(new XElement(
            MainNs + "is",
            new XElement(
                MainNs + "r",
                new XElement(
                    MainNs + "rPr",
                    new XElement(MainNs + "i"),
                    new XElement(MainNs + "rFont", new XAttribute("val", "FreeXInline"))),
                new XElement(MainNs + "t", "Inline ")),
            new XElement(
                MainNs + "r",
                new XElement(MainNs + "t", "phonetic")),
            new XElement(
                MainNs + "rPh",
                new XAttribute("sb", "0"),
                new XAttribute("eb", "6"),
                new XElement(MainNs + "t", "in-line")),
            new XElement(
                MainNs + "phoneticPr",
                new XAttribute("fontId", "1"),
                new XAttribute("type", "noConversion"))));
        ReplaceXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

        var sharedStringsXml = LoadXml(archive, "xl/sharedStrings.xml");
        var richSharedString = sharedStringsXml.Root!
            .Elements(MainNs + "si")
            .Single(item => item.Element(MainNs + "t")?.Value == "Rich phonetic");
        richSharedString.ReplaceNodes(
            new XElement(
                MainNs + "r",
                new XElement(
                    MainNs + "rPr",
                    new XElement(MainNs + "b"),
                    new XElement(MainNs + "rFont", new XAttribute("val", "FreeXRich"))),
                new XElement(MainNs + "t", "Rich ")),
            new XElement(
                MainNs + "r",
                new XElement(MainNs + "t", "phonetic")),
            new XElement(
                MainNs + "rPh",
                new XAttribute("sb", "0"),
                new XAttribute("eb", "4"),
                new XElement(MainNs + "t", "ri-chi")),
            new XElement(
                MainNs + "phoneticPr",
                new XAttribute("fontId", "1"),
                new XAttribute("type", "noConversion")));
        ReplaceXml(archive, "xl/sharedStrings.xml", sharedStringsXml);

        var commentsEntry = archive.Entries.Single(entry =>
            entry.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        ReplaceXml(archive, commentsEntry.FullName, new XDocument(
            new XElement(
                MainNs + "comments",
                new XElement(
                    MainNs + "authors",
                    new XElement(MainNs + "author", "Excel Reviewer")),
                new XElement(
                    MainNs + "commentList",
                    new XElement(
                        MainNs + "comment",
                        new XAttribute("ref", "C2"),
                        new XAttribute("authorId", "0"),
                        new XElement(
                            MainNs + "text",
                            new XElement(
                                MainNs + "r",
                                new XElement(
                                    MainNs + "rPr",
                                    new XElement(MainNs + "b"),
                                    new XElement(MainNs + "rFont", new XAttribute("val", "FreeXBold"))),
                                new XElement(MainNs + "t", "Check ")),
                            new XElement(
                                MainNs + "r",
                                new XElement(MainNs + "t", "this input"))))))));
    }
}
