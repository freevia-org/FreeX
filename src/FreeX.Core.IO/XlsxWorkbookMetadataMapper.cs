using FreeX.Core.Model;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookMetadataMapper
{
    public static WorkbookFileRecoveryPropertiesModel ToFileRecoveryProperties(XElement element)
    {
        var model = new WorkbookFileRecoveryPropertiesModel
        {
            AutoRecover = XlsxXmlAttributeReader.ReadNullableBoolAttribute(element, "autoRecover"),
            CrashSave = XlsxXmlAttributeReader.ReadNullableBoolAttribute(element, "crashSave"),
            DataExtractLoad = XlsxXmlAttributeReader.ReadNullableBoolAttribute(element, "dataExtractLoad"),
            RepairLoad = XlsxXmlAttributeReader.ReadNullableBoolAttribute(element, "repairLoad")
        };

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                attribute.Name.LocalName is "autoRecover" or "crashSave" or "dataExtractLoad" or "repairLoad")
            {
                continue;
            }

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        return model;
    }

    public static WorkbookFileVersionModel ToFileVersion(XElement element)
    {
        var model = new WorkbookFileVersionModel
        {
            AppName = element.Attribute("appName")?.Value,
            LastEdited = element.Attribute("lastEdited")?.Value,
            LowestEdited = element.Attribute("lowestEdited")?.Value,
            RupBuild = element.Attribute("rupBuild")?.Value,
            CodeName = element.Attribute("codeName")?.Value
        };

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                attribute.Name.LocalName is "appName" or "lastEdited" or "lowestEdited" or "rupBuild" or "codeName")
            {
                continue;
            }

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        return model;
    }

    public static WorkbookFunctionGroupsModel ToFunctionGroups(XElement element, XNamespace workbookNs)
    {
        var model = new WorkbookFunctionGroupsModel
        {
            BuiltInGroupCount = element.Attribute("builtInGroupCount")?.Value,
            Groups = element.Elements(workbookNs + "functionGroup")
                .Select(ToFunctionGroup)
                .ToList()
        };
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.LocalName == "builtInGroupCount")
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        return model;
    }

    public static WorkbookSmartTagMetadataModel ToSmartTags(
        XElement? smartTagProperties,
        XElement? smartTagTypes,
        XNamespace workbookNs)
    {
        var model = new WorkbookSmartTagMetadataModel
        {
            Embed = smartTagProperties is null ? null : XlsxXmlAttributeReader.ReadNullableBoolAttribute(smartTagProperties, "embed"),
            Show = smartTagProperties?.Attribute("show")?.Value,
            Types = smartTagTypes?
                .Elements(workbookNs + "smartTagType")
                .Select(ToSmartTagType)
                .ToList() ?? []
        };

        if (smartTagProperties is not null)
        {
            foreach (var attribute in smartTagProperties.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || attribute.Name.LocalName is "embed" or "show")
                    continue;

                model.PropertiesNativeAttributes[attribute.Name.ToString()] = attribute.Value;
            }
        }

        if (smartTagTypes is not null)
        {
            foreach (var attribute in smartTagTypes.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue;

                model.TypesNativeAttributes[attribute.Name.ToString()] = attribute.Value;
            }
        }

        return model;
    }

    public static XlsxWorkbookCustomView ToCustomView(XElement view)
    {
        var id = view.Attribute("guid")?.Value;
        var name = view.Attribute("name")?.Value;
        return new XlsxWorkbookCustomView(
            id ?? "",
            name ?? "",
            XlsxXmlAttributeReader.ReadBoolAttribute(view, "includePrintSettings", defaultValue: true),
            XlsxXmlAttributeReader.ReadBoolAttribute(view, "includeHiddenRowCol", defaultValue: true));
    }

    private static WorkbookFunctionGroupModel ToFunctionGroup(XElement element)
    {
        var model = new WorkbookFunctionGroupModel
        {
            Name = element.Attribute("name")?.Value
        };
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.LocalName == "name")
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        return model;
    }

    private static WorkbookSmartTagTypeModel ToSmartTagType(XElement element)
    {
        var model = new WorkbookSmartTagTypeModel
        {
            NamespaceUri = element.Attribute("namespaceUri")?.Value,
            Name = element.Attribute("name")?.Value,
            Url = element.Attribute("url")?.Value
        };

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                attribute.Name.LocalName is "namespaceUri" or "name" or "url")
            {
                continue;
            }

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        return model;
    }
}
