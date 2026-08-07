using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class NativeJsonVisualDtoMapper
{
    public static PictureDto FromPicture(PictureModel picture) => new()
    {
        Id = picture.Id,
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
        FlipHorizontal = picture.FlipHorizontal,
        FlipVertical = picture.FlipVertical,
        IsVisible = picture.IsVisible,
        CropLeft = SanitizeCropEdge(picture.CropLeft),
        CropTop = SanitizeCropEdge(picture.CropTop),
        CropRight = SanitizeCropEdge(picture.CropRight),
        CropBottom = SanitizeCropEdge(picture.CropBottom),
        Title = picture.Title,
        AltText = picture.AltText,
        IsDecorative = picture.IsDecorative,
        // R127B-native-fxl-editas-parity: mirrors ChartDto.DrawingAnchorKind (NativeJsonAdapter.ChartDto.cs)
        // -- without this, loading an .xlsx with a oneCellAnchor/absoluteAnchor picture (correctly
        // captured by XlsxDrawingAnchorApplier), Save As .fxl, then reopening silently reverted the
        // kind to the TwoCell default, reintroducing the original r127 move/resize defect.
        DrawingAnchorKind = ValidEnumOrDefault(picture.DrawingAnchorKind, ChartDrawingAnchorKind.TwoCell),
        Cells = picture.Cells
            .OfType<PictureCellSnapshot>()
            .Select(cell => new PictureCellDto
            {
                RowOffset = cell.RowOffset,
                ColumnOffset = cell.ColumnOffset,
                Text = cell.Text,
                Style = NativeJsonAdapter.FromCellStyle(cell.Style),
                IsNumericOrDate = cell.IsNumericOrDate
            })
            .ToList()
    };

    // A picture is owned by (and saved under) the sheet it is anchored/visually placed on, regardless
    // of which sheet its optional LinkedSourceRange points at. The linked range's own sheet identity
    // round-trips separately via LinkedSourceSheetName (see FromPicture/ToPicture + the cross-sheet
    // rebind pass in NativeJsonAdapter), so a cross-sheet linked picture must never be excluded here —
    // doing so silently drops the picture from every sheet's saved list (P24).
    public static bool IsPictureOnSheet(PictureModel picture, SheetId sheetId) =>
        picture.Anchor.Sheet == sheetId;

    public static PictureModel? ToPicture(PictureDto? pictureDto, SheetId sheetId)
    {
        if (pictureDto?.Anchor is null)
            return null;

        try
        {
            var picture = new PictureModel
            {
                Id = ExistingOrNewId(pictureDto.Id),
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
                FlipHorizontal = pictureDto.FlipHorizontal,
                FlipVertical = pictureDto.FlipVertical,
                IsVisible = pictureDto.IsVisible,
                CropLeft = SanitizeCropEdge(pictureDto.CropLeft),
                CropTop = SanitizeCropEdge(pictureDto.CropTop),
                CropRight = SanitizeCropEdge(pictureDto.CropRight),
                CropBottom = SanitizeCropEdge(pictureDto.CropBottom),
                Title = pictureDto.Title,
                AltText = pictureDto.AltText,
                IsDecorative = pictureDto.IsDecorative,
                // R127B-native-fxl-editas-parity: see the matching comment on FromPicture's assignment.
                DrawingAnchorKind = ValidEnumOrDefault(pictureDto.DrawingAnchorKind, ChartDrawingAnchorKind.TwoCell)
            };

            NormalizePictureCrop(picture);
            foreach (var cellDto in pictureDto.Cells ?? [])
            {
                if (cellDto is null)
                    continue;

                picture.Cells.Add(new PictureCellSnapshot(
                    cellDto.RowOffset,
                    cellDto.ColumnOffset,
                    cellDto.Text ?? "",
                    NativeJsonAdapter.ToCellStyle(cellDto.Style),
                    cellDto.IsNumericOrDate));
            }

            return picture;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static TextBoxDto FromTextBox(TextBoxModel textBox) => new()
    {
        Id = textBox.Id,
        Name = textBox.Name,
        Anchor = textBox.Anchor.ToA1(),
        Text = textBox.Text,
        Width = PositiveFiniteOrDefault(textBox.Width, 180),
        Height = PositiveFiniteOrDefault(textBox.Height, 80),
        RotationDegrees = NormalizeRotation(textBox.RotationDegrees),
        FlipHorizontal = textBox.FlipHorizontal,
        FlipVertical = textBox.FlipVertical,
        IsVisible = textBox.IsVisible,
        HasFill = textBox.HasFill,
        FillColor = textBox.FillColor is { } fill ? FormatColor(fill) : null,
        OutlineColor = textBox.OutlineColor is { } outline ? FormatColor(outline) : null,
        FillThemeColor = FromThemeColorReference(textBox.FillThemeColor),
        OutlineThemeColor = FromThemeColorReference(textBox.OutlineThemeColor),
        // R91-commands-insert-object-5-1: round-trip explicit line suppression (e.g. a freshly
        // inserted text box, which now defaults to No Line) through the native format too --
        // mirrors DrawingShapeDto.OutlineHasNoFill. Defaults false so pre-existing .fxl files
        // without this field keep their prior always-bordered rendering.
        OutlineHasNoFill = textBox.OutlineHasNoFill,
        Title = textBox.Title,
        AltText = textBox.AltText,
        TextFontFamily = textBox.TextFontFamily,
        TextFontSizePoints = textBox.TextFontSizePoints,
        TextBold = textBox.TextBold,
        TextItalic = textBox.TextItalic,
        TextColor = textBox.TextColor is { } textColor ? FormatColor(textColor) : null,
        TextThemeColor = FromThemeColorReference(textBox.TextThemeColor),
        TextHAlign = ValidEnumOrDefault(textBox.TextHAlign, DrawingShapeTextHAlign.Left),
        TextVAnchor = ValidEnumOrDefault(textBox.TextVAnchor, DrawingShapeTextVAnchor.Top),
        // R127B-native-fxl-editas-parity: see the matching comment on PictureDto's DrawingAnchorKind.
        DrawingAnchorKind = ValidEnumOrDefault(textBox.DrawingAnchorKind, ChartDrawingAnchorKind.TwoCell)
    };

    public static bool IsTextBoxOnSheet(TextBoxModel textBox, SheetId sheetId) =>
        textBox.Anchor.Sheet == sheetId;

    public static TextBoxModel? ToTextBox(TextBoxDto? textBoxDto, SheetId sheetId)
    {
        if (textBoxDto?.Anchor is null)
            return null;

        try
        {
            return new TextBoxModel
            {
                Id = ExistingOrNewId(textBoxDto.Id),
                Anchor = CellAddress.Parse(textBoxDto.Anchor, sheetId),
                Name = textBoxDto.Name,
                Text = textBoxDto.Text ?? "",
                Width = PositiveFiniteOrDefault(textBoxDto.Width, 180),
                Height = PositiveFiniteOrDefault(textBoxDto.Height, 80),
                RotationDegrees = NormalizeRotation(textBoxDto.RotationDegrees),
                FlipHorizontal = textBoxDto.FlipHorizontal,
                FlipVertical = textBoxDto.FlipVertical,
                IsVisible = textBoxDto.IsVisible,
                HasFill = textBoxDto.HasFill,
                FillColor = textBoxDto.FillColor is { } fill ? ParseColor(fill) : null,
                OutlineColor = textBoxDto.OutlineColor is { } outline ? ParseColor(outline) : null,
                FillThemeColor = ToThemeColorReference(textBoxDto.FillThemeColor),
                OutlineThemeColor = ToThemeColorReference(textBoxDto.OutlineThemeColor),
                OutlineHasNoFill = textBoxDto.OutlineHasNoFill,
                Title = textBoxDto.Title,
                AltText = textBoxDto.AltText,
                TextFontFamily = textBoxDto.TextFontFamily,
                TextFontSizePoints = textBoxDto.TextFontSizePoints,
                TextBold = textBoxDto.TextBold,
                TextItalic = textBoxDto.TextItalic,
                TextColor = textBoxDto.TextColor is { } textColor ? ParseColor(textColor) : null,
                TextThemeColor = ToThemeColorReference(textBoxDto.TextThemeColor),
                TextHAlign = ValidEnumOrDefault(textBoxDto.TextHAlign, DrawingShapeTextHAlign.Left),
                TextVAnchor = ValidEnumOrDefault(textBoxDto.TextVAnchor, DrawingShapeTextVAnchor.Top),
                // R127B-native-fxl-editas-parity: see the matching comment on PictureDto's DrawingAnchorKind.
                DrawingAnchorKind = ValidEnumOrDefault(textBoxDto.DrawingAnchorKind, ChartDrawingAnchorKind.TwoCell)
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static DrawingShapeDto FromDrawingShape(DrawingShapeModel shape) => new()
    {
        Id = shape.Id,
        Name = shape.Name,
        Anchor = shape.Anchor.ToA1(),
        Kind = ValidEnumOrDefault(shape.Kind, DrawingShapeKind.Rectangle),
        Width = PositiveFiniteOrDefault(shape.Width, 120),
        Height = PositiveFiniteOrDefault(shape.Height, 70),
        RotationDegrees = NormalizeRotation(shape.RotationDegrees),
        FlipHorizontal = shape.FlipHorizontal,
        FlipVertical = shape.FlipVertical,
        IsVisible = shape.IsVisible,
        HasFill = shape.HasFill,
        FillColor = shape.FillColor is { } fill ? FormatColor(fill) : null,
        OutlineColor = shape.OutlineColor is { } outline ? FormatColor(outline) : null,
        GradientFillEndColor = shape.GradientFillEndColor is { } gradientEnd ? FormatColor(gradientEnd) : null,
        GradientFillDirection = ValidEnumOrDefault(
            shape.GetEffectiveGradientFillDirection(),
            DrawingShapeGradientDirection.DiagonalDown),
        FillThemeColor = FromThemeColorReference(shape.FillThemeColor),
        OutlineThemeColor = FromThemeColorReference(shape.OutlineThemeColor),
        HasShadowEffect = shape.GetEffectiveEffectPreset() == DrawingShapeEffectPreset.Shadow,
        EffectPreset = ValidEnumOrDefault(shape.GetEffectiveEffectPreset(), DrawingShapeEffectPreset.None),
        Title = shape.Title,
        AltText = shape.AltText,
        OutlineWidthPoints = shape.OutlineWidthPoints,
        OutlineHasNoFill = shape.OutlineHasNoFill,
        OutlineDash = ValidEnumOrDefault(shape.OutlineDash, DrawingShapeOutlineDash.Solid),
        HeadArrowhead = shape.HeadArrowhead is { IsPresent: true } ha
            ? new ArrowheadDto { Type = ha.Type, Width = ha.Width, Length = ha.Length }
            : null,
        TailArrowhead = shape.TailArrowhead is { IsPresent: true } ta
            ? new ArrowheadDto { Type = ta.Type, Width = ta.Width, Length = ta.Length }
            : null,
        ShapeText = shape.ShapeText,
        ShapeTextFontSizePoints = shape.ShapeTextFontSizePoints,
        ShapeTextBold = shape.ShapeTextBold,
        ShapeTextItalic = shape.ShapeTextItalic,
        ShapeTextUnderline = shape.ShapeTextUnderline,
        ShapeTextColor = shape.ShapeTextColor is { } tc ? FormatColor(tc) : null,
        ShapeTextThemeColor = FromThemeColorReference(shape.ShapeTextThemeColor),
        ShapeTextHAlign = ValidEnumOrDefault(shape.ShapeTextHAlign, DrawingShapeTextHAlign.Left),
        ShapeTextVAnchor = ValidEnumOrDefault(shape.ShapeTextVAnchor, DrawingShapeTextVAnchor.Middle),
        ShapeTextWrap = shape.ShapeTextWrap,
        IsWordArt = shape.IsWordArt,
        WarpPreset = shape.WarpPreset,
        ShapeTextGradientEndColor = shape.ShapeTextGradientEndColor is { } gradEnd ? FormatColor(gradEnd) : null,
        ShapeTextGradientEndThemeColor = FromThemeColorReference(shape.ShapeTextGradientEndThemeColor),
        ShapeTextGradientAngle = shape.ShapeTextGradientAngle,
        ShapeTextOutlineColor = shape.ShapeTextOutlineColor is { } outlineC ? FormatColor(outlineC) : null,
        ShapeTextOutlineThemeColor = FromThemeColorReference(shape.ShapeTextOutlineThemeColor),
        ShapeTextOutlineWidthPoints = shape.ShapeTextOutlineWidthPoints,
        // R127B-native-fxl-editas-parity: see the matching comment on PictureDto's DrawingAnchorKind.
        DrawingAnchorKind = ValidEnumOrDefault(shape.DrawingAnchorKind, ChartDrawingAnchorKind.TwoCell)
    };

    public static bool IsDrawingShapeOnSheet(DrawingShapeModel shape, SheetId sheetId) =>
        shape.Anchor.Sheet == sheetId;

    public static DrawingShapeModel? ToDrawingShape(DrawingShapeDto? shapeDto, SheetId sheetId)
    {
        if (shapeDto?.Anchor is null)
            return null;

        try
        {
            var effectPreset = ValidEnumOrDefault(shapeDto.EffectPreset, DrawingShapeEffectPreset.None);
            if (effectPreset == DrawingShapeEffectPreset.None && shapeDto.HasShadowEffect)
                effectPreset = DrawingShapeEffectPreset.Shadow;

            return new DrawingShapeModel
            {
                Id = ExistingOrNewId(shapeDto.Id),
                Anchor = CellAddress.Parse(shapeDto.Anchor, sheetId),
                Name = shapeDto.Name,
                Kind = ValidEnumOrDefault(shapeDto.Kind, DrawingShapeKind.Rectangle),
                Width = PositiveFiniteOrDefault(shapeDto.Width, 120),
                Height = PositiveFiniteOrDefault(shapeDto.Height, 70),
                RotationDegrees = NormalizeRotation(shapeDto.RotationDegrees),
                FlipHorizontal = shapeDto.FlipHorizontal,
                FlipVertical = shapeDto.FlipVertical,
                IsVisible = shapeDto.IsVisible,
                HasFill = shapeDto.HasFill,
                FillColor = shapeDto.FillColor is { } fill ? ParseColor(fill) : null,
                OutlineColor = shapeDto.OutlineColor is { } outline ? ParseColor(outline) : null,
                GradientFillEndColor = shapeDto.GradientFillEndColor is { } gradientEnd ? ParseColor(gradientEnd) : null,
                GradientFillDirection = ValidEnumOrDefault(
                    shapeDto.GradientFillDirection,
                    DrawingShapeGradientDirection.DiagonalDown),
                FillThemeColor = ToThemeColorReference(shapeDto.FillThemeColor),
                OutlineThemeColor = ToThemeColorReference(shapeDto.OutlineThemeColor),
                HasShadowEffect = effectPreset == DrawingShapeEffectPreset.Shadow,
                EffectPreset = effectPreset,
                Title = shapeDto.Title,
                AltText = shapeDto.AltText,
                OutlineWidthPoints = shapeDto.OutlineWidthPoints,
                OutlineHasNoFill = shapeDto.OutlineHasNoFill,
                OutlineDash = ValidEnumOrDefault(shapeDto.OutlineDash, DrawingShapeOutlineDash.Solid),
                HeadArrowhead = shapeDto.HeadArrowhead is { } ha && ha.Type != DrawingArrowheadType.None
                    ? new DrawingArrowhead(ha.Type, ha.Width, ha.Length)
                    : null,
                TailArrowhead = shapeDto.TailArrowhead is { } ta && ta.Type != DrawingArrowheadType.None
                    ? new DrawingArrowhead(ta.Type, ta.Width, ta.Length)
                    : null,
                ShapeText = shapeDto.ShapeText,
                ShapeTextFontSizePoints = shapeDto.ShapeTextFontSizePoints,
                ShapeTextBold = shapeDto.ShapeTextBold,
                ShapeTextItalic = shapeDto.ShapeTextItalic,
                ShapeTextUnderline = shapeDto.ShapeTextUnderline,
                ShapeTextColor = shapeDto.ShapeTextColor is { } stc ? ParseColor(stc) : null,
                ShapeTextThemeColor = ToThemeColorReference(shapeDto.ShapeTextThemeColor),
                ShapeTextHAlign = ValidEnumOrDefault(shapeDto.ShapeTextHAlign, DrawingShapeTextHAlign.Left),
                ShapeTextVAnchor = ValidEnumOrDefault(shapeDto.ShapeTextVAnchor, DrawingShapeTextVAnchor.Middle),
                ShapeTextWrap = shapeDto.ShapeTextWrap,
                IsWordArt = shapeDto.IsWordArt,
                WarpPreset = shapeDto.WarpPreset,
                ShapeTextGradientEndColor = shapeDto.ShapeTextGradientEndColor is { } gradEnd ? ParseColor(gradEnd) : null,
                ShapeTextGradientEndThemeColor = ToThemeColorReference(shapeDto.ShapeTextGradientEndThemeColor),
                ShapeTextGradientAngle = shapeDto.ShapeTextGradientAngle,
                ShapeTextOutlineColor = shapeDto.ShapeTextOutlineColor is { } outlineC ? ParseColor(outlineC) : null,
                ShapeTextOutlineThemeColor = ToThemeColorReference(shapeDto.ShapeTextOutlineThemeColor),
                ShapeTextOutlineWidthPoints = shapeDto.ShapeTextOutlineWidthPoints,
                // R127B-native-fxl-editas-parity: see the matching comment on PictureDto's DrawingAnchorKind.
                DrawingAnchorKind = ValidEnumOrDefault(shapeDto.DrawingAnchorKind, ChartDrawingAnchorKind.TwoCell)
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

    private static Guid ExistingOrNewId(Guid? id) =>
        id is { } value && value != Guid.Empty ? value : Guid.NewGuid();

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
    public Guid? Id { get; set; }
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
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public bool IsVisible { get; set; } = true;
    public double CropLeft { get; set; }
    public double CropTop { get; set; }
    public double CropRight { get; set; }
    public double CropBottom { get; set; }
    public string? AltText { get; set; }
    public string? Title { get; set; }
    public bool IsDecorative { get; set; }
    // R127B-native-fxl-editas-parity: mirrors ChartDto.DrawingAnchorKind (NativeJsonAdapter.ChartDto.cs)
    // -- round-trips the oneCellAnchor/twoCellAnchor/absoluteAnchor "move/size with cells" kind through
    // the native .fxl format so it survives a Save As .fxl / reopen. Defaults to TwoCell, matching
    // PictureModel.DrawingAnchorKind's own default, so pre-existing .fxl files without this field keep
    // their prior always-move-and-size behavior.
    public ChartDrawingAnchorKind DrawingAnchorKind { get; set; } = ChartDrawingAnchorKind.TwoCell;
    public List<PictureCellDto> Cells { get; set; } = [];
}

internal class PictureCellDto
{
    public uint RowOffset { get; set; }
    public uint ColumnOffset { get; set; }
    public string? Text { get; set; }
    public NativeJsonAdapter.CellStyleDto? Style { get; set; }
    public bool IsNumericOrDate { get; set; }
}

internal class TextBoxDto
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? Anchor { get; set; }
    public string? Text { get; set; }
    public double Width { get; set; } = 180;
    public double Height { get; set; } = 80;
    public double RotationDegrees { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool HasFill { get; set; } = true;
    public string? FillColor { get; set; }
    public string? OutlineColor { get; set; }
    public ThemeColorReferenceDto? FillThemeColor { get; set; }
    public ThemeColorReferenceDto? OutlineThemeColor { get; set; }
    // R91-commands-insert-object-5-1: mirrors DrawingShapeDto.OutlineHasNoFill; defaults false so
    // pre-existing .fxl files (saved before this field existed) keep drawing a border.
    public bool OutlineHasNoFill { get; set; }
    public string? Title { get; set; }
    public string? AltText { get; set; }
    // ── Text formatting (txBody) — mirrors DrawingShapeDto's ShapeText* fields ──────────────
    public string? TextFontFamily { get; set; }
    public double TextFontSizePoints { get; set; }
    public bool TextBold { get; set; }
    public bool TextItalic { get; set; }
    public string? TextColor { get; set; }
    public ThemeColorReferenceDto? TextThemeColor { get; set; }
    public DrawingShapeTextHAlign TextHAlign { get; set; } = DrawingShapeTextHAlign.Left;
    public DrawingShapeTextVAnchor TextVAnchor { get; set; } = DrawingShapeTextVAnchor.Top;
    // R127B-native-fxl-editas-parity: see the matching comment on PictureDto.DrawingAnchorKind.
    public ChartDrawingAnchorKind DrawingAnchorKind { get; set; } = ChartDrawingAnchorKind.TwoCell;
}

internal class DrawingShapeDto
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? Anchor { get; set; }
    public DrawingShapeKind Kind { get; set; } = DrawingShapeKind.Rectangle;
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 70;
    public double RotationDegrees { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool HasFill { get; set; } = true;
    public string? FillColor { get; set; }
    public string? OutlineColor { get; set; }
    public string? GradientFillEndColor { get; set; }
    public DrawingShapeGradientDirection GradientFillDirection { get; set; } = DrawingShapeGradientDirection.DiagonalDown;
    public ThemeColorReferenceDto? FillThemeColor { get; set; }
    public ThemeColorReferenceDto? OutlineThemeColor { get; set; }
    public bool HasShadowEffect { get; set; }
    public DrawingShapeEffectPreset EffectPreset { get; set; }
    public string? Title { get; set; }
    public string? AltText { get; set; }
    public double OutlineWidthPoints { get; set; }
    public bool OutlineHasNoFill { get; set; }
    public DrawingShapeOutlineDash OutlineDash { get; set; } = DrawingShapeOutlineDash.Solid;
    // ── Arrowheads for line-like shapes ──────────────────────────────────
    public ArrowheadDto? HeadArrowhead { get; set; }
    public ArrowheadDto? TailArrowhead { get; set; }
    // ── Shape text ────────────────────────────────────────────────────────
    public string? ShapeText { get; set; }
    public double ShapeTextFontSizePoints { get; set; }
    public bool ShapeTextBold { get; set; }
    public bool ShapeTextItalic { get; set; }
    public bool ShapeTextUnderline { get; set; }
    public string? ShapeTextColor { get; set; }
    public ThemeColorReferenceDto? ShapeTextThemeColor { get; set; }
    public DrawingShapeTextHAlign ShapeTextHAlign { get; set; } = DrawingShapeTextHAlign.Left;
    public DrawingShapeTextVAnchor ShapeTextVAnchor { get; set; } = DrawingShapeTextVAnchor.Middle;
    public bool ShapeTextWrap { get; set; } = true;
    // ── WordArt ──────────────────────────────────────────────────────────
    public bool IsWordArt { get; set; }
    public string? WarpPreset { get; set; }
    public string? ShapeTextGradientEndColor { get; set; }
    public ThemeColorReferenceDto? ShapeTextGradientEndThemeColor { get; set; }
    public long ShapeTextGradientAngle { get; set; } = 5400000;
    public string? ShapeTextOutlineColor { get; set; }
    public ThemeColorReferenceDto? ShapeTextOutlineThemeColor { get; set; }
    public double ShapeTextOutlineWidthPoints { get; set; }
    // R127B-native-fxl-editas-parity: see the matching comment on PictureDto.DrawingAnchorKind.
    public ChartDrawingAnchorKind DrawingAnchorKind { get; set; } = ChartDrawingAnchorKind.TwoCell;
}

internal class ThemeColorReferenceDto
{
    public WorkbookThemeColorSlot Slot { get; set; }
    public double Tint { get; set; }
}

internal class ArrowheadDto
{
    public DrawingArrowheadType Type { get; set; }
    public DrawingArrowheadSize Width { get; set; } = DrawingArrowheadSize.Medium;
    public DrawingArrowheadSize Length { get; set; } = DrawingArrowheadSize.Medium;
}
