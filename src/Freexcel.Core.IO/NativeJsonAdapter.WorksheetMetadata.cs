using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorksheetAutoFilterModel? ToWorksheetAutoFilter(WorksheetAutoFilterDto? dto) =>
        dto is null || (string.IsNullOrWhiteSpace(dto.Reference) && string.IsNullOrWhiteSpace(dto.NativeXml))
            ? null
            : new WorksheetAutoFilterModel(dto.Reference, dto.NativeXml);

    private static WorksheetAutoFilterDto? ToWorksheetAutoFilterDto(WorksheetAutoFilterModel? autoFilter) =>
        autoFilter is null
            ? null
            : new WorksheetAutoFilterDto
            {
                Reference = autoFilter.Reference,
                NativeXml = autoFilter.NativeXml
            };

    private static WorksheetSmartTagsModel? ToWorksheetSmartTags(WorksheetSmartTagsDto? dto)
    {
        if (dto is null)
            return null;

        var nativeXml = string.IsNullOrWhiteSpace(dto.NativeXml) ? null : dto.NativeXml;
        var cells = (dto.Cells ?? [])
            .Select(ToWorksheetCellSmartTags)
            .OfType<WorksheetCellSmartTagsModel>()
            .ToList();
        if (nativeXml is null && cells.Count == 0)
            return null;

        return new WorksheetSmartTagsModel
        {
            NativeXml = nativeXml,
            Cells = cells
        };
    }

    private static WorksheetCellSmartTagsModel? ToWorksheetCellSmartTags(WorksheetCellSmartTagsDto? dto)
    {
        if (dto is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference;
        var nativeAttributes = CleanWorksheetSmartTagAttributes(dto.NativeAttributes);
        var tags = (dto.Tags ?? [])
            .Select(ToWorksheetCellSmartTag)
            .OfType<WorksheetCellSmartTagModel>()
            .ToList();
        if (reference is null && nativeAttributes.Count == 0 && tags.Count == 0)
            return null;

        return new WorksheetCellSmartTagsModel
        {
            Reference = reference,
            NativeAttributes = nativeAttributes,
            Tags = tags
        };
    }

    private static WorksheetCellSmartTagModel? ToWorksheetCellSmartTag(WorksheetCellSmartTagDto? dto)
    {
        if (dto is null)
            return null;

        var type = string.IsNullOrWhiteSpace(dto.Type) ? null : dto.Type;
        var nativeAttributes = CleanWorksheetSmartTagAttributes(dto.NativeAttributes);
        var properties = (dto.Properties ?? [])
            .Select(ToWorksheetCellSmartTagProperty)
            .OfType<WorksheetCellSmartTagPropertyModel>()
            .ToList();
        if (type is null && dto.Deleted is null && nativeAttributes.Count == 0 && properties.Count == 0)
            return null;

        return new WorksheetCellSmartTagModel
        {
            Type = type,
            Deleted = dto.Deleted,
            NativeAttributes = nativeAttributes,
            Properties = properties
        };
    }

    private static WorksheetCellSmartTagPropertyModel? ToWorksheetCellSmartTagProperty(WorksheetCellSmartTagPropertyDto? dto)
    {
        if (dto is null)
            return null;

        var key = string.IsNullOrWhiteSpace(dto.Key) ? null : dto.Key;
        var nativeAttributes = CleanWorksheetSmartTagAttributes(dto.NativeAttributes);
        if (key is null && dto.Value is null && nativeAttributes.Count == 0)
            return null;

        return new WorksheetCellSmartTagPropertyModel
        {
            Key = key,
            Value = dto.Value,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorksheetSmartTagsDto? ToWorksheetSmartTagsDto(WorksheetSmartTagsModel? model)
    {
        if (model is null)
            return null;

        var nativeXml = string.IsNullOrWhiteSpace(model.NativeXml) ? null : model.NativeXml;
        var cells = model.Cells
            .Select(ToWorksheetCellSmartTagsDto)
            .OfType<WorksheetCellSmartTagsDto>()
            .ToList();
        if (nativeXml is null && cells.Count == 0)
            return null;

        return new WorksheetSmartTagsDto
        {
            NativeXml = nativeXml,
            Cells = cells
        };
    }

    private static WorksheetCellSmartTagsDto? ToWorksheetCellSmartTagsDto(WorksheetCellSmartTagsModel? model)
    {
        if (model is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(model.Reference) ? null : model.Reference;
        var nativeAttributes = CleanWorksheetSmartTagAttributes(model.NativeAttributes);
        var tags = model.Tags
            .Select(ToWorksheetCellSmartTagDto)
            .OfType<WorksheetCellSmartTagDto>()
            .ToList();
        if (reference is null && nativeAttributes.Count == 0 && tags.Count == 0)
            return null;

        return new WorksheetCellSmartTagsDto
        {
            Reference = reference,
            NativeAttributes = nativeAttributes,
            Tags = tags
        };
    }

    private static WorksheetCellSmartTagDto? ToWorksheetCellSmartTagDto(WorksheetCellSmartTagModel? model)
    {
        if (model is null)
            return null;

        var type = string.IsNullOrWhiteSpace(model.Type) ? null : model.Type;
        var nativeAttributes = CleanWorksheetSmartTagAttributes(model.NativeAttributes);
        var properties = model.Properties
            .Select(ToWorksheetCellSmartTagPropertyDto)
            .OfType<WorksheetCellSmartTagPropertyDto>()
            .ToList();
        if (type is null && model.Deleted is null && nativeAttributes.Count == 0 && properties.Count == 0)
            return null;

        return new WorksheetCellSmartTagDto
        {
            Type = type,
            Deleted = model.Deleted,
            NativeAttributes = nativeAttributes,
            Properties = properties
        };
    }

    private static WorksheetCellSmartTagPropertyDto? ToWorksheetCellSmartTagPropertyDto(WorksheetCellSmartTagPropertyModel? model)
    {
        if (model is null)
            return null;

        var key = string.IsNullOrWhiteSpace(model.Key) ? null : model.Key;
        var nativeAttributes = CleanWorksheetSmartTagAttributes(model.NativeAttributes);
        if (key is null && model.Value is null && nativeAttributes.Count == 0)
            return null;

        return new WorksheetCellSmartTagPropertyDto
        {
            Key = key,
            Value = model.Value,
            NativeAttributes = nativeAttributes
        };
    }

    private static Dictionary<string, string> CleanWorksheetSmartTagAttributes(Dictionary<string, string>? attributes) =>
        (attributes ?? new Dictionary<string, string>())
        .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

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
