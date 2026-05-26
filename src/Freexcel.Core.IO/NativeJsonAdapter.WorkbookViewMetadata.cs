using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorkbookFunctionGroupsModel? ToWorkbookFunctionGroups(WorkbookFunctionGroupsDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var builtInGroupCount = string.IsNullOrWhiteSpace(dto.BuiltInGroupCount) ? null : dto.BuiltInGroupCount;
        var groups = (dto.Groups ?? [])
            .Select(ToWorkbookFunctionGroup)
            .OfType<WorkbookFunctionGroupModel>()
            .ToList();
        if (builtInGroupCount is null && nativeAttributes.Count == 0 && groups.Count == 0)
            return null;

        return new WorkbookFunctionGroupsModel
        {
            BuiltInGroupCount = builtInGroupCount,
            NativeAttributes = nativeAttributes,
            Groups = groups
        };
    }

    private static WorkbookFunctionGroupModel? ToWorkbookFunctionGroup(WorkbookFunctionGroupDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var name = string.IsNullOrWhiteSpace(dto.Name) ? null : dto.Name;
        if (name is null && nativeAttributes.Count == 0)
            return null;

        return new WorkbookFunctionGroupModel
        {
            Name = name,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorkbookAdditionalViewsModel? ToWorkbookAdditionalViews(WorkbookAdditionalViewsDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var views = (dto.Views ?? [])
            .Select(ToWorkbookAdditionalView)
            .OfType<WorkbookAdditionalViewModel>()
            .ToList();
        if (nativeAttributes.Count == 0 && views.Count == 0)
            return null;

        return new WorkbookAdditionalViewsModel
        {
            NativeAttributes = nativeAttributes,
            Views = views
        };
    }

    private static WorkbookAdditionalViewModel? ToWorkbookAdditionalView(WorkbookAdditionalViewDto? dto)
    {
        if (dto is null)
            return null;

        var nativeXml = string.IsNullOrWhiteSpace(dto.NativeXml) ? null : dto.NativeXml;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        if (nativeXml is null && nativeAttributes.Count == 0)
            return null;

        return new WorkbookAdditionalViewModel
        {
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorkbookFunctionGroupsDto? FromWorkbookFunctionGroups(WorkbookFunctionGroupsModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var builtInGroupCount = string.IsNullOrWhiteSpace(model.BuiltInGroupCount) ? null : model.BuiltInGroupCount;
        var groups = model.Groups
            .Select(FromWorkbookFunctionGroup)
            .OfType<WorkbookFunctionGroupDto>()
            .ToList();
        if (builtInGroupCount is null && nativeAttributes.Count == 0 && groups.Count == 0)
            return null;

        return new WorkbookFunctionGroupsDto
        {
            BuiltInGroupCount = builtInGroupCount,
            NativeAttributes = nativeAttributes,
            Groups = groups
        };
    }

    private static WorkbookFunctionGroupDto? FromWorkbookFunctionGroup(WorkbookFunctionGroupModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var name = string.IsNullOrWhiteSpace(model.Name) ? null : model.Name;
        if (name is null && nativeAttributes.Count == 0)
            return null;

        return new WorkbookFunctionGroupDto
        {
            Name = name,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorkbookAdditionalViewsDto? FromWorkbookAdditionalViews(WorkbookAdditionalViewsModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var views = model.Views
            .Select(FromWorkbookAdditionalView)
            .OfType<WorkbookAdditionalViewDto>()
            .ToList();
        if (nativeAttributes.Count == 0 && views.Count == 0)
            return null;

        return new WorkbookAdditionalViewsDto
        {
            NativeAttributes = nativeAttributes,
            Views = views
        };
    }

    private static WorkbookAdditionalViewDto? FromWorkbookAdditionalView(WorkbookAdditionalViewModel? model)
    {
        if (model is null)
            return null;

        var nativeXml = string.IsNullOrWhiteSpace(model.NativeXml) ? null : model.NativeXml;
        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        if (nativeXml is null && nativeAttributes.Count == 0)
            return null;

        return new WorkbookAdditionalViewDto
        {
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes
        };
    }
}
