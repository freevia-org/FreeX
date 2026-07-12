using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetDimensionDefaultsWriter
{
    public static bool HasNonDefaultDimensions(Sheet sheet) =>
        IsNonDefaultColumnWidth(sheet.DefaultColumnWidth) ||
        IsNonDefaultRowHeight(sheet.DefaultRowHeight) ||
        sheet.SheetFormatMetadata is not null;

    public static void Save(Stream packageStream, Workbook workbook)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        Save(archive, workbook, XlsxWorkbookWorksheetPathMap.TryCreate(archive));
    }

    public static void Save(Stream packageStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        Save(archive, workbook, worksheetPathMap);
    }

    private static void Save(ZipArchive archive, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null || worksheetPathMap is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets.Where(HasNonDefaultDimensions))
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
            var sheetFormat = root.Element(workbookNs + "sheetFormatPr");
            if (sheetFormat is null)
            {
                sheetFormat = new XElement(workbookNs + "sheetFormatPr");
                root.AddFirst(sheetFormat);
                changed = true;
            }

            if (IsNonDefaultColumnWidth(sheet.DefaultColumnWidth))
                changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(
                    sheetFormat,
                    "defaultColWidth",
                    FormatDouble(sheet.DefaultColumnWidth));

            var isNonDefaultRowHeight = IsNonDefaultRowHeight(sheet.DefaultRowHeight);
            if (isNonDefaultRowHeight)
            {
                changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(
                    sheetFormat,
                    "defaultRowHeight",
                    FormatDouble(sheet.DefaultRowHeight * (72.0 / 96.0)));
                changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(
                    sheetFormat,
                    "customHeight",
                    "1");
            }

            changed |= ApplyNativeSheetFormatMetadata(sheetFormat, sheet.SheetFormatMetadata, isNonDefaultRowHeight);

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static readonly IReadOnlyCollection<string> ModeledSheetFormatAttributes =
        ["defaultColWidth", "defaultRowHeight"];

    // When we've just written a live customHeight="1" for a genuinely new DefaultRowHeight, the bag's
    // stale customHeight (captured from the source file before the edit) must not be reapplied on top of it.
    private static readonly IReadOnlyCollection<string> ModeledSheetFormatAttributesWithCustomHeight =
        ["defaultColWidth", "defaultRowHeight", "customHeight"];

    private static bool ApplyNativeSheetFormatMetadata(
        XElement sheetFormat,
        NativeXmlPreserveBag? metadata,
        bool excludeCustomHeight)
    {
        if (metadata is null)
            return false;

        var modeledAttributes = excludeCustomHeight
            ? ModeledSheetFormatAttributesWithCustomHeight
            : ModeledSheetFormatAttributes;
        return XmlNativeBagSerializer.ApplyToElement(sheetFormat, metadata.Get("sheetFormatPr"), modeledAttributes);
    }

    private static bool IsNonDefaultColumnWidth(double value) =>
        double.IsFinite(value) && value > 0 && Math.Abs(value - 8.43) >= 0.01;

    private static bool IsNonDefaultRowHeight(double value) =>
        double.IsFinite(value) && value > 0 && Math.Abs(value - 20.0) >= 0.01;

    private static string FormatDouble(double value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

}
