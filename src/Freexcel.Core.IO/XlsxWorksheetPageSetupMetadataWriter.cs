using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetPageSetupMetadataWriter
{
    public static bool HasModeledPrinterAttributes(Sheet sheet) =>
        sheet.UsePrinterDefaults is not null ||
        sheet.PrintCopies is > 0 ||
        sheet.FitToPage is not null ||
        sheet.AutoPageBreaks is not null;

    public static void Save(
        Stream packageStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        if (worksheetPathMap is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets)
        {
            if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var changed = false;
            changed |= ApplyPageSetupAttributes(root, workbookNs, sheet);
            changed |= ApplyPageSetupProperties(root, workbookNs, sheet);

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static bool ApplyPageSetupAttributes(XElement root, XNamespace workbookNs, Sheet sheet)
    {
        var pageSetup = root.Element(workbookNs + "pageSetup");
        if (pageSetup is null)
        {
            if (sheet.UsePrinterDefaults is null && sheet.PrintCopies is not > 0)
                return false;

            pageSetup = new XElement(workbookNs + "pageSetup");
            InsertPageSetupInOrder(root, workbookNs, pageSetup);
        }

        var changed = false;
        changed |= SetOptionalBoolAttribute(pageSetup, "usePrinterDefaults", sheet.UsePrinterDefaults);
        changed |= SetOptionalIntAttribute(pageSetup, "copies", sheet.PrintCopies);
        return changed;
    }

    private static bool ApplyPageSetupProperties(XElement root, XNamespace workbookNs, Sheet sheet)
    {
        var sheetProperties = root.Element(workbookNs + "sheetPr");
        var pageSetupProperties = sheetProperties?.Element(workbookNs + "pageSetUpPr");
        if (pageSetupProperties is null)
        {
            if (sheet.FitToPage is null && sheet.AutoPageBreaks is null)
                return false;

            sheetProperties ??= new XElement(workbookNs + "sheetPr");
            if (sheetProperties.Parent is null)
                root.AddFirst(sheetProperties);

            pageSetupProperties = new XElement(workbookNs + "pageSetUpPr");
            sheetProperties.Add(pageSetupProperties);
        }

        var changed = false;
        changed |= SetOptionalBoolAttribute(pageSetupProperties, "fitToPage", sheet.FitToPage);
        changed |= SetOptionalBoolAttribute(pageSetupProperties, "autoPageBreaks", sheet.AutoPageBreaks);
        return changed;
    }

    private static bool SetOptionalBoolAttribute(XElement element, XName name, bool? value) =>
        value is { } flag
            ? SetAttributeIfDifferent(element, name, flag ? "1" : "0")
            : RemoveAttributeIfPresent(element, name);

    private static bool SetOptionalIntAttribute(XElement element, XName name, int? value) =>
        value is > 0
            ? SetAttributeIfDifferent(element, name, value.Value.ToString(CultureInfo.InvariantCulture))
            : RemoveAttributeIfPresent(element, name);

    private static bool SetAttributeIfDifferent(XElement element, XName name, string value)
    {
        if (string.Equals(element.Attribute(name)?.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(name, value);
        return true;
    }

    private static bool RemoveAttributeIfPresent(XElement element, XName name)
    {
        if (element.Attribute(name) is null)
            return false;

        element.SetAttributeValue(name, null);
        return true;
    }

    private static void InsertPageSetupInOrder(
        XElement worksheetRoot,
        XNamespace workbookNs,
        XElement pageSetup)
    {
        var headerFooter = worksheetRoot.Element(workbookNs + "headerFooter");
        if (headerFooter is not null)
        {
            headerFooter.AddBeforeSelf(pageSetup);
            return;
        }

        var pageMargins = worksheetRoot.Element(workbookNs + "pageMargins");
        if (pageMargins is not null)
        {
            pageMargins.AddAfterSelf(pageSetup);
            return;
        }

        worksheetRoot.Add(pageSetup);
    }
}
