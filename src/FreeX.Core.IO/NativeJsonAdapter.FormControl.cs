using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private class FormControlDto
    {
        public FormControlKind Kind { get; set; } = FormControlKind.Unknown;
        public string? Name { get; set; }
        public string? Caption { get; set; }
        public uint? ShapeId { get; set; }
        public string? Anchor { get; set; }
        public DrawingAnchorRangeDto? AnchorOffsets { get; set; }
        public string? LinkedCell { get; set; }
        public string? ListFillRange { get; set; }
        public bool IsChecked { get; set; }
        public int? Value { get; set; }
        public int? Min { get; set; }
        public int? Max { get; set; }
        public int? Increment { get; set; }
        public int? PageChange { get; set; }
        public int? SelectedIndex { get; set; }
    }

    private static FormControlDto ToFormControlDto(FormControlModel control) => new()
    {
        Kind = Enum.IsDefined(control.Kind) ? control.Kind : FormControlKind.Unknown,
        Name = control.Name,
        Caption = control.Caption,
        ShapeId = control.ShapeId,
        Anchor = control.Anchor?.ToString(),
        AnchorOffsets = ToDrawingAnchorRangeDto(control.AnchorOffsets),
        LinkedCell = control.LinkedCell,
        ListFillRange = control.ListFillRange,
        IsChecked = control.IsChecked,
        Value = control.Value,
        Min = control.Min,
        Max = control.Max,
        Increment = control.Increment,
        PageChange = control.PageChange,
        SelectedIndex = control.SelectedIndex
    };

    private static bool IsFormControlOnSheet(FormControlModel control, SheetId sheetId) =>
        control.Anchor is not { } anchor || (anchor.Start.Sheet == sheetId && anchor.End.Sheet == sheetId);

    private static FormControlModel? ToFormControl(FormControlDto? dto, SheetId sheetId)
    {
        if (dto is null)
            return null;

        try
        {
            return new FormControlModel
            {
                Kind = Enum.IsDefined(dto.Kind) ? dto.Kind : FormControlKind.Unknown,
                Name = dto.Name,
                Caption = dto.Caption,
                ShapeId = dto.ShapeId,
                Anchor = string.IsNullOrWhiteSpace(dto.Anchor) ? null : GridRange.Parse(dto.Anchor, sheetId),
                AnchorOffsets = ToDrawingAnchorRange(dto.AnchorOffsets),
                LinkedCell = dto.LinkedCell,
                ListFillRange = dto.ListFillRange,
                IsChecked = dto.IsChecked,
                Value = dto.Value,
                Min = dto.Min,
                Max = dto.Max,
                Increment = dto.Increment,
                PageChange = dto.PageChange,
                SelectedIndex = dto.SelectedIndex
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static DrawingAnchorRangeDto? ToDrawingAnchorRangeDto(DrawingAnchorRange? anchor)
    {
        if (anchor is null)
            return null;

        return new DrawingAnchorRangeDto
        {
            From = ToDrawingAnchorPointDto(anchor.From),
            To = ToDrawingAnchorPointDto(anchor.To)
        };
    }

    private static DrawingAnchorPointDto ToDrawingAnchorPointDto(DrawingAnchorPoint point) => new()
    {
        Column = point.Column,
        ColumnOffsetEmu = point.ColumnOffsetEmu,
        Row = point.Row,
        RowOffsetEmu = point.RowOffsetEmu
    };

    private static DrawingAnchorRange? ToDrawingAnchorRange(DrawingAnchorRangeDto? dto)
    {
        if (dto is null)
            return null;

        return new DrawingAnchorRange(
            ToDrawingAnchorPoint(dto.From),
            ToDrawingAnchorPoint(dto.To));
    }

    private static DrawingAnchorPoint ToDrawingAnchorPoint(DrawingAnchorPointDto? dto) => new(
        dto?.Column ?? 0,
        dto?.ColumnOffsetEmu ?? 0,
        dto?.Row ?? 0,
        dto?.RowOffsetEmu ?? 0);
}
