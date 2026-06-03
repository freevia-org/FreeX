using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookSchemaNormalizer
{
    public static void Normalize(Stream xlsxStream)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Normalize(archive);
    }

    public static void Normalize(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        if (!NormalizeWorkbook(workbookXml, workbookNs))
            return;

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
    }

    internal static bool NormalizeWorkbook(XDocument workbookXml, XNamespace workbookNs)
    {
        var root = workbookXml.Root;
        if (root is null)
            return false;

        var orderedChildren = root.Elements()
            .Select((element, index) => new { Element = element, Index = index })
            .OrderBy(item => WorkbookChildOrder(item.Element, workbookNs))
            .ThenBy(item => item.Index)
            .Select(item => item.Element)
            .ToList();
        if (orderedChildren.Count == 0 || root.Elements().SequenceEqual(orderedChildren))
            return false;

        root.ReplaceNodes(orderedChildren);
        return true;
    }

    private static int WorkbookChildOrder(XElement element, XNamespace workbookNs) =>
        element.Name == workbookNs + "revisionPtr" ? 0 :
        element.Name == workbookNs + "fileVersion" ? 1 :
        element.Name == workbookNs + "fileSharing" ? 2 :
        element.Name == workbookNs + "workbookPr" ? 3 :
        element.Name == workbookNs + "workbookProtection" ? 4 :
        element.Name == workbookNs + "bookViews" ? 5 :
        element.Name == workbookNs + "sheets" ? 6 :
        element.Name == workbookNs + "functionGroups" ? 7 :
        element.Name == workbookNs + "externalReferences" ? 8 :
        element.Name == workbookNs + "definedNames" ? 9 :
        element.Name == workbookNs + "calcPr" ? 10 :
        element.Name == workbookNs + "oleSize" ? 11 :
        element.Name == workbookNs + "customWorkbookViews" ? 12 :
        element.Name == workbookNs + "pivotCaches" ? 13 :
        element.Name == workbookNs + "smartTagPr" ? 14 :
        element.Name == workbookNs + "smartTagTypes" ? 15 :
        element.Name == workbookNs + "webPublishing" ? 16 :
        element.Name == workbookNs + "fileRecoveryPr" ? 17 :
        element.Name == workbookNs + "webPublishObjects" ? 18 :
        element.Name == workbookNs + "extLst" ? 100 :
        90;
}
