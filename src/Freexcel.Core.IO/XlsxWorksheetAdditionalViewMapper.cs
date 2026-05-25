using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetAdditionalViewMapper
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static WorksheetAdditionalViewsModel? Read(XElement? sheetViews)
    {
        if (sheetViews is null)
            return null;

        var model = new WorksheetAdditionalViewsModel
        {
            Views = sheetViews.Elements(WorksheetNs + "sheetView")
                .Where(IsAdditionalView)
                .Select(ReadView)
                .ToList()
        };
        ReadNativeAttributes(sheetViews, model.NativeAttributes, []);

        return model.NativeAttributes.Count == 0 && model.Views.Count == 0
            ? null
            : model;
    }

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.AdditionalViews is not null))
        {
            if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var entry = archive.GetEntry(worksheetPath);
            if (entry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(entry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var changed = false;
            var sheetViews = root.Element(WorksheetNs + "sheetViews");
            if (sheetViews is null)
            {
                sheetViews = new XElement(WorksheetNs + "sheetViews");
                root.AddFirst(sheetViews);
                changed = true;
            }

            ApplyNativeAttributes(sheetViews, sheet.AdditionalViews!.NativeAttributes, []);
            changed |= sheet.AdditionalViews.NativeAttributes.Count > 0;
            foreach (var view in sheetViews.Elements(WorksheetNs + "sheetView").Where(IsAdditionalView).ToList())
            {
                view.Remove();
                changed = true;
            }

            foreach (var view in sheet.AdditionalViews.Views.Select(ToXml).OfType<XElement>())
            {
                sheetViews.Add(view);
                changed = true;
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static WorksheetAdditionalViewModel ReadView(XElement element)
    {
        var model = new WorksheetAdditionalViewModel
        {
            WorkbookViewId = element.Attribute("workbookViewId")?.Value,
            NativeXml = element.ToString(SaveOptions.DisableFormatting)
        };
        ReadNativeAttributes(element, model.NativeAttributes, ["workbookViewId"]);
        return model;
    }

    private static XElement? ToXml(WorksheetAdditionalViewModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.NativeXml))
        {
            try
            {
                return XElement.Parse(model.NativeXml);
            }
            catch
            {
                // Fall back to the structured model below.
            }
        }

        if (string.IsNullOrWhiteSpace(model.WorkbookViewId) && model.NativeAttributes.Count == 0)
            return null;

        var element = new XElement(WorksheetNs + "sheetView");
        ApplyNativeAttributes(element, model.NativeAttributes, ["workbookViewId"]);
        element.SetAttributeValue("workbookViewId", model.WorkbookViewId);
        return element;
    }

    private static bool IsAdditionalView(XElement element) =>
        !string.Equals(element.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal);

    private static void ReadNativeAttributes(
        XElement element,
        Dictionary<string, string> target,
        IReadOnlyCollection<string> modeledNames)
    {
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || modeledNames.Contains(attribute.Name.LocalName, StringComparer.Ordinal))
                continue;

            target[attribute.Name.ToString()] = attribute.Value;
        }
    }

    private static void ApplyNativeAttributes(
        XElement element,
        Dictionary<string, string> attributes,
        IReadOnlyCollection<string> modeledNames)
    {
        foreach (var attribute in attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key) || modeledNames.Contains(attribute.Key, StringComparer.Ordinal))
                continue;

            element.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
        }
    }
}
