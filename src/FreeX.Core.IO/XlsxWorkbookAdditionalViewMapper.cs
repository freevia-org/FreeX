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
            bookViews.Add(view);

        return true;
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
        RemoveOfficeRevisionAttributes(element);
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

            RemoveOfficeRevisionAttributes(nativeElement);
            XlsxWorkbookViewNormalizer.NormalizeWorkbookViewElement(nativeElement);
            return nativeElement;
        }
        catch
        {
            return null;
        }
    }

    private static void RemoveOfficeRevisionAttributes(XElement element)
    {
        foreach (var attribute in element.Attributes().Where(IsOfficeRevisionAttribute).ToList())
            attribute.Remove();

        foreach (var namespaceAttribute in element.Attributes().Where(attribute =>
                     attribute.IsNamespaceDeclaration &&
                     IsOfficeRevisionNamespace(attribute.Value) &&
                     !element.Attributes().Any(other =>
                         !other.IsNamespaceDeclaration &&
                         other.Name.NamespaceName == attribute.Value)).ToList())
        {
            namespaceAttribute.Remove();
        }
    }

    private static bool IsOfficeRevisionAttribute(XAttribute attribute) =>
        !attribute.IsNamespaceDeclaration &&
        string.Equals(attribute.Name.LocalName, "uid", StringComparison.Ordinal) &&
        IsOfficeRevisionNamespace(attribute.Name.NamespaceName);

    private static bool IsOfficeRevisionNamespace(string namespaceName) =>
        namespaceName.StartsWith("http://schemas.microsoft.com/office/spreadsheetml/", StringComparison.Ordinal) &&
        namespaceName.Contains("/revision", StringComparison.Ordinal);

}
