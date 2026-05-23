using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.Core.IO.Tests;

public partial class FileAdapterSmokeTests
{
    private static void AddMinimalWorkbookExtensionList(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace x15Ns = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Add(new XElement(
                workbookNs + "extLst",
                new XElement(
                    workbookNs + "ext",
                    new XAttribute("uri", "{00112233-4455-6677-8899-AABBCCDDEEFF}"),
                    new XElement(
                        x15Ns + "futureMetadata",
                        new XAttribute(XNamespace.Xmlns + "x15", x15Ns),
                        new XAttribute("name", "FreexcelUnknownWorkbookExtension")))));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalWorkbookWebPublishObjects(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Add(new XElement(
                workbookNs + "webPublishObjects",
                new XAttribute("count", "1"),
                new XElement(
                    workbookNs + "webPublishObject",
                    new XAttribute("id", "1"),
                    new XAttribute("divId", "FreexcelWebPublish"),
                    new XAttribute("sourceObject", "Data"),
                    new XAttribute("destinationFile", "https://example.invalid/report.htm"),
                    new XAttribute("title", "Report"),
                    new XAttribute("autoRepublish", "0"))));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalWorkbookWebPublishingSettings(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Add(new XElement(
                workbookNs + "webPublishing",
                new XAttribute("css", "1"),
                new XAttribute("thicket", "0"),
                new XAttribute("longFileNames", "1"),
                new XAttribute("vml", "1"),
                new XAttribute("allowPng", "1"),
                new XAttribute("targetScreenSize", "800x600"),
                new XAttribute("dpi", "96")));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalWorkbookRevisionPointer(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/revisionHeaders/revisionHeader1.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.revisionHeaders+xml");
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/revisions/revisionLog1.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.revisionLog+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var workbookRelsPath = "xl/_rels/workbook.xml.rels";
            var workbookRelsXml = LoadPackageXml(archive.GetEntry(workbookRelsPath)!);
            workbookRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelRevisionHeaders"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/revisionHeaders"),
                new XAttribute("Target", "revisionHeaders/revisionHeader1.xml")));
            ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.AddFirst(new XElement(
                workbookNs + "revisionPtr",
                new XAttribute("revIDLastSave", "1"),
                new XAttribute("documentId", "FreexcelRevisionDoc"),
                new XAttribute("coauthVersionLast", "1"),
                new XAttribute("coauthVersionMax", "1")));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

            ReplacePackageXml(archive, "xl/revisionHeaders/revisionHeader1.xml", new XDocument(
                new XElement(
                    workbookNs + "headers",
                    new XElement(
                        workbookNs + "header",
                        new XAttribute("guid", "{00112233-4455-6677-8899-AABBCCDDEEFF}"),
                        new XAttribute("dateTime", "2026-05-20T00:00:00Z"),
                        new XAttribute("maxSheetId", "1")))));
            ReplacePackageXml(archive, "xl/revisions/revisionLog1.xml", new XDocument(
                new XElement(workbookNs + "revisions")));
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalWorkbookOleSize(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Add(new XElement(
                workbookNs + "oleSize",
                new XAttribute("ref", "A1:D12")));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddUnsupportedDefinedName(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            var definedNames = workbookXml.Root!.Element(workbookNs + "definedNames");
            if (definedNames is null)
            {
                definedNames = new XElement(workbookNs + "definedNames");
                workbookXml.Root!.Add(definedNames);
            }

            definedNames.Add(new XElement(
                workbookNs + "definedName",
                new XAttribute("name", "DynamicSalesRange"),
                new XAttribute("hidden", "1"),
                "1+1"));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddAdditionalWorkbookView(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            var bookViews = workbookXml.Root!.Element(workbookNs + "bookViews");
            if (bookViews is null)
            {
                bookViews = new XElement(workbookNs + "bookViews");
                workbookXml.Root!.AddFirst(bookViews);
            }

            bookViews.Add(new XElement(
                workbookNs + "workbookView",
                new XAttribute("visibility", "hidden"),
                new XAttribute("minimized", "1"),
                new XAttribute("showHorizontalScroll", "0"),
                new XAttribute("showVerticalScroll", "0"),
                new XAttribute("showSheetTabs", "0"),
                new XAttribute("tabRatio", "700"),
                new XAttribute("firstSheet", "0"),
                new XAttribute("activeTab", "0")));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddPrimaryWorkbookViewNativeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            var bookViews = workbookXml.Root!.Element(workbookNs + "bookViews");
            bookViews.Should().NotBeNull();
            var workbookView = bookViews!.Elements(workbookNs + "workbookView").Single();
            workbookView.SetAttributeValue("visibility", "visible");
            workbookView.SetAttributeValue("showSheetTabs", "0");
            workbookView.SetAttributeValue("tabRatio", "700");
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddCustomWorkbookViews(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Add(new XElement(
                workbookNs + "customWorkbookViews",
                new XElement(
                    workbookNs + "customWorkbookView",
                    new XAttribute("name", "FreexcelView"),
                    new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                    new XAttribute("autoUpdate", "0"),
                    new XAttribute("mergeInterval", "0"),
                    new XAttribute("personalView", "0"),
                    new XAttribute("includePrintSettings", "1"),
                    new XAttribute("includeHiddenRowCol", "1"))));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorkbookFileVersion(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.AddFirst(new XElement(
                workbookNs + "fileVersion",
                new XAttribute("appName", "xl"),
                new XAttribute("lastEdited", "7"),
                new XAttribute("lowestEdited", "7"),
                new XAttribute("rupBuild", "28129")));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorkbookFileSharing(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.AddFirst(new XElement(
                workbookNs + "fileSharing",
                new XAttribute("readOnlyRecommended", "1"),
                new XAttribute("userName", "FreexcelTest")));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorkbookFileRecoveryProperties(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Add(new XElement(
                workbookNs + "fileRecoveryPr",
                new XAttribute("autoRecover", "1"),
                new XAttribute("crashSave", "1"),
                new XAttribute("repairLoad", "0")));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorkbookSmartTagMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Add(
                new XElement(
                    workbookNs + "smartTagPr",
                    new XAttribute("embed", "1"),
                    new XAttribute("show", "all")),
                new XElement(
                    workbookNs + "smartTagTypes",
                    new XElement(
                        workbookNs + "smartTagType",
                        new XAttribute("namespaceUri", "urn:schemas-microsoft-com:office:smarttags"),
                        new XAttribute("name", "place"))));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorkbookFunctionGroups(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Add(new XElement(
                workbookNs + "functionGroups",
                new XAttribute("builtInGroupCount", "16"),
                new XElement(
                    workbookNs + "functionGroup",
                    new XAttribute("name", "FreexcelNativeFunctions"))));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddUnsupportedWorkbookProperties(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace freexcelNs = "urn:freexcel:test";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            var workbookPr = workbookXml.Root!.Element(workbookNs + "workbookPr");
            if (workbookPr is null)
            {
                workbookPr = new XElement(workbookNs + "workbookPr");
                workbookXml.Root!.AddFirst(workbookPr);
            }

            workbookPr.SetAttributeValue("date1904", "1");
            workbookPr.SetAttributeValue("defaultThemeVersion", "166925");
            workbookPr.Add(
                new XElement(freexcelNs + "workbookPrNativeChild", new XAttribute("id", "first")),
                new XElement(freexcelNs + "workbookPrNativeChild", new XAttribute("id", "second")));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorkbookCalculationNativeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            var calcPr = workbookXml.Root!.Element(workbookNs + "calcPr");
            calcPr.Should().NotBeNull();
            calcPr!.SetAttributeValue("calcId", "191029");
            calcPr.SetAttributeValue("refMode", "A1");
            calcPr.SetAttributeValue("fullPrecision", "0");
            calcPr.SetAttributeValue("concurrentCalc", "1");
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

}
