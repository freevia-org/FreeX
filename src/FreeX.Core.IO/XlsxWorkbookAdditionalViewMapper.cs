using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookAdditionalViewMapper
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static WorkbookAdditionalViewsModel? Read(Stream xlsxStream)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return null;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        return Read(workbookXml);
    }

    public static WorkbookAdditionalViewsModel? Read(XDocument workbookXml)
    {
        var bookViews = workbookXml.Root?.Element(WorkbookNs + "bookViews");
        if (bookViews is null)
            return null;

        var model = new WorkbookAdditionalViewsModel
        {
            Views = bookViews.Elements(WorkbookNs + "workbookView")
                .Skip(1)
                .Select(ReadView)
                .ToList()
        };
        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(bookViews, model.NativeAttributes, []);

        return model.NativeAttributes.Count == 0 && model.Views.Count == 0
            ? null
            : model;
    }

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        if (!ApplyToWorkbookXml(workbookXml, workbook))
            return;

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
    }

    public static bool ApplyToWorkbookXml(XDocument workbookXml, Workbook workbook)
    {
        if (workbook.AdditionalViews is null)
            return false;

        var root = workbookXml.Root;
        if (root is null)
            return false;

        var bookViews = root.Element(WorkbookNs + "bookViews");
        if (bookViews is null)
        {
            bookViews = new XElement(WorkbookNs + "bookViews");
            var sheets = root.Element(WorkbookNs + "sheets");
            if (sheets is null)
                root.Add(bookViews);
            else
                sheets.AddBeforeSelf(bookViews);
        }

        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(bookViews, workbook.AdditionalViews.NativeAttributes, []);
        foreach (var view in bookViews.Elements(WorkbookNs + "workbookView").Skip(1).ToList())
            view.Remove();

        foreach (var view in workbook.AdditionalViews.Views.Select(ToXml).OfType<XElement>())
        {
            ClampToCurrentSheetCount(view, workbook);
            bookViews.Add(view);
        }

        return true;
    }

    // Excel writes one additional <workbookView> per open secondary window (View > New Window),
    // each with its own activeTab/firstSheet index into the workbook's sheet-tab order. FreeX only
    // models the PRIMARY view's activeTab/firstSheet on Workbook.ActiveSheetIndex/
    // FirstVisibleSheetIndex (reconciled every save via
    // XlsxWorkbookMetadataXmlHelper.ClampToVisibleSheetIndex); every other workbookView is preserved
    // as an opaque blob and would otherwise be re-emitted with its load-time activeTab/firstSheet
    // unchanged. Once the user inserts/deletes/reorders sheets, that stale index can go out of range
    // for the new sheet count or land on a completely different sheet after a reorder. Clamp it the
    // same way the primary view is clamped before writing the preserved element back out.
    private static void ClampToCurrentSheetCount(XElement view, Workbook workbook)
    {
        ClampIndexAttribute(view, "activeTab", workbook);
        ClampIndexAttribute(view, "firstSheet", workbook);
    }

    private static void ClampIndexAttribute(XElement view, string attributeName, Workbook workbook)
    {
        var attribute = view.Attribute(attributeName);
        if (attribute is null)
            return;

        if (!int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return;

        var clamped = XlsxWorkbookMetadataXmlHelper.ClampToVisibleSheetIndex(workbook, value)
            ?? 0;
        attribute.Value = clamped.ToString(CultureInfo.InvariantCulture);
    }

    private static WorkbookAdditionalViewModel ReadView(XElement element)
    {
        var model = new WorkbookAdditionalViewModel
        {
            NativeXml = element.ToString(SaveOptions.DisableFormatting)
        };
        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(element, model.NativeAttributes, []);
        return model;
    }

    private static XElement? ToXml(WorkbookAdditionalViewModel model)
    {
        if (TryCreateNativeWorkbookView(model.NativeXml) is { } nativeElement)
            return nativeElement;

        if (model.NativeAttributes.Count == 0)
            return null;

        var element = new XElement(WorkbookNs + "workbookView");
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(element, model.NativeAttributes, []);
        XlsxXmlPreservationPolicy.RemoveOfficeRevisionAttributes(element);
        XlsxWorkbookViewNormalizer.NormalizeWorkbookViewElement(element);
        return element;
    }

    private static XElement? TryCreateNativeWorkbookView(string? nativeXml)
    {
        if (string.IsNullOrWhiteSpace(nativeXml))
            return null;

        try
        {
            var nativeElement = XElement.Parse(nativeXml);
            if (nativeElement.Name != WorkbookNs + "workbookView")
                return null;

            XlsxXmlPreservationPolicy.RemoveOfficeRevisionAttributes(nativeElement);
            XlsxWorkbookViewNormalizer.NormalizeWorkbookViewElement(nativeElement);
            return nativeElement;
        }
        catch
        {
            return null;
        }
    }

}
