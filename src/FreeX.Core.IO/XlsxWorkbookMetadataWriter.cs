using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookMetadataWriter
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static bool HasPostProcessingMetadata(Workbook workbook) =>
        workbook.Uses1904DateSystem ||
        workbook.Properties is not null ||
        workbook.ShowSheetTabs is not null ||
        workbook.SheetTabRatio is not null ||
        workbook.FirstVisibleSheetIndex is not null ||
        workbook.ActiveSheetIndex is not null ||
        workbook.AdditionalViews is not null ||
        workbook.FileVersion is not null ||
        workbook.FunctionGroups is not null ||
        workbook.SmartTags is not null ||
        workbook.FileSharing is not null ||
        workbook.FileRecoveryProperties.Count > 0 ||
        workbook.IsStructureProtected ||
        workbook.ProtectionMetadata is not null ||
        HasCalculationProperties(workbook);

    public static void SavePostProcessingMetadata(Stream xlsxStream, Workbook workbook) =>
        SaveWorkbookXml(xlsxStream, workbook, static (workbookXml, root, model) =>
        {
            ApplyWorkbookProperties(root, model);
            ApplyWorkbookViewProperties(root, model);
            XlsxWorkbookAdditionalViewMapper.ApplyToWorkbookXml(workbookXml, model);

            if (model.FileVersion is not null)
                ApplyFileVersion(root, model);
            if (model.FunctionGroups is not null)
                ApplyFunctionGroups(root, model);
            if (model.SmartTags is not null)
                ApplySmartTags(root, model);
            if (model.FileSharing is not null)
                ApplyFileSharing(root, model);
            if (model.FileRecoveryProperties.Count > 0)
                ApplyFileRecoveryProperties(root, model);
            if (model.IsStructureProtected || model.ProtectionMetadata is not null)
                ApplyProtection(root, model);

            ApplyCalculationProperties(root, model);
            return true;
        });

    public static void SaveSourcePackageReplayMetadata(Stream xlsxStream, Workbook workbook) =>
        SaveWorkbookXml(xlsxStream, workbook, static (workbookXml, root, model) =>
        {
            var changed = XlsxWorkbookAdditionalViewMapper.ApplyToWorkbookXml(workbookXml, model);

            if (model.FileVersion is not null)
                changed |= ApplyFileVersion(root, model);
            if (model.FunctionGroups is not null)
                changed |= ApplyFunctionGroups(root, model);
            if (model.SmartTags is not null)
                changed |= ApplySmartTags(root, model);
            if (model.FileSharing is not null)
                changed |= ApplyFileSharing(root, model);
            if (model.FileRecoveryProperties.Count > 0)
                changed |= ApplyFileRecoveryProperties(root, model);

            return changed;
        });

    public static bool HasSourcePackageReplayMetadata(Workbook workbook) =>
        workbook.AdditionalViews is not null ||
        workbook.FileVersion is not null ||
        workbook.FunctionGroups is not null ||
        workbook.SmartTags is not null ||
        workbook.FileSharing is not null ||
        workbook.FileRecoveryProperties.Count > 0;

    private static bool HasCalculationProperties(Workbook workbook) =>
        workbook.CalculationMode != WorkbookCalculationMode.Automatic ||
        workbook.FullCalculationOnLoad ||
        workbook.ForceFullCalculation ||
        workbook.IterativeCalculation ||
        workbook.MaxCalculationIterations is not null ||
        workbook.MaxCalculationChange is not null;

    public static void SaveWorkbookProperties(Stream xlsxStream, Workbook workbook)
    {
        SaveWorkbookXml(xlsxStream, workbook, static (_, root, model) => ApplyWorkbookProperties(root, model));
    }

    private static bool ApplyWorkbookProperties(XElement root, Workbook workbook)
    {
        var workbookProperties = root.Element(WorkbookNs + "workbookPr");
        if (workbookProperties is null)
        {
            if (!workbook.Uses1904DateSystem && workbook.Properties is null)
                return false;

            workbookProperties = new XElement(WorkbookNs + "workbookPr");
            root.AddFirst(workbookProperties);
        }

        if (workbook.Properties is not null)
        {
            XmlNativeBagSerializer.ApplyToElement(workbookProperties, workbook.Properties.Get("workbookPr"), ["date1904"]);
        }

        workbookProperties.SetAttributeValue("date1904", workbook.Uses1904DateSystem ? "1" : null);
        return true;
    }

    public static void SaveWorkbookViewProperties(Stream xlsxStream, Workbook workbook)
    {
        SaveWorkbookXml(xlsxStream, workbook, static (_, root, model) => ApplyWorkbookViewProperties(root, model));
    }

    private static bool ApplyWorkbookViewProperties(XElement root, Workbook workbook)
    {
        if (workbook.ShowSheetTabs is null &&
            workbook.SheetTabRatio is null &&
            workbook.FirstVisibleSheetIndex is null &&
            workbook.ActiveSheetIndex is null)
        {
            return false;
        }

        var bookViews = root.Element(WorkbookNs + "bookViews");
        if (bookViews is null)
        {
            bookViews = new XElement(WorkbookNs + "bookViews");
            var sheets = root.Element(WorkbookNs + "sheets");
            if (sheets is not null)
                sheets.AddBeforeSelf(bookViews);
            else
                root.Add(bookViews);
        }

        var primaryView = bookViews.Elements(WorkbookNs + "workbookView").FirstOrDefault()
            ?? new XElement(WorkbookNs + "workbookView");
        if (primaryView.Parent is null)
            bookViews.AddFirst(primaryView);

        primaryView.SetAttributeValue("showSheetTabs", workbook.ShowSheetTabs is { } showSheetTabs ? showSheetTabs ? "1" : "0" : null);
        primaryView.SetAttributeValue("tabRatio", XlsxWorkbookMetadataXmlHelper.ClampWorkbookViewInteger(workbook.SheetTabRatio, 0, 1000));
        primaryView.SetAttributeValue("firstSheet", XlsxWorkbookMetadataXmlHelper.ClampWorkbookViewInteger(workbook.FirstVisibleSheetIndex, 0, Math.Max(0, workbook.Sheets.Count - 1)));
        primaryView.SetAttributeValue("activeTab", XlsxWorkbookMetadataXmlHelper.ClampWorkbookViewInteger(workbook.ActiveSheetIndex, 0, Math.Max(0, workbook.Sheets.Count - 1)));

        return true;
    }

    public static void SaveFileSharing(Stream xlsxStream, Workbook workbook)
    {
        SaveWorkbookXml(xlsxStream, workbook, static (_, root, model) => ApplyFileSharing(root, model));
    }

    private static bool ApplyFileSharing(XElement root, Workbook workbook)
    {
        var existingFileSharing = root.Element(WorkbookNs + "fileSharing");
        var fileSharing = existingFileSharing is not null
            ? new XElement(existingFileSharing)
            : new XElement(WorkbookNs + "fileSharing");
        existingFileSharing?.Remove();
        if (workbook.FileSharing is null)
        {
            return true;
        }

        fileSharing.Attribute("readOnlyRecommended")?.Remove();
        fileSharing.Attribute("userName")?.Remove();
        fileSharing.Attribute("reservationPassword")?.Remove();
        fileSharing.SetAttributeValue(
            "readOnlyRecommended",
            workbook.FileSharing.ReadOnlyRecommended is { } readOnlyRecommended ? readOnlyRecommended ? "1" : "0" : null);
        fileSharing.SetAttributeValue(
            "userName",
            string.IsNullOrWhiteSpace(workbook.FileSharing.UserName) ? null : workbook.FileSharing.UserName);
        fileSharing.SetAttributeValue(
            "reservationPassword",
            string.IsNullOrWhiteSpace(workbook.FileSharing.ReservationPassword) ? null : workbook.FileSharing.ReservationPassword);

        var workbookProtection = root.Element(WorkbookNs + "workbookProtection");
        if (workbookProtection is not null)
            workbookProtection.AddBeforeSelf(fileSharing);
        else
        {
            var sheets = root.Element(WorkbookNs + "sheets");
            if (sheets is not null)
                sheets.AddBeforeSelf(fileSharing);
            else
                root.Add(fileSharing);
        }

        return true;
    }

    public static void SaveFileRecoveryProperties(Stream xlsxStream, Workbook workbook)
    {
        SaveWorkbookXml(xlsxStream, workbook, static (_, root, model) => ApplyFileRecoveryProperties(root, model));
    }

    private static bool ApplyFileRecoveryProperties(XElement root, Workbook workbook)
    {
        root.Elements(WorkbookNs + "fileRecoveryPr").Remove();
        if (workbook.FileRecoveryProperties.Count == 0)
        {
            return true;
        }

        var recoveryElements = workbook.FileRecoveryProperties.Select(item =>
        {
            var element = new XElement(WorkbookNs + "fileRecoveryPr");
            foreach (var attribute in item.NativeAttributes)
            {
                if (!string.IsNullOrWhiteSpace(attribute.Key) &&
                    attribute.Key is not "autoRecover" and not "crashSave" and not "dataExtractLoad" and not "repairLoad")
                {
                    XlsxWorkbookMetadataXmlHelper.TrySetNativeAttribute(element, attribute.Key, attribute.Value);
                }
            }

            SetBooleanAttribute(element, "autoRecover", item.AutoRecover);
            SetBooleanAttribute(element, "crashSave", item.CrashSave);
            SetBooleanAttribute(element, "dataExtractLoad", item.DataExtractLoad);
            SetBooleanAttribute(element, "repairLoad", item.RepairLoad);
            return element;
        }).ToArray();

        var webPublishObjects = root.Element(WorkbookNs + "webPublishObjects");
        if (webPublishObjects is not null)
            webPublishObjects.AddBeforeSelf(recoveryElements);
        else if (root.Element(WorkbookNs + "extLst") is { } extensionList)
            extensionList.AddBeforeSelf(recoveryElements);
        else
            root.Add(recoveryElements);

        return true;

        static void SetBooleanAttribute(XElement element, string name, bool? value) =>
            element.SetAttributeValue(name, value is { } boolValue ? boolValue ? "1" : "0" : null);
    }

    public static void SaveFileVersion(Stream xlsxStream, Workbook workbook)
    {
        SaveWorkbookXml(xlsxStream, workbook, static (_, root, model) => ApplyFileVersion(root, model));
    }

    private static bool ApplyFileVersion(XElement root, Workbook workbook)
    {
        root.Element(WorkbookNs + "fileVersion")?.Remove();
        if (workbook.FileVersion is null)
        {
            return true;
        }

        var fileVersion = new XElement(WorkbookNs + "fileVersion");
        foreach (var attribute in workbook.FileVersion.NativeAttributes)
        {
            if (!string.IsNullOrWhiteSpace(attribute.Key) &&
                attribute.Key is not "appName" and not "lastEdited" and not "lowestEdited" and not "rupBuild" and not "codeName")
            {
                XlsxWorkbookMetadataXmlHelper.TrySetNativeAttribute(fileVersion, attribute.Key, attribute.Value);
            }
        }

        fileVersion.SetAttributeValue("appName", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.FileVersion.AppName));
        fileVersion.SetAttributeValue("lastEdited", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.FileVersion.LastEdited));
        fileVersion.SetAttributeValue("lowestEdited", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.FileVersion.LowestEdited));
        fileVersion.SetAttributeValue("rupBuild", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.FileVersion.RupBuild));
        fileVersion.SetAttributeValue("codeName", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.FileVersion.CodeName));

        root.AddFirst(fileVersion);
        return true;
    }

    public static void SaveFunctionGroups(Stream xlsxStream, Workbook workbook)
    {
        SaveWorkbookXml(xlsxStream, workbook, static (_, root, model) => ApplyFunctionGroups(root, model));
    }

    private static bool ApplyFunctionGroups(XElement root, Workbook workbook)
    {
        root.Element(WorkbookNs + "functionGroups")?.Remove();
        if (workbook.FunctionGroups is null)
        {
            return true;
        }

        var functionGroups = new XElement(WorkbookNs + "functionGroups");
        foreach (var attribute in workbook.FunctionGroups.NativeAttributes)
        {
            if (!string.IsNullOrWhiteSpace(attribute.Key) && attribute.Key != "builtInGroupCount")
                XlsxWorkbookMetadataXmlHelper.TrySetNativeAttribute(functionGroups, attribute.Key, attribute.Value);
        }

        functionGroups.SetAttributeValue("builtInGroupCount", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.FunctionGroups.BuiltInGroupCount));
        foreach (var group in workbook.FunctionGroups.Groups)
        {
            var element = new XElement(WorkbookNs + "functionGroup");
            foreach (var attribute in group.NativeAttributes)
            {
                if (!string.IsNullOrWhiteSpace(attribute.Key) && attribute.Key != "name")
                    XlsxWorkbookMetadataXmlHelper.TrySetNativeAttribute(element, attribute.Key, attribute.Value);
            }

            element.SetAttributeValue("name", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(group.Name));
            functionGroups.Add(element);
        }

        var oleSize = root.Element(WorkbookNs + "oleSize");
        if (oleSize is not null)
            oleSize.AddBeforeSelf(functionGroups);
        else if (root.Element(WorkbookNs + "extLst") is { } extensionList)
            extensionList.AddBeforeSelf(functionGroups);
        else
            root.Add(functionGroups);

        return true;
    }

    public static void SaveSmartTags(Stream xlsxStream, Workbook workbook)
    {
        SaveWorkbookXml(xlsxStream, workbook, static (_, root, model) => ApplySmartTags(root, model));
    }

    private static bool ApplySmartTags(XElement root, Workbook workbook)
    {
        root.Element(WorkbookNs + "smartTagPr")?.Remove();
        root.Element(WorkbookNs + "smartTagTypes")?.Remove();
        if (workbook.SmartTags is null)
        {
            return true;
        }

        var smartTagProperties = new XElement(WorkbookNs + "smartTagPr");
        foreach (var attribute in workbook.SmartTags.PropertiesNativeAttributes)
        {
            if (!string.IsNullOrWhiteSpace(attribute.Key) && attribute.Key is not "embed" and not "show")
                XlsxWorkbookMetadataXmlHelper.TrySetNativeAttribute(smartTagProperties, attribute.Key, attribute.Value);
        }

        smartTagProperties.SetAttributeValue("embed", workbook.SmartTags.Embed is { } embed ? embed ? "1" : "0" : null);
        smartTagProperties.SetAttributeValue("show", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.SmartTags.Show));

        var smartTagTypes = new XElement(WorkbookNs + "smartTagTypes");
        foreach (var attribute in workbook.SmartTags.TypesNativeAttributes)
        {
            if (!string.IsNullOrWhiteSpace(attribute.Key))
                XlsxWorkbookMetadataXmlHelper.TrySetNativeAttribute(smartTagTypes, attribute.Key, attribute.Value);
        }

        foreach (var type in workbook.SmartTags.Types)
        {
            var element = new XElement(WorkbookNs + "smartTagType");
            foreach (var attribute in type.NativeAttributes)
            {
                if (!string.IsNullOrWhiteSpace(attribute.Key) &&
                    attribute.Key is not "namespaceUri" and not "name" and not "url")
                {
                    XlsxWorkbookMetadataXmlHelper.TrySetNativeAttribute(element, attribute.Key, attribute.Value);
                }
            }

            element.SetAttributeValue("namespaceUri", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(type.NamespaceUri));
            element.SetAttributeValue("name", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(type.Name));
            element.SetAttributeValue("url", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(type.Url));
            smartTagTypes.Add(element);
        }

        var extensionList = root.Element(WorkbookNs + "extLst");
        if (extensionList is not null)
            extensionList.AddBeforeSelf(smartTagProperties, smartTagTypes);
        else
            root.Add(smartTagProperties, smartTagTypes);

        return true;
    }

    public static void SaveProtection(Stream xlsxStream, Workbook workbook)
    {
        SaveWorkbookXml(xlsxStream, workbook, static (_, root, model) => ApplyProtection(root, model));
    }

    private static bool ApplyProtection(XElement root, Workbook workbook)
    {
        root.Element(WorkbookNs + "workbookProtection")?.Remove();
        if (!workbook.IsStructureProtected &&
            string.IsNullOrWhiteSpace(workbook.StructureProtectionPassword) &&
            workbook.ProtectionMetadata is null)
        {
            return true;
        }

        var protection = new XElement(WorkbookNs + "workbookProtection");
        if (workbook.ProtectionMetadata is not null)
        {
            XmlNativeBagSerializer.ApplyToElement(protection, workbook.ProtectionMetadata.Get("workbookProtection"),
                ["lockStructure", "workbookPassword"]);
        }

        if (workbook.IsStructureProtected)
            protection.SetAttributeValue("lockStructure", "1");
        if (!string.IsNullOrWhiteSpace(workbook.StructureProtectionPassword))
            protection.SetAttributeValue("workbookPassword", XlsxWorkbookMetadataXmlHelper.ToLegacyPasswordHash(workbook.StructureProtectionPassword));

        var sheets = root.Element(WorkbookNs + "sheets");
        if (sheets is not null)
            sheets.AddBeforeSelf(protection);
        else
            root.Add(protection);

        return true;
    }

    public static void SaveCalculationProperties(Stream xlsxStream, Workbook workbook)
    {
        SaveWorkbookXml(xlsxStream, workbook, static (_, root, model) => ApplyCalculationProperties(root, model));
    }

    private static bool ApplyCalculationProperties(XElement root, Workbook workbook)
    {
        var calcPr = root.Element(WorkbookNs + "calcPr");
        if (calcPr is null)
        {
            calcPr = new XElement(WorkbookNs + "calcPr");
            root.Add(calcPr);
        }

        calcPr.SetAttributeValue("calcMode", workbook.CalculationMode == WorkbookCalculationMode.Manual ? "manual" : "auto");
        SetBooleanAttribute(calcPr, "fullCalcOnLoad", workbook.FullCalculationOnLoad);
        SetBooleanAttribute(calcPr, "forceFullCalc", workbook.ForceFullCalculation);
        SetBooleanAttribute(calcPr, "iterate", workbook.IterativeCalculation);
        calcPr.SetAttributeValue(
            "iterateCount",
            workbook.MaxCalculationIterations is { } maxIterations ? maxIterations.ToString(CultureInfo.InvariantCulture) : null);
        calcPr.SetAttributeValue(
            "iterateDelta",
            workbook.MaxCalculationChange is { } maxChange ? maxChange.ToString(CultureInfo.InvariantCulture) : null);

        return true;

        static void SetBooleanAttribute(XElement element, string name, bool value) =>
            element.SetAttributeValue(name, value ? "1" : null);
    }

    private static void SaveWorkbookXml(Stream xlsxStream, Workbook workbook, Func<XDocument, XElement, Workbook, bool> apply)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var root = workbookXml.Root;
        if (root is null || !apply(workbookXml, root, workbook))
            return;

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
    }
}
