using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxBroaderRetentionChecksTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace CorePropsNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace AppPropsNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
    private static readonly XNamespace CustomPropsNs = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
    private static readonly XNamespace VtNs = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
    private static readonly XNamespace FxNs = "urn:freex:test";

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

    private static MemoryStream CreateBasePackage()
    {
        var workbook = new Workbook("BroaderRetention");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Inline phonetic"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Rich phonetic"));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 1), "A1+1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Check this input"));
        sheet.Comments[new CellAddress(sheet.Id, 2, 3)] = "Check this input";
        sheet.ColumnWidths[2] = 18;
        sheet.RowHeights[2] = 28;

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream LoadEditSave(MemoryStream source, Action<Workbook>? edit = null)
    {
        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new TextValue("retention edit marker"));
        edit?.Invoke(workbook);

        var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static void AddWorkbookDocumentStylesAndPackageMetadata(ZipArchive archive)
    {
        AddStableDocumentProperties(archive);
        AddCustomDocumentProperties(archive);
        AddWorkbookMetadata(archive);
        AddStylesheetMetadata(archive);
        AddExternalLinkPackage(archive);
        AddCustomXmlPackage(archive);
    }

    private static void AddStableDocumentProperties(ZipArchive archive)
    {
        var coreXml = archive.GetEntry("docProps/core.xml") is { } coreEntry
            ? LoadXml(coreEntry)
            : new XDocument(new XElement(CorePropsNs + "coreProperties"));
        SetElementValue(coreXml.Root!, DcNs + "subject", "FreeX retention subject");
        SetElementValue(coreXml.Root!, CorePropsNs + "keywords", "freex,xlsx,retention");
        SetElementValue(coreXml.Root!, CorePropsNs + "category", "Native Metadata");
        SetElementValue(coreXml.Root!, CorePropsNs + "contentStatus", "Reviewed");
        SetElementValue(coreXml.Root!, DcNs + "language", "en-US");
        SetElementValue(coreXml.Root!, CorePropsNs + "version", "2026.06");
        ReplaceXml(archive, "docProps/core.xml", coreXml);
        AddContentTypeOverride(
            archive,
            "/docProps/core.xml",
            "application/vnd.openxmlformats-package.core-properties+xml");
        AddRootRelationship(
            archive,
            "rIdFreeXCoreProperties",
            "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties",
            "docProps/core.xml");

        var appXml = archive.GetEntry("docProps/app.xml") is { } appEntry
            ? LoadXml(appEntry)
            : new XDocument(new XElement(AppPropsNs + "Properties"));
        SetElementValue(appXml.Root!, AppPropsNs + "Application", "Microsoft Excel");
        SetElementValue(appXml.Root!, AppPropsNs + "Company", "FreeX Test Lab");
        SetElementValue(appXml.Root!, AppPropsNs + "Manager", "XLSX Fidelity");
        SetElementValue(appXml.Root!, AppPropsNs + "Template", "RetentionTemplate.xltx");
        ReplaceXml(archive, "docProps/app.xml", appXml);
        AddContentTypeOverride(
            archive,
            "/docProps/app.xml",
            "application/vnd.openxmlformats-officedocument.extended-properties+xml");
        AddRootRelationship(
            archive,
            "rIdFreeXExtendedProperties",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties",
            "docProps/app.xml");
    }

    private static void AddCustomDocumentProperties(ZipArchive archive)
    {
        ReplaceXml(archive, "docProps/custom.xml", new XDocument(
            new XElement(
                CustomPropsNs + "Properties",
                new XAttribute(XNamespace.Xmlns + "vt", VtNs),
                new XElement(
                    CustomPropsNs + "property",
                    new XAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"),
                    new XAttribute("pid", "2"),
                    new XAttribute("name", "FreeXCustomProperty"),
                    new XElement(VtNs + "lpwstr", "kept")),
                new XElement(
                    CustomPropsNs + "property",
                    new XAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"),
                    new XAttribute("pid", "3"),
                    new XAttribute("name", "MSIP_Label_01234567-89ab-cdef-0123-456789abcdef_Enabled"),
                    new XElement(VtNs + "lpwstr", "true")))));
        AddContentTypeOverride(
            archive,
            "/docProps/custom.xml",
            "application/vnd.openxmlformats-officedocument.custom-properties+xml");
        AddRootRelationship(
            archive,
            "rIdFreeXCustomProperties",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties",
            "docProps/custom.xml");
    }

    private static void AddWorkbookMetadata(ZipArchive archive)
    {
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var root = workbookXml.Root!;
        root.Elements(MainNs + "fileVersion").Remove();
        root.Elements(MainNs + "fileSharing").Remove();
        root.Elements(MainNs + "workbookPr").Remove();
        root.Elements(MainNs + "bookViews").Remove();
        root.Elements(MainNs + "functionGroups").Remove();
        root.Elements(MainNs + "customWorkbookViews").Remove();
        root.Elements(MainNs + "smartTagPr").Remove();
        root.Elements(MainNs + "smartTagTypes").Remove();
        root.Elements(MainNs + "fileRecoveryPr").Remove();
        root.Elements(MainNs + "extLst").Remove();

        var sheets = root.Element(MainNs + "sheets")!;
        sheets.AddBeforeSelf(
            new XElement(
                MainNs + "fileVersion",
                new XAttribute("appName", "xl"),
                new XAttribute("lastEdited", "7"),
                new XAttribute("lowestEdited", "7"),
                new XAttribute("rupBuild", "28129"),
                new XAttribute("customVersionFlag", "keep")),
            new XElement(
                MainNs + "fileSharing",
                new XAttribute("readOnlyRecommended", "1"),
                new XAttribute("userName", "SourceUser"),
                new XAttribute("reservationPassword", "ABCD"),
                new XAttribute("customFileSharingAttr", "keep")),
            new XElement(
                MainNs + "workbookPr",
                new XAttribute("date1904", "1"),
                new XAttribute("defaultThemeVersion", "166925"),
                new XElement(FxNs + "workbookPrNativeChild", new XAttribute("id", "workbook-pr"))),
            new XElement(
                MainNs + "bookViews",
                new XAttribute("nativeBookViewsAttr", "kept"),
                new XElement(
                    MainNs + "workbookView",
                    new XAttribute("visibility", "visible"),
                    new XAttribute("showSheetTabs", "0"),
                    new XAttribute("tabRatio", "650"),
                    new XAttribute("firstSheet", "0"),
                    new XAttribute("activeTab", "0"),
                    new XAttribute("nativePrimaryViewAttr", "kept")),
                new XElement(
                    MainNs + "workbookView",
                    new XAttribute("visibility", "hidden"),
                    new XAttribute("tabRatio", "700"),
                    new XAttribute("firstSheet", "0"),
                    new XAttribute("activeTab", "0"),
                    new XAttribute("nativeHiddenViewAttr", "kept"))));

        sheets.AddAfterSelf(
            new XElement(
                MainNs + "functionGroups",
                new XAttribute("builtInGroupCount", "16"),
                new XAttribute("customFunctionGroupFlag", "keep"),
                new XElement(
                    MainNs + "functionGroup",
                    new XAttribute("name", "FreeXNativeFunctions"),
                    new XAttribute("customGroupFlag", "keep"))),
            new XElement(
                MainNs + "externalReferences",
                new XElement(MainNs + "externalReference", new XAttribute(RelNs + "id", "rIdFreeXExternalLink"))));

        root.Add(
            new XElement(
                MainNs + "customWorkbookViews",
                new XElement(
                    MainNs + "customWorkbookView",
                    new XAttribute("name", "NativeOnlyView"),
                    new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                    new XAttribute("autoUpdate", "0"),
                    new XAttribute("includePrintSettings", "1"),
                    new XAttribute("customWorkbookViewAttr", "keep"))),
            new XElement(
                MainNs + "smartTagPr",
                new XAttribute("embed", "1"),
                new XAttribute("show", "all"),
                new XAttribute("customSmartTagFlag", "keep")),
            new XElement(
                MainNs + "smartTagTypes",
                new XAttribute("customSmartTagTypesFlag", "keep"),
                new XElement(
                    MainNs + "smartTagType",
                    new XAttribute("namespaceUri", "urn:schemas-microsoft-com:office:smarttags"),
                    new XAttribute("name", "place"),
                    new XAttribute("customSmartTagTypeFlag", "keep"))),
            new XElement(
                MainNs + "fileRecoveryPr",
                new XAttribute("autoRecover", "1"),
                new XAttribute("crashSave", "1"),
                new XAttribute("repairLoad", "0"),
                new XAttribute("customRecoveryFlag", "keep")),
            new XElement(
                MainNs + "extLst",
                new XElement(
                    MainNs + "ext",
                    new XAttribute("uri", "{FREEX-WORKBOOK-EXT}"),
                    new XElement(FxNs + "workbookExt", new XAttribute("id", "workbook-ext")))));

        ReplaceXml(archive, "xl/workbook.xml", workbookXml);

        var workbookRels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        workbookRels.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", "rIdFreeXExternalLink"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink"),
            new XAttribute("Target", "externalLinks/externalLink1.xml")));
        ReplaceXml(archive, "xl/_rels/workbook.xml.rels", workbookRels);
    }

    private static void AddStylesheetMetadata(ZipArchive archive)
    {
        var stylesXml = LoadXml(archive, "xl/styles.xml");
        var root = stylesXml.Root!;
        root.Elements(MainNs + "colors").Remove();
        root.Elements(MainNs + "dxfs").Remove();
        root.Elements(MainNs + "tableStyles").Remove();
        root.Elements(MainNs + "extLst").Remove();

        root.Add(
            new XElement(
                MainNs + "colors",
                new XElement(
                    MainNs + "indexedColors",
                    new XElement(MainNs + "rgbColor", new XAttribute("rgb", "FF010203")))),
            new XElement(
                MainNs + "dxfs",
                new XAttribute("count", "1"),
                new XElement(
                    MainNs + "dxf",
                    new XAttribute("nativeDxfAttr", "kept"),
                    new XElement(
                        MainNs + "fill",
                        new XElement(
                            MainNs + "patternFill",
                            new XAttribute("patternType", "solid"),
                            new XElement(MainNs + "fgColor", new XAttribute("rgb", "FFABCDEF")))),
                    new XElement(FxNs + "dxfNativeChild", new XAttribute("id", "dxf-child")))),
            new XElement(
                MainNs + "tableStyles",
                new XAttribute("defaultPivotStyle", "PivotStyleMedium9"),
                new XAttribute("nativeTableStylesAttr", "kept"),
                new XElement(FxNs + "tableStylesNativeChild", new XAttribute("id", "table-styles-child")),
                new XElement(
                    MainNs + "tableStyle",
                    new XAttribute("name", "FreeXNativeTableStyle"),
                    new XAttribute("pivot", "0"),
                    new XAttribute("table", "1"),
                    new XAttribute("count", "1"),
                    new XElement(
                        MainNs + "tableStyleElement",
                        new XAttribute("type", "wholeTable"),
                        new XAttribute("dxfId", "0")))),
            new XElement(
                MainNs + "extLst",
                new XElement(
                    MainNs + "ext",
                    new XAttribute("uri", "{FREEX-STYLES-EXT}"),
                    new XElement(FxNs + "stylesExt", new XAttribute("id", "styles-ext")))));

        ReplaceXml(archive, "xl/styles.xml", stylesXml);
    }

    private static void AddExternalLinkPackage(ZipArchive archive)
    {
        ReplaceXml(archive, "xl/externalLinks/externalLink1.xml", new XDocument(
            new XElement(
                MainNs + "externalLink",
                new XAttribute(XNamespace.Xmlns + "r", RelNs),
                new XElement(
                    MainNs + "externalBook",
                    new XAttribute(RelNs + "id", "rIdFreeXExternalBook"),
                    new XElement(MainNs + "sheetNames",
                        new XElement(MainNs + "sheetName", new XAttribute("val", "LinkedSheet")))))));
        ReplaceXml(archive, "xl/externalLinks/_rels/externalLink1.xml.rels", new XDocument(
            new XElement(
                PackageRelNs + "Relationships",
                new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXExternalBook"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath"),
                    new XAttribute("Target", "linked-workbook.xlsx"),
                    new XAttribute("TargetMode", "External")))));
        AddContentTypeOverride(
            archive,
            "/xl/externalLinks/externalLink1.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml");
    }

    private static void AddCustomXmlPackage(ZipArchive archive)
    {
        WriteEntry(archive, "customXml/item1.xml", """
            <root xmlns="urn:freex:customXml">
              <value>retained-custom-xml</value>
            </root>
            """);
        ReplaceXml(archive, "customXml/itemProps1.xml", new XDocument(
            new XElement(
                XName.Get("datastoreItem", "http://schemas.openxmlformats.org/officeDocument/2006/customXml"),
                new XAttribute("itemID", "{01234567-89AB-CDEF-0123-456789ABCDEF}"))));
        ReplaceXml(archive, "customXml/_rels/item1.xml.rels", new XDocument(
            new XElement(
                PackageRelNs + "Relationships",
                new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXItemProps"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"),
                    new XAttribute("Target", "itemProps1.xml")))));
        AddContentTypeOverride(
            archive,
            "/customXml/itemProps1.xml",
            "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
        AddRootRelationship(
            archive,
            "rIdFreeXCustomXml",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml",
            "customXml/item1.xml");
    }

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

    private static void AssertDocumentPropertiesWereRetained(ZipArchive archive)
    {
        var coreXml = LoadXml(archive, "docProps/core.xml");
        coreXml.Root!.Element(DcNs + "subject")!.Value.Should().Be("FreeX retention subject");
        coreXml.Root!.Element(CorePropsNs + "keywords")!.Value.Should().Be("freex,xlsx,retention");
        coreXml.Root!.Element(CorePropsNs + "category")!.Value.Should().Be("Native Metadata");
        coreXml.Root!.Element(CorePropsNs + "contentStatus")!.Value.Should().Be("Reviewed");
        coreXml.Root!.Element(DcNs + "language")!.Value.Should().Be("en-US");
        coreXml.Root!.Element(CorePropsNs + "version")!.Value.Should().Be("2026.06");

        var appXml = LoadXml(archive, "docProps/app.xml");
        appXml.Root!.Element(AppPropsNs + "Application")!.Value.Should().Be("Microsoft Excel");
        appXml.Root!.Element(AppPropsNs + "Company")!.Value.Should().Be("FreeX Test Lab");
        appXml.Root!.Element(AppPropsNs + "Manager")!.Value.Should().Be("XLSX Fidelity");
        appXml.Root!.Element(AppPropsNs + "Template")!.Value.Should().Be("RetentionTemplate.xltx");

        var customXml = LoadXml(archive, "docProps/custom.xml").ToString(SaveOptions.DisableFormatting);
        customXml.Should().Contain("FreeXCustomProperty");
        customXml.Should().Contain("MSIP_Label_01234567-89ab-cdef-0123-456789abcdef_Enabled");
    }

    private static void AssertWorkbookMetadataWasRetainedWithoutOverridingModeledState(ZipArchive archive)
    {
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var workbookText = workbookXml.ToString(SaveOptions.DisableFormatting);
        var workbookPr = workbookXml.Root!.Element(MainNs + "workbookPr");
        workbookPr.Should().NotBeNull();
        workbookPr!.Attribute("date1904")?.Value.Should().NotBe("1");
        workbookPr.Attribute("defaultThemeVersion")!.Value.Should().Be("166925");
        workbookPr.Element(FxNs + "workbookPrNativeChild")!.Attribute("id")!.Value.Should().Be("workbook-pr");

        var fileSharing = workbookXml.Root.Element(MainNs + "fileSharing");
        fileSharing.Should().NotBeNull();
        fileSharing!.Attribute("userName")!.Value.Should().Be("EditedUser");
        fileSharing.Attribute("customFileSharingAttr")!.Value.Should().Be("keep");
        workbookText.Should().NotContain("userName=\"SourceUser\"");

        workbookText.Should().Contain("customVersionFlag=\"keep\"");
        workbookText.Should().Contain("customRecoveryFlag=\"keep\"");
        workbookText.Should().Contain("customSmartTagFlag=\"keep\"");
        workbookText.Should().Contain("customSmartTagTypeFlag=\"keep\"");
        workbookText.Should().Contain("customFunctionGroupFlag=\"keep\"");
        workbookText.Should().Contain("FreeXNativeFunctions");
        workbookText.Should().Contain("nativeHiddenViewAttr=\"kept\"");
        workbookText.Should().Contain("customWorkbookViewAttr=\"keep\"");
        workbookText.Should().Contain("{FREEX-WORKBOOK-EXT}");
        workbookText.Should().Contain("externalReferences");

        var workbookRels = LoadXml(archive, "xl/_rels/workbook.xml.rels").ToString(SaveOptions.DisableFormatting);
        workbookRels.Should().Contain("externalLinks/externalLink1.xml");
        workbookRels.Should().Contain("/externalLink");
        LoadXml(archive, "xl/externalLinks/externalLink1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Contain("LinkedSheet");
        LoadXml(archive, "xl/externalLinks/_rels/externalLink1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Contain("linked-workbook.xlsx");
    }

    private static void AssertStyleAndPackagePartsWereRetained(ZipArchive archive)
    {
        var stylesText = LoadXml(archive, "xl/styles.xml").ToString(SaveOptions.DisableFormatting);
        stylesText.Should().Contain("FF010203");
        stylesText.Should().Contain("nativeDxfAttr=\"kept\"");
        stylesText.Should().Contain("dxfNativeChild");
        stylesText.Should().Contain("nativeTableStylesAttr=\"kept\"");
        stylesText.Should().Contain("FreeXNativeTableStyle");
        stylesText.Should().Contain("{FREEX-STYLES-EXT}");

        ReadEntryText(archive, "customXml/item1.xml").Should().Contain("retained-custom-xml");
        LoadXml(archive, "customXml/itemProps1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Contain("{01234567-89AB-CDEF-0123-456789ABCDEF}");
        LoadXml(archive, "customXml/_rels/item1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Contain("customXmlProps");
    }

    private static void PatchPackage(MemoryStream stream, Action<ZipArchive> patch)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            patch(archive);

        stream.Position = 0;
    }

    private static XDocument LoadXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"package entry {entryName} should exist");
        return LoadXml(entry!);
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplaceXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream, SaveOptions.DisableFormatting);
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static void WriteEntry(ZipArchive archive, string entryName, byte[] bytes)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static string ReadEntryText(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"package entry {entryName} should exist");
        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] ReadEntryBytes(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"package entry {entryName} should exist");
        using var stream = entry!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void AddContentTypeOverride(ZipArchive archive, string partName, string contentType)
    {
        var contentTypesXml = LoadXml(archive, "[Content_Types].xml");
        var existing = contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .FirstOrDefault(element => string.Equals(element.Attribute("PartName")?.Value, partName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            contentTypesXml.Root.Add(new XElement(
                ContentTypeNs + "Override",
                new XAttribute("PartName", partName),
                new XAttribute("ContentType", contentType)));
        }
        else
        {
            existing.SetAttributeValue("ContentType", contentType);
        }

        ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);
    }

    private static void AddRootRelationship(ZipArchive archive, string id, string type, string target)
    {
        var relsXml = LoadXml(archive, "_rels/.rels");
        var matching = relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship =>
                string.Equals(relationship.Attribute("Id")?.Value, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(relationship.Attribute("Type")?.Value, type, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    relationship.Attribute("Target")?.Value.TrimStart('/'),
                    target.TrimStart('/'),
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var relationship in matching)
            relationship.Remove();

        relsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", type),
            new XAttribute("Target", target)));

        ReplaceXml(archive, "_rels/.rels", relsXml);
    }

    private static void SetElementValue(XElement root, XName name, string value)
    {
        var element = root.Element(name);
        if (element is null)
        {
            element = new XElement(name);
            root.Add(element);
        }

        element.Value = value;
    }

    private static string ReadSharedStringPlainText(XElement item)
    {
        var runs = item.Elements(MainNs + "r").ToList();
        if (runs.Count > 0)
            return string.Concat(runs.Select(run => run.Element(MainNs + "t")?.Value ?? string.Empty));

        return item.Element(MainNs + "t")?.Value ?? string.Empty;
    }
}
