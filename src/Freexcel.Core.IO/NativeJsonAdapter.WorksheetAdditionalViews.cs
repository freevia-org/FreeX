using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorksheetAdditionalViewsModel? ToWorksheetAdditionalViews(WorksheetAdditionalViewsDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var views = (dto.Views ?? [])
            .Select(ToWorksheetAdditionalView)
            .OfType<WorksheetAdditionalViewModel>()
            .ToList();
        if (nativeAttributes.Count == 0 && views.Count == 0)
            return null;

        return new WorksheetAdditionalViewsModel
        {
            NativeAttributes = nativeAttributes,
            Views = views
        };
    }

    private static WorksheetAdditionalViewModel? ToWorksheetAdditionalView(WorksheetAdditionalViewDto? dto)
    {
        if (dto is null)
            return null;

        var workbookViewId = string.IsNullOrWhiteSpace(dto.WorkbookViewId) ? null : dto.WorkbookViewId;
        var nativeXml = string.IsNullOrWhiteSpace(dto.NativeXml) ? null : dto.NativeXml;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        if (workbookViewId is null && nativeXml is null && nativeAttributes.Count == 0)
            return null;

        return new WorksheetAdditionalViewModel
        {
            WorkbookViewId = workbookViewId,
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorksheetAdditionalViewsDto? ToWorksheetAdditionalViewsDto(WorksheetAdditionalViewsModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(model.NativeAttributes);
        var views = model.Views
            .Select(ToWorksheetAdditionalViewDto)
            .OfType<WorksheetAdditionalViewDto>()
            .ToList();
        if (nativeAttributes.Count == 0 && views.Count == 0)
            return null;

        return new WorksheetAdditionalViewsDto
        {
            NativeAttributes = nativeAttributes,
            Views = views
        };
    }

    private static WorksheetAdditionalViewDto? ToWorksheetAdditionalViewDto(WorksheetAdditionalViewModel? model)
    {
        if (model is null)
            return null;

        var workbookViewId = string.IsNullOrWhiteSpace(model.WorkbookViewId) ? null : model.WorkbookViewId;
        var nativeXml = string.IsNullOrWhiteSpace(model.NativeXml) ? null : model.NativeXml;
        var nativeAttributes = CleanNativeAttributes(model.NativeAttributes);
        if (workbookViewId is null && nativeXml is null && nativeAttributes.Count == 0)
            return null;

        return new WorksheetAdditionalViewDto
        {
            WorkbookViewId = workbookViewId,
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes
        };
    }
}
