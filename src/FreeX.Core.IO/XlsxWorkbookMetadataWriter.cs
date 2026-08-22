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
        !workbook.ShowInkAnnotations ||
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
                ApplyFileRecoveryProperties(root, model, preserveRepairLoad: false);
            if (model.IsStructureProtected || model.ProtectionMetadata is not null)
                ApplyProtection(root, model);

            ApplyCalculationProperties(root, model);
            return true;
        });

    public static void SaveSourcePackageReplayMetadata(Stream xlsxStream, Workbook workbook) =>
        SaveWorkbookXml(xlsxStream, workbook, static (workbookXml, root, model) =>
        {
            var changed = ApplyWorkbookProperties(root, model);
            changed |= XlsxWorkbookAdditionalViewMapper.ApplyToWorkbookXml(workbookXml, model);

            if (model.FileVersion is not null)
                changed |= ApplyFileVersion(root, model);
            if (model.FunctionGroups is not null)
                changed |= ApplyFunctionGroups(root, model);
            if (model.SmartTags is not null)
                changed |= ApplySmartTags(root, model);
            if (model.FileSharing is not null)
                changed |= ApplyFileSharing(root, model);
            if (model.FileRecoveryProperties.Count > 0)
                changed |= ApplyFileRecoveryProperties(root, model, preserveRepairLoad: true);

            return changed;
        });

    public static bool HasSourcePackageReplayMetadata(Workbook workbook) =>
        workbook.Properties is not null ||
        !workbook.ShowInkAnnotations ||
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
        workbook.MaxCalculationChange is not null ||
        !workbook.FullPrecision;

    public static void SaveWorkbookProperties(Stream xlsxStream, Workbook workbook)
    {
        SaveWorkbookXml(xlsxStream, workbook, static (_, root, model) => ApplyWorkbookProperties(root, model));
    }

    private static bool ApplyWorkbookProperties(XElement root, Workbook workbook)
    {
        var workbookProperties = root.Element(WorkbookNs + "workbookPr");
        if (workbookProperties is null)
        {
            if (!workbook.Uses1904DateSystem && workbook.ShowInkAnnotations && workbook.Properties is null)
                return false;

            workbookProperties = new XElement(WorkbookNs + "workbookPr");
            root.AddFirst(workbookProperties);
        }

        if (workbook.Properties is not null)
        {
            XmlNativeBagSerializer.ApplyToElement(workbookProperties, workbook.Properties.Get("workbookPr"), ["date1904", "showInkAnnotation"]);
        }

        workbookProperties.SetAttributeValue("date1904", workbook.Uses1904DateSystem ? "1" : null);
        workbookProperties.SetAttributeValue("showInkAnnotation", workbook.ShowInkAnnotations ? null : "0");
        XlsxWorkbookLeafElementNormalizer.Normalize(workbookProperties);
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

        var primaryView = FindFirstWorkbookView(bookViews) ?? new XElement(WorkbookNs + "workbookView");
        if (primaryView.Parent is null)
            bookViews.AddFirst(primaryView);

        primaryView.SetAttributeValue("showSheetTabs", workbook.ShowSheetTabs is { } showSheetTabs ? showSheetTabs ? "1" : "0" : null);
        primaryView.SetAttributeValue("tabRatio", XlsxWorkbookMetadataXmlHelper.ClampWorkbookViewInteger(workbook.SheetTabRatio, 0, 1000));
        primaryView.SetAttributeValue("firstSheet", XlsxWorkbookMetadataXmlHelper.ClampToVisibleSheetIndex(workbook, workbook.FirstVisibleSheetIndex));
        primaryView.SetAttributeValue("activeTab", XlsxWorkbookMetadataXmlHelper.ClampToVisibleSheetIndex(workbook, workbook.ActiveSheetIndex));
        XlsxWorkbookViewNormalizer.NormalizeWorkbookViewElement(primaryView);

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
        XlsxWorkbookLeafElementNormalizer.Normalize(fileSharing);

        InsertFileSharingInOrder(root, fileSharing);

        return true;
    }

    public static void SaveFileRecoveryProperties(Stream xlsxStream, Workbook workbook)
    {
        SaveWorkbookXml(xlsxStream, workbook, static (_, root, model) => ApplyFileRecoveryProperties(root, model, preserveRepairLoad: false));
    }

    private static bool ApplyFileRecoveryProperties(XElement root, Workbook workbook, bool preserveRepairLoad)
    {
        root.Elements(WorkbookNs + "fileRecoveryPr").Remove();
        if (workbook.FileRecoveryProperties.Count == 0)
        {
            return true;
        }

        var recoveryElements = workbook.FileRecoveryProperties.Select(item =>
        {
            var element = new XElement(WorkbookNs + "fileRecoveryPr");
            XlsxWorkbookMetadataXmlHelper.ApplyNativeAttributes(
                element,
                item.NativeAttributes,
                "autoRecover",
                "crashSave",
                "dataExtractLoad");
            if (preserveRepairLoad)
                XlsxWorkbookMetadataXmlHelper.ApplyNativeAttributes(element, item.NativeAttributes, "repairLoad");

            SetBooleanAttribute(element, "autoRecover", item.AutoRecover);
            SetBooleanAttribute(element, "crashSave", item.CrashSave);
            SetBooleanAttribute(element, "dataExtractLoad", item.DataExtractLoad);
            SetBooleanAttribute(
                element,
                "repairLoad",
                preserveRepairLoad || item.RepairLoad != true ? item.RepairLoad : null);
            XlsxWorkbookLeafElementNormalizer.Normalize(element);
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
        XlsxWorkbookMetadataXmlHelper.ApplyNativeAttributes(
            fileVersion,
            workbook.FileVersion.NativeAttributes,
            "appName",
            "lastEdited",
            "lowestEdited",
            "rupBuild",
            "codeName");

        fileVersion.SetAttributeValue("appName", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.FileVersion.AppName));
        fileVersion.SetAttributeValue("lastEdited", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.FileVersion.LastEdited));
        fileVersion.SetAttributeValue("lowestEdited", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.FileVersion.LowestEdited));
        fileVersion.SetAttributeValue("rupBuild", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.FileVersion.RupBuild));
        fileVersion.SetAttributeValue("codeName", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.FileVersion.CodeName));
        XlsxWorkbookLeafElementNormalizer.Normalize(fileVersion);

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
        XlsxWorkbookMetadataXmlHelper.ApplyNativeAttributes(
            functionGroups,
            workbook.FunctionGroups.NativeAttributes,
            "builtInGroupCount");

        functionGroups.SetAttributeValue("builtInGroupCount", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.FunctionGroups.BuiltInGroupCount));
        foreach (var group in workbook.FunctionGroups.Groups)
        {
            var element = new XElement(WorkbookNs + "functionGroup");
            XlsxWorkbookMetadataXmlHelper.ApplyNativeAttributes(element, group.NativeAttributes, "name");

            element.SetAttributeValue("name", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(group.Name));
            functionGroups.Add(element);
        }

        XlsxWorkbookFunctionGroupsNormalizer.NormalizeElement(functionGroups);

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
        XlsxWorkbookMetadataXmlHelper.ApplyNativeAttributes(
            smartTagProperties,
            workbook.SmartTags.PropertiesNativeAttributes,
            "embed",
            "show");

        smartTagProperties.SetAttributeValue("embed", workbook.SmartTags.Embed is { } embed ? embed ? "1" : "0" : null);
        smartTagProperties.SetAttributeValue("show", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(workbook.SmartTags.Show));

        var smartTagTypes = new XElement(WorkbookNs + "smartTagTypes");
        XlsxWorkbookMetadataXmlHelper.ApplyNativeAttributes(smartTagTypes, workbook.SmartTags.TypesNativeAttributes);

        foreach (var type in workbook.SmartTags.Types)
        {
            var element = new XElement(WorkbookNs + "smartTagType");
            XlsxWorkbookMetadataXmlHelper.ApplyNativeAttributes(
                element,
                type.NativeAttributes,
                "namespaceUri",
                "name",
                "url");

            element.SetAttributeValue("namespaceUri", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(type.NamespaceUri));
            element.SetAttributeValue("name", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(type.Name));
            element.SetAttributeValue("url", XlsxWorkbookMetadataXmlHelper.NullIfWhiteSpace(type.Url));
            smartTagTypes.Add(element);
        }

        XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagPropertiesElement(smartTagProperties);
        XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagTypesElement(smartTagTypes);

        var extensionList = root.Element(WorkbookNs + "extLst");
        XElement[] elements = XlsxWorkbookSmartTagNormalizer.ShouldRemoveSmartTagTypesElement(smartTagTypes)
            ? [smartTagProperties]
            : [smartTagProperties, smartTagTypes];
        if (extensionList is not null)
            extensionList.AddBeforeSelf(elements);
        else
            root.Add(elements);

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

        // StructureProtectionPassword is sometimes only the encoded mirror of a modern ISO 29500
        // hash already preserved verbatim via ProtectionMetadata above (see
        // ProtectionPasswordHelper.EncodeIso29500Hash) — there is no real legacy password in that
        // case to (re-)derive a workbookPassword hash from, so leave whatever legacy attribute the
        // metadata bag carried (if any) untouched.
        if (!string.IsNullOrWhiteSpace(workbook.StructureProtectionPassword) &&
            !ProtectionPasswordHelper.IsIso29500Hash(workbook.StructureProtectionPassword))
        {
            protection.SetAttributeValue("workbookPassword", ProtectionPasswordHelper.ToLegacyPasswordHash(workbook.StructureProtectionPassword));
        }

        XlsxWorkbookLeafElementNormalizer.Normalize(protection);

        InsertWorkbookProtectionInOrder(root, protection);

        return true;
    }

    private static void InsertFileSharingInOrder(XElement root, XElement fileSharing)
    {
        string[] laterWorkbookElements =
        [
            "workbookPr",
            "workbookProtection",
            "bookViews",
            "sheets"
        ];

        var insertionPoint = FindFirstWorkbookChild(root, laterWorkbookElements);
        if (insertionPoint is null)
            root.Add(fileSharing);
        else
            insertionPoint.AddBeforeSelf(fileSharing);
    }

    private static void InsertWorkbookProtectionInOrder(XElement root, XElement protection)
    {
        string[] laterWorkbookElements =
        [
            "bookViews",
            "sheets"
        ];

        var insertionPoint = FindFirstWorkbookChild(root, laterWorkbookElements);
        if (insertionPoint is null)
            root.Add(protection);
        else
            insertionPoint.AddBeforeSelf(protection);
    }

    private static XElement? FindFirstWorkbookView(XElement bookViews)
    {
        foreach (var element in bookViews.Elements(WorkbookNs + "workbookView"))
            return element;

        return null;
    }

    private static XElement? FindFirstWorkbookChild(XElement root, string[] localNames)
    {
        foreach (var element in root.Elements())
        {
            if (element.Name.Namespace != WorkbookNs)
                continue;

            foreach (var localName in localNames)
            {
                if (string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal))
                    return element;
            }
        }

        return null;
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

        calcPr.SetAttributeValue("calcMode", workbook.CalculationMode switch
        {
            WorkbookCalculationMode.Manual => "manual",
            WorkbookCalculationMode.AutomaticExceptDataTables => "autoNoTable",
            _ => "auto"
        });
        SetBooleanAttribute(calcPr, "fullCalcOnLoad", workbook.FullCalculationOnLoad);
        SetBooleanAttribute(calcPr, "forceFullCalc", workbook.ForceFullCalculation);
        SetBooleanAttribute(calcPr, "iterate", workbook.IterativeCalculation);
        calcPr.SetAttributeValue(
            "iterateCount",
            workbook.MaxCalculationIterations is { } maxIterations ? maxIterations.ToString(CultureInfo.InvariantCulture) : null);
        calcPr.SetAttributeValue(
            "iterateDelta",
            workbook.MaxCalculationChange is { } maxChange ? maxChange.ToString(CultureInfo.InvariantCulture) : null);
        // fullPrecision defaults to true (full precision) in Excel when the attribute is absent,
        // so — mirroring the fullCalcOnLoad pattern — only write it when the workbook deviates
        // from that default (precision-as-displayed, fullPrecision="0").
        calcPr.SetAttributeValue("fullPrecision", workbook.FullPrecision ? null : "0");
        XlsxWorkbookLeafElementNormalizer.Normalize(calcPr);

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
