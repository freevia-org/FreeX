using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxBroaderRetentionChecksTests
{
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
}
