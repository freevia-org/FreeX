using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class NativeJsonVisualDtoMapper
{
    public static PictureDto FromPicture(PictureModel picture) => new()
    {
        Name = picture.Name,
        Anchor = picture.Anchor.ToA1(),
        Kind = ValidEnumOrDefault(picture.Kind, PictureKind.CellRangeSnapshot),
        SourceRowCount = picture.SourceRowCount,
        SourceColumnCount = picture.SourceColumnCount,
        IsLinkedToSourceRange = picture.IsLinkedToSourceRange,
        LinkedSourceRange = picture.LinkedSourceRange?.ToString(),
        LinkedSourceSheetName = picture.LinkedSourceSheetName,
        ImageBase64 = picture.ImageBytes is { Length: > 0 } bytes ? Convert.ToBase64String(bytes) : null,
        ContentType = picture.ContentType,
        Width = PositiveFiniteOrDefault(picture.Width, 240),
        Height = PositiveFiniteOrDefault(picture.Height, 140),
        LockAspectRatio = picture.LockAspectRatio,
        RotationDegrees = NormalizeRotation(picture.RotationDegrees),
        IsVisible = picture.IsVisible,
        CropLeft = SanitizeCropEdge(picture.CropLeft),
        CropTop = SanitizeCropEdge(picture.CropTop),
        CropRight = SanitizeCropEdge(picture.CropRight),
        CropBottom = SanitizeCropEdge(picture.CropBottom),
        Title = picture.Title,
        AltText = picture.AltText,
        Cells = picture.Cells.Select(cell => new PictureCellDto
        {
            RowOffset = cell.RowOffset,
            ColumnOffset = cell.ColumnOffset,
            Text = cell.Text
        }).ToList()
    };

    public static PictureModel? ToPicture(PictureDto? pictureDto, SheetId sheetId)
    {
        if (pictureDto?.Anchor is null)
            return null;

        try
        {
            var picture = new PictureModel
            {
                Anchor = CellAddress.Parse(pictureDto.Anchor, sheetId),
                Name = pictureDto.Name,
                Kind = ValidEnumOrDefault(pictureDto.Kind, PictureKind.CellRangeSnapshot),
                SourceRowCount = pictureDto.SourceRowCount,
                SourceColumnCount = pictureDto.SourceColumnCount,
                IsLinkedToSourceRange = pictureDto.IsLinkedToSourceRange,
                LinkedSourceRange = pictureDto.LinkedSourceRange is null ? null : GridRange.Parse(pictureDto.LinkedSourceRange, sheetId),
                LinkedSourceSheetName = pictureDto.LinkedSourceSheetName,
                ImageBytes = string.IsNullOrEmpty(pictureDto.ImageBase64) ? null : Convert.FromBase64String(pictureDto.ImageBase64),
                ContentType = pictureDto.ContentType,
                Width = PositiveFiniteOrDefault(pictureDto.Width, 240),
                Height = PositiveFiniteOrDefault(pictureDto.Height, 140),
                LockAspectRatio = pictureDto.LockAspectRatio,
                RotationDegrees = NormalizeRotation(pictureDto.RotationDegrees),
                IsVisible = pictureDto.IsVisible,
                CropLeft = SanitizeCropEdge(pictureDto.CropLeft),
                CropTop = SanitizeCropEdge(pictureDto.CropTop),
                CropRight = SanitizeCropEdge(pictureDto.CropRight),
                CropBottom = SanitizeCropEdge(pictureDto.CropBottom),
                Title = pictureDto.Title,
                AltText = pictureDto.AltText
            };

            NormalizePictureCrop(picture);
            foreach (var cellDto in pictureDto.Cells ?? [])
                picture.Cells.Add(new PictureCellSnapshot(cellDto.RowOffset, cellDto.ColumnOffset, cellDto.Text ?? ""));

            return picture;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static TextBoxDto FromTextBox(TextBoxModel textBox) => new()
    {
        Name = textBox.Name,
        Anchor = textBox.Anchor.ToA1(),
        Text = textBox.Text,
        Width = PositiveFiniteOrDefault(textBox.Width, 180),
        Height = PositiveFiniteOrDefault(textBox.Height, 80),
        RotationDegrees = NormalizeRotation(textBox.RotationDegrees),
        IsVisible = textBox.IsVisible,
        FillColor = textBox.FillColor is { } fill ? FormatColor(fill) : null,
        OutlineColor = textBox.OutlineColor is { } outline ? FormatColor(outline) : null,
        FillThemeColor = FromThemeColorReference(textBox.FillThemeColor),
        OutlineThemeColor = FromThemeColorReference(textBox.OutlineThemeColor),
        Title = textBox.Title,
        AltText = textBox.AltText
    };

    public static TextBoxModel? ToTextBox(TextBoxDto? textBoxDto, SheetId sheetId)
    {
        if (textBoxDto?.Anchor is null)
            return null;

        try
        {
            return new TextBoxModel
            {
                Anchor = CellAddress.Parse(textBoxDto.Anchor, sheetId),
                Name = textBoxDto.Name,
                Text = textBoxDto.Text ?? "",
                Width = PositiveFiniteOrDefault(textBoxDto.Width, 180),
                Height = PositiveFiniteOrDefault(textBoxDto.Height, 80),
                RotationDegrees = NormalizeRotation(textBoxDto.RotationDegrees),
                IsVisible = textBoxDto.IsVisible,
                FillColor = textBoxDto.FillColor is { } fill ? ParseColor(fill) : null,
                OutlineColor = textBoxDto.OutlineColor is { } outline ? ParseColor(outline) : null,
                FillThemeColor = ToThemeColorReference(textBoxDto.FillThemeColor),
                OutlineThemeColor = ToThemeColorReference(textBoxDto.OutlineThemeColor),
                Title = textBoxDto.Title,
                AltText = textBoxDto.AltText
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static DrawingShapeDto FromDrawingShape(DrawingShapeModel shape) => new()
    {
        Name = shape.Name,
        Anchor = shape.Anchor.ToA1(),
        Kind = ValidEnumOrDefault(shape.Kind, DrawingShapeKind.Rectangle),
        Width = PositiveFiniteOrDefault(shape.Width, 120),
        Height = PositiveFiniteOrDefault(shape.Height, 70),
        RotationDegrees = NormalizeRotation(shape.RotationDegrees),
        IsVisible = shape.IsVisible,
        FillColor = shape.FillColor is { } fill ? FormatColor(fill) : null,
        OutlineColor = shape.OutlineColor is { } outline ? FormatColor(outline) : null,
        GradientFillEndColor = shape.GradientFillEndColor is { } gradientEnd ? FormatColor(gradientEnd) : null,
        FillThemeColor = FromThemeColorReference(shape.FillThemeColor),
        OutlineThemeColor = FromThemeColorReference(shape.OutlineThemeColor),
        HasShadowEffect = shape.HasShadowEffect,
        Title = shape.Title,
        AltText = shape.AltText
    };

    public static DrawingShapeModel? ToDrawingShape(DrawingShapeDto? shapeDto, SheetId sheetId)
    {
        if (shapeDto?.Anchor is null)
            return null;

        try
        {
            return new DrawingShapeModel
            {
                Anchor = CellAddress.Parse(shapeDto.Anchor, sheetId),
                Name = shapeDto.Name,
                Kind = ValidEnumOrDefault(shapeDto.Kind, DrawingShapeKind.Rectangle),
                Width = PositiveFiniteOrDefault(shapeDto.Width, 120),
                Height = PositiveFiniteOrDefault(shapeDto.Height, 70),
                RotationDegrees = NormalizeRotation(shapeDto.RotationDegrees),
                IsVisible = shapeDto.IsVisible,
                FillColor = shapeDto.FillColor is { } fill ? ParseColor(fill) : null,
                OutlineColor = shapeDto.OutlineColor is { } outline ? ParseColor(outline) : null,
                GradientFillEndColor = shapeDto.GradientFillEndColor is { } gradientEnd ? ParseColor(gradientEnd) : null,
                FillThemeColor = ToThemeColorReference(shapeDto.FillThemeColor),
                OutlineThemeColor = ToThemeColorReference(shapeDto.OutlineThemeColor),
                HasShadowEffect = shapeDto.HasShadowEffect,
                Title = shapeDto.Title,
                AltText = shapeDto.AltText
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static TEnum ValidEnumOrDefault<TEnum>(TEnum value, TEnum defaultValue)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value) ? value : defaultValue;

    private static double PositiveFiniteOrDefault(double value, double defaultValue) =>
        double.IsFinite(value) && value > 0 ? value : defaultValue;

    private static double NormalizeRotation(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static double SanitizeCropEdge(double value) =>
        double.IsFinite(value) && value > 0 ? Math.Min(0.99, value) : 0;

    private static void NormalizePictureCrop(PictureModel picture)
    {
        if (picture.CropLeft + picture.CropRight >= 1)
        {
            picture.CropLeft = 0;
            picture.CropRight = 0;
        }

        if (picture.CropTop + picture.CropBottom >= 1)
        {
            picture.CropTop = 0;
            picture.CropBottom = 0;
        }
    }

    private static string FormatColor(CellColor color) => NativeJsonColorMapper.FormatColor(color);

    private static CellColor? ParseColor(string text) => NativeJsonColorMapper.ParseColor(text);

    private static WorkbookThemeColorReference? ToThemeColorReference(ThemeColorReferenceDto? dto) =>
        NativeJsonColorMapper.ToThemeColorReference(dto);

    private static ThemeColorReferenceDto? FromThemeColorReference(WorkbookThemeColorReference? reference) =>
        NativeJsonColorMapper.FromThemeColorReference(reference);
}

internal class PictureDto
{
    public string? Name { get; set; }
    public string? Anchor { get; set; }
    public PictureKind Kind { get; set; } = PictureKind.CellRangeSnapshot;
    public uint SourceRowCount { get; set; }
    public uint SourceColumnCount { get; set; }
    public bool IsLinkedToSourceRange { get; set; }
    public string? LinkedSourceRange { get; set; }
    public string? LinkedSourceSheetName { get; set; }
    public string? ImageBase64 { get; set; }
    public string? ContentType { get; set; }
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 140;
    public bool LockAspectRatio { get; set; } = true;
    public double RotationDegrees { get; set; }
    public bool IsVisible { get; set; } = true;
    public double CropLeft { get; set; }
    public double CropTop { get; set; }
    public double CropRight { get; set; }
    public double CropBottom { get; set; }
    public string? AltText { get; set; }
    public string? Title { get; set; }
    public List<PictureCellDto> Cells { get; set; } = [];
}

internal class PictureCellDto
{
    public uint RowOffset { get; set; }
    public uint ColumnOffset { get; set; }
    public string? Text { get; set; }
}

internal class TextBoxDto
{
    public string? Name { get; set; }
    public string? Anchor { get; set; }
    public string? Text { get; set; }
    public double Width { get; set; } = 180;
    public double Height { get; set; } = 80;
    public double RotationDegrees { get; set; }
    public bool IsVisible { get; set; } = true;
    public string? FillColor { get; set; }
    public string? OutlineColor { get; set; }
    public ThemeColorReferenceDto? FillThemeColor { get; set; }
    public ThemeColorReferenceDto? OutlineThemeColor { get; set; }
    public string? Title { get; set; }
    public string? AltText { get; set; }
}

internal class DrawingShapeDto
{
    public string? Name { get; set; }
    public string? Anchor { get; set; }
    public DrawingShapeKind Kind { get; set; } = DrawingShapeKind.Rectangle;
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 70;
    public double RotationDegrees { get; set; }
    public bool IsVisible { get; set; } = true;
    public string? FillColor { get; set; }
    public string? OutlineColor { get; set; }
    public string? GradientFillEndColor { get; set; }
    public ThemeColorReferenceDto? FillThemeColor { get; set; }
    public ThemeColorReferenceDto? OutlineThemeColor { get; set; }
    public bool HasShadowEffect { get; set; }
    public string? Title { get; set; }
    public string? AltText { get; set; }
}

internal class ThemeColorReferenceDto
{
    public WorkbookThemeColorSlot Slot { get; set; }
    public double Tint { get; set; }
}
