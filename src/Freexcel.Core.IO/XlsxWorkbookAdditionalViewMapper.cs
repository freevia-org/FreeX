using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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
        ReadNativeAttributes(bookViews, model.NativeAttributes);

        return model.NativeAttributes.Count == 0 && model.Views.Count == 0
            ? null
            : model;
    }

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        if (workbook.AdditionalViews is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var root = workbookXml.Root;
        if (root is null)
            return;

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

        ApplyNativeAttributes(bookViews, workbook.AdditionalViews.NativeAttributes);
        foreach (var view in bookViews.Elements(WorkbookNs + "workbookView").Skip(1).ToList())
            view.Remove();

        foreach (var view in workbook.AdditionalViews.Views.Select(ToXml).OfType<XElement>())
            bookViews.Add(view);

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static WorkbookAdditionalViewModel ReadView(XElement element)
    {
        var model = new WorkbookAdditionalViewModel
        {
            NativeXml = element.ToString(SaveOptions.DisableFormatting)
        };
        ReadNativeAttributes(element, model.NativeAttributes);
        return model;
    }

    private static XElement? ToXml(WorkbookAdditionalViewModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.NativeXml))
        {
            try
            {
                var nativeElement = XElement.Parse(model.NativeXml);
                if (nativeElement.Name == WorkbookNs + "workbookView")
                    return nativeElement;
            }
            catch
            {
                // Fall back to native attributes below.
            }
        }

        if (model.NativeAttributes.Count == 0)
            return null;

        var element = new XElement(WorkbookNs + "workbookView");
        ApplyNativeAttributes(element, model.NativeAttributes);
        return element;
    }

    private static void ReadNativeAttributes(XElement element, Dictionary<string, string> target)
    {
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            target[attribute.Name.ToString()] = attribute.Value;
        }
    }

    private static void ApplyNativeAttributes(XElement element, Dictionary<string, string> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key))
                continue;

            if (TryGetAttributeName(attribute.Key) is { } attributeName)
                element.SetAttributeValue(attributeName, attribute.Value);
        }
    }

    private static XName? TryGetAttributeName(string key)
    {
        try
        {
            return XName.Get(key);
        }
        catch
        {
            return null;
        }
    }
}
