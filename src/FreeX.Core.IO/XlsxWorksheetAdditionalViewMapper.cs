using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

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
        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(sheetViews, model.NativeAttributes, []);

        return model.NativeAttributes.Count == 0 && model.Views.Count == 0
            ? null
            : model;
    }

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
        => XlsxWorksheetPackageEditTraversal.Edit(
            xlsxStream,
            workbook,
            worksheetPathMap,
            SaveWorksheet);

    internal static void Save(XlsxWorksheetXmlEditSession session, Workbook workbook)
        => XlsxWorksheetPackageEditTraversal.Edit(session, workbook, SaveWorksheet);

    private static void SaveWorksheet(
        XlsxWorksheetXmlEditSession session,
        Sheet sheet,
        XlsxWorksheetXmlEdit edit)
    {
        var additionalViews = sheet.AdditionalViews;
        if (additionalViews is null)
            return;

        var root = edit.Root;

        var changed = false;
        var sheetViews = root.Element(WorksheetNs + "sheetViews");
        if (sheetViews is null)
        {
            sheetViews = new XElement(WorksheetNs + "sheetViews");
            root.AddFirst(sheetViews);
            changed = true;
        }

        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(sheetViews, additionalViews.NativeAttributes, []);
        changed |= additionalViews.NativeAttributes.Count > 0;
        foreach (var view in sheetViews.Elements(WorksheetNs + "sheetView").Where(IsAdditionalView).ToList())
        {
            view.Remove();
            changed = true;
        }

        foreach (var viewModel in additionalViews.Views)
        {
            var view = ToXml(viewModel);
            if (view is null)
                continue;

            sheetViews.Add(view);
            changed = true;
        }

        if (changed)
            session.MarkDirty(edit);
    }

    private static WorksheetAdditionalViewModel ReadView(XElement element)
    {
        var model = new WorksheetAdditionalViewModel
        {
            WorkbookViewId = element.Attribute("workbookViewId")?.Value,
            NativeXml = element.ToString(SaveOptions.DisableFormatting)
        };
        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(element, model.NativeAttributes, ["workbookViewId"]);
        return model;
    }

    private static XElement? ToXml(WorksheetAdditionalViewModel model)
    {
        if (XlsxWorksheetNativeMetadataHelpers.TryParseNativeElement(
                model.NativeXml,
                WorksheetNs + "sheetView",
                XlsxWorksheetSheetViewNormalizer.NormalizeSheetViewElement) is { } nativeElement)
        {
            return IsAdditionalView(nativeElement) ? nativeElement : null;
        }

        if (string.IsNullOrWhiteSpace(model.WorkbookViewId) && model.NativeAttributes.Count == 0)
            return null;

        var element = new XElement(WorksheetNs + "sheetView");
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(element, model.NativeAttributes, ["workbookViewId"]);
        element.SetAttributeValue("workbookViewId", model.WorkbookViewId);
        XlsxWorksheetSheetViewNormalizer.NormalizeSheetViewElement(element);
        return IsAdditionalView(element) ? element : null;
    }

    private static bool IsAdditionalView(XElement element) =>
        !string.Equals(element.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal);
}
