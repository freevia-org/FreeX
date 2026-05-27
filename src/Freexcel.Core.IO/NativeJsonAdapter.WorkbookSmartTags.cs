using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorkbookSmartTagMetadataModel? ToWorkbookSmartTags(WorkbookSmartTagMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var propertiesNativeAttributes = CleanNativeAttributes(dto.PropertiesNativeAttributes);
        var typesNativeAttributes = CleanNativeAttributes(dto.TypesNativeAttributes);
        var show = string.IsNullOrWhiteSpace(dto.Show) ? null : dto.Show;
        var types = (dto.Types ?? [])
            .Select(ToWorkbookSmartTagType)
            .OfType<WorkbookSmartTagTypeModel>()
            .ToList();
        if (dto.Embed is null &&
            show is null &&
            propertiesNativeAttributes.Count == 0 &&
            typesNativeAttributes.Count == 0 &&
            types.Count == 0)
        {
            return null;
        }

        return new WorkbookSmartTagMetadataModel
        {
            Embed = dto.Embed,
            Show = show,
            PropertiesNativeAttributes = propertiesNativeAttributes,
            TypesNativeAttributes = typesNativeAttributes,
            Types = types
        };
    }

    private static WorkbookSmartTagTypeModel? ToWorkbookSmartTagType(WorkbookSmartTagTypeDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var namespaceUri = string.IsNullOrWhiteSpace(dto.NamespaceUri) ? null : dto.NamespaceUri;
        var name = string.IsNullOrWhiteSpace(dto.Name) ? null : dto.Name;
        var url = string.IsNullOrWhiteSpace(dto.Url) ? null : dto.Url;
        if (namespaceUri is null && name is null && url is null && nativeAttributes.Count == 0)
            return null;

        return new WorkbookSmartTagTypeModel
        {
            NamespaceUri = namespaceUri,
            Name = name,
            Url = url,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorkbookSmartTagMetadataDto? FromWorkbookSmartTags(WorkbookSmartTagMetadataModel? model)
    {
        if (model is null)
            return null;

        var propertiesNativeAttributes = CleanNativeAttributesForSave(model.PropertiesNativeAttributes);
        var typesNativeAttributes = CleanNativeAttributesForSave(model.TypesNativeAttributes);
        var show = string.IsNullOrWhiteSpace(model.Show) ? null : model.Show;
        var types = model.Types
            .Select(FromWorkbookSmartTagType)
            .OfType<WorkbookSmartTagTypeDto>()
            .ToList();
        if (model.Embed is null &&
            show is null &&
            propertiesNativeAttributes.Count == 0 &&
            typesNativeAttributes.Count == 0 &&
            types.Count == 0)
        {
            return null;
        }

        return new WorkbookSmartTagMetadataDto
        {
            Embed = model.Embed,
            Show = show,
            PropertiesNativeAttributes = propertiesNativeAttributes,
            TypesNativeAttributes = typesNativeAttributes,
            Types = types
        };
    }

    private static WorkbookSmartTagTypeDto? FromWorkbookSmartTagType(WorkbookSmartTagTypeModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var namespaceUri = string.IsNullOrWhiteSpace(model.NamespaceUri) ? null : model.NamespaceUri;
        var name = string.IsNullOrWhiteSpace(model.Name) ? null : model.Name;
        var url = string.IsNullOrWhiteSpace(model.Url) ? null : model.Url;
        if (namespaceUri is null && name is null && url is null && nativeAttributes.Count == 0)
            return null;

        return new WorkbookSmartTagTypeDto
        {
            NamespaceUri = namespaceUri,
            Name = name,
            Url = url,
            NativeAttributes = nativeAttributes
        };
    }
}
