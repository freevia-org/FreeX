using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorksheetAutoFilterModel? ToWorksheetAutoFilter(WorksheetAutoFilterDto? dto)
    {
        if (dto is null ||
            (string.IsNullOrWhiteSpace(dto.Reference) &&
             string.IsNullOrWhiteSpace(dto.NativeXml) &&
             (dto.FilterColumns is null || dto.FilterColumns.Count == 0)))
        {
            return null;
        }

        var autoFilter = new WorksheetAutoFilterModel(dto.Reference, dto.NativeXml)
        {
            NativeAttributes = CleanNativeAttributes(dto.NativeAttributes),
            NativeChildXmls = dto.NativeChildXmls?
                .Where(xml => !string.IsNullOrWhiteSpace(xml))
                .ToArray()
        };
        foreach (var column in dto.FilterColumns ?? [])
        {
            if (column.ColumnId >= 0)
            {
                autoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
                    column.ColumnId,
                    column.Values?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [],
                    column.IncludeBlank,
                    column.NativeFilterXmls?.Where(xml => !string.IsNullOrWhiteSpace(xml)).ToArray() ?? [],
                    CleanNativeAttributes(column.NativeAttributes)));
            }
        }

        return autoFilter;
    }

    private static WorksheetAutoFilterDto? ToWorksheetAutoFilterDto(WorksheetAutoFilterModel? autoFilter) =>
        autoFilter is null
            ? null
            : new WorksheetAutoFilterDto
            {
                Reference = autoFilter.Reference,
                NativeXml = autoFilter.NativeXml,
                NativeAttributes = CleanNativeAttributesForSave(autoFilter.NativeAttributes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
                NativeChildXmls = autoFilter.NativeChildXmls?
                    .Where(xml => !string.IsNullOrWhiteSpace(xml))
                    .ToList(),
                FilterColumns = autoFilter.FilterColumns.Select(column => new WorksheetAutoFilterColumnDto
                {
                    ColumnId = column.ColumnId,
                    Values = column.Values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList(),
                    IncludeBlank = column.IncludeBlank,
                    NativeFilterXmls = column.NativeFilterXmls.Where(xml => !string.IsNullOrWhiteSpace(xml)).ToList(),
                    NativeAttributes = CleanNativeAttributesForSave(column.NativeAttributes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                }).ToList()
            };

    private static WorksheetCustomPropertyMetadataModel? ToWorksheetCustomPropertyMetadata(WorksheetCustomPropertyMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var nativeChildXmls = (dto.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetCustomPropertyMetadataModel
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetCustomPropertyMetadataDto? FromWorksheetCustomPropertyMetadata(WorksheetCustomPropertyMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var nativeChildXmls = (model.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetCustomPropertyMetadataDto
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetSortStateModel? ToWorksheetSortState(WorksheetSortStateDto? dto)
    {
        if (dto is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference;
        var sortMethod = string.IsNullOrWhiteSpace(dto.SortMethod) ? null : dto.SortMethod;
        var nativeXml = string.IsNullOrWhiteSpace(dto.NativeXml) ? null : dto.NativeXml;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var conditions = (dto.Conditions ?? [])
            .Select(ToWorksheetSortCondition)
            .OfType<WorksheetSortConditionModel>()
            .ToList();
        if (reference is null && dto.ColumnSort is null && dto.CaseSensitive is null && sortMethod is null &&
            nativeXml is null && nativeAttributes.Count == 0 && conditions.Count == 0)
        {
            return null;
        }

        return new WorksheetSortStateModel
        {
            Reference = reference,
            ColumnSort = dto.ColumnSort,
            CaseSensitive = dto.CaseSensitive,
            SortMethod = sortMethod,
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes,
            Conditions = conditions
        };
    }

    private static WorksheetSortConditionModel? ToWorksheetSortCondition(WorksheetSortConditionDto? dto)
    {
        if (dto is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference;
        var sortBy = string.IsNullOrWhiteSpace(dto.SortBy) ? null : dto.SortBy;
        var customList = string.IsNullOrWhiteSpace(dto.CustomList) ? null : dto.CustomList;
        var dxfId = string.IsNullOrWhiteSpace(dto.DxfId) ? null : dto.DxfId;
        var iconSet = string.IsNullOrWhiteSpace(dto.IconSet) ? null : dto.IconSet;
        var iconId = string.IsNullOrWhiteSpace(dto.IconId) ? null : dto.IconId;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        if (reference is null && dto.Descending is null && sortBy is null && customList is null &&
            dxfId is null && iconSet is null && iconId is null && nativeAttributes.Count == 0)
        {
            return null;
        }

        return new WorksheetSortConditionModel
        {
            Reference = reference,
            Descending = dto.Descending,
            SortBy = sortBy,
            CustomList = customList,
            DxfId = dxfId,
            IconSet = iconSet,
            IconId = iconId,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorksheetSortStateDto? ToWorksheetSortStateDto(WorksheetSortStateModel? model)
    {
        if (model is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(model.Reference) ? null : model.Reference;
        var sortMethod = string.IsNullOrWhiteSpace(model.SortMethod) ? null : model.SortMethod;
        var nativeXml = string.IsNullOrWhiteSpace(model.NativeXml) ? null : model.NativeXml;
        var nativeAttributes = CleanNativeAttributes(model.NativeAttributes);
        var conditions = model.Conditions
            .Select(ToWorksheetSortConditionDto)
            .OfType<WorksheetSortConditionDto>()
            .ToList();
        if (reference is null && model.ColumnSort is null && model.CaseSensitive is null && sortMethod is null &&
            nativeXml is null && nativeAttributes.Count == 0 && conditions.Count == 0)
        {
            return null;
        }

        return new WorksheetSortStateDto
        {
            Reference = reference,
            ColumnSort = model.ColumnSort,
            CaseSensitive = model.CaseSensitive,
            SortMethod = sortMethod,
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes,
            Conditions = conditions
        };
    }

    private static WorksheetSortConditionDto? ToWorksheetSortConditionDto(WorksheetSortConditionModel? model)
    {
        if (model is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(model.Reference) ? null : model.Reference;
        var sortBy = string.IsNullOrWhiteSpace(model.SortBy) ? null : model.SortBy;
        var customList = string.IsNullOrWhiteSpace(model.CustomList) ? null : model.CustomList;
        var dxfId = string.IsNullOrWhiteSpace(model.DxfId) ? null : model.DxfId;
        var iconSet = string.IsNullOrWhiteSpace(model.IconSet) ? null : model.IconSet;
        var iconId = string.IsNullOrWhiteSpace(model.IconId) ? null : model.IconId;
        var nativeAttributes = CleanNativeAttributes(model.NativeAttributes);
        if (reference is null && model.Descending is null && sortBy is null && customList is null &&
            dxfId is null && iconSet is null && iconId is null && nativeAttributes.Count == 0)
        {
            return null;
        }

        return new WorksheetSortConditionDto
        {
            Reference = reference,
            Descending = model.Descending,
            SortBy = sortBy,
            CustomList = customList,
            DxfId = dxfId,
            IconSet = iconSet,
            IconId = iconId,
            NativeAttributes = nativeAttributes
        };
    }

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

    private static WorksheetCustomViewState ToWorksheetCustomViewState(CustomViewSheetDto sheetDto)
    {
        var frozenRows = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(sheetDto.FrozenRows);
        var frozenCols = NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(sheetDto.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        return new WorksheetCustomViewState(
            sheetDto.SheetName,
            Enum.IsDefined(sheetDto.ViewMode) ? sheetDto.ViewMode : WorksheetViewMode.Normal,
            frozenRows,
            frozenCols,
            hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidRowPaneOrNull(sheetDto.SplitRow),
            hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidColumnPaneOrNull(sheetDto.SplitColumn),
            sheetDto.ShowGridlines ?? true,
            sheetDto.ShowHeadings ?? true,
            sheetDto.ShowRulers ?? true,
            NativeJsonValueSanitizer.ValidZoomPercentOrDefault(sheetDto.ZoomPercent),
            sheetDto.ShowFormulas ?? false);
    }

    private static CustomViewSheetDto ToCustomViewSheetDto(WorksheetCustomViewState state)
    {
        var frozenRows = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(state.FrozenRows);
        var frozenCols = NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(state.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        return new CustomViewSheetDto
        {
            SheetName = state.SheetName,
            ViewMode = NativeJsonValueSanitizer.ValidEnumOrDefault(state.ViewMode, WorksheetViewMode.Normal),
            FrozenRows = frozenRows,
            FrozenCols = frozenCols,
            SplitRow = hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidRowPaneOrNull(state.SplitRow),
            SplitColumn = hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidColumnPaneOrNull(state.SplitColumn),
            ShowGridlines = state.ShowGridlines,
            ShowHeadings = state.ShowHeadings,
            ShowRulers = state.ShowRulers,
            ZoomPercent = NativeJsonValueSanitizer.ValidZoomPercentOrDefault(state.ZoomPercent),
            ShowFormulas = state.ShowFormulas
        };
    }

    private static WorksheetPhoneticProperties? ToWorksheetPhoneticProperties(WorksheetPhoneticPropertiesDto? dto)
    {
        if (dto is null)
            return null;

        var fontId = string.IsNullOrWhiteSpace(dto.FontId) ? null : dto.FontId;
        var type = string.IsNullOrWhiteSpace(dto.Type) ? null : dto.Type;
        var alignment = string.IsNullOrWhiteSpace(dto.Alignment) ? null : dto.Alignment;
        return fontId is null && type is null && alignment is null
            ? null
            : new WorksheetPhoneticProperties(fontId, type, alignment);
    }

    private static WorksheetPhoneticPropertiesDto? ToWorksheetPhoneticPropertiesDto(WorksheetPhoneticProperties? properties)
    {
        if (properties is null)
            return null;

        var fontId = string.IsNullOrWhiteSpace(properties.FontId) ? null : properties.FontId;
        var type = string.IsNullOrWhiteSpace(properties.Type) ? null : properties.Type;
        var alignment = string.IsNullOrWhiteSpace(properties.Alignment) ? null : properties.Alignment;
        return fontId is null && type is null && alignment is null
            ? null
            : new WorksheetPhoneticPropertiesDto
            {
                FontId = fontId,
                Type = type,
                Alignment = alignment
            };
    }

    private static WorksheetRepeatRange? ToRepeatRange(RepeatRangeDto? dto) =>
        dto is null ? null : new WorksheetRepeatRange(dto.Start, dto.End);

    private static RepeatRangeDto? FromRepeatRange(WorksheetRepeatRange? range) =>
        range is null ? null : new RepeatRangeDto { Start = range.Value.Start, End = range.Value.End };

    private static RepeatRangeDto? FromValidRepeatRange(WorksheetRepeatRange? range, uint max) =>
        range is { } value && value.Start >= 1 && value.End >= value.Start && value.End <= max
            ? new RepeatRangeDto { Start = value.Start, End = value.End }
            : null;

    private static PageMarginsDto FromPageMargins(WorksheetPageMargins margins) =>
        new()
        {
            Left = margins.Left,
            Right = margins.Right,
            Top = margins.Top,
            Bottom = margins.Bottom
        };

    private static WorksheetHeaderFooter ToHeaderFooter(HeaderFooterDto? dto) =>
        dto is null
            ? new WorksheetHeaderFooter("", "", "")
            : new WorksheetHeaderFooter(dto.Left ?? "", dto.Center ?? "", dto.Right ?? "");

    private static HeaderFooterDto FromHeaderFooter(WorksheetHeaderFooter value) =>
        new() { Left = value.Left, Center = value.Center, Right = value.Right };

    private static WorksheetHeaderFooterPictureSet ToHeaderFooterPictures(HeaderFooterPictureSetDto? dto) =>
        dto is null
            ? WorksheetHeaderFooterPictureSet.Empty
            : new WorksheetHeaderFooterPictureSet(
                ToHeaderFooterPicture(dto.Left),
                ToHeaderFooterPicture(dto.Center),
                ToHeaderFooterPicture(dto.Right));

    private static HeaderFooterPictureSetDto? FromHeaderFooterPictures(WorksheetHeaderFooterPictureSet value) =>
        value.Left is null && value.Center is null && value.Right is null
            ? null
            : new HeaderFooterPictureSetDto
            {
                Left = FromHeaderFooterPicture(value.Left),
                Center = FromHeaderFooterPicture(value.Center),
                Right = FromHeaderFooterPicture(value.Right)
            };

    private static WorksheetHeaderFooterPicture? ToHeaderFooterPicture(HeaderFooterPictureDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.ImageBase64))
            return null;

        try
        {
            var bytes = Convert.FromBase64String(dto.ImageBase64);
            return new WorksheetHeaderFooterPicture(
                bytes,
                string.IsNullOrWhiteSpace(dto.ContentType) ? "image/png" : dto.ContentType,
                dto.FileName,
                NativeJsonValueSanitizer.PositiveFiniteOrDefault(dto.Width, 96),
                NativeJsonValueSanitizer.PositiveFiniteOrDefault(dto.Height, 48));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static HeaderFooterPictureDto? FromHeaderFooterPicture(WorksheetHeaderFooterPicture? value) =>
        value is null
            ? null
            : new HeaderFooterPictureDto
            {
                ImageBase64 = Convert.ToBase64String(value.ImageBytes),
                ContentType = value.ContentType,
                FileName = value.FileName,
                Width = NativeJsonValueSanitizer.PositiveFiniteOrDefault(value.Width, 96),
                Height = NativeJsonValueSanitizer.PositiveFiniteOrDefault(value.Height, 48)
            };
}
