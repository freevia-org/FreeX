namespace FreeX.Core.Model;

public enum PictureKind
{
    CellRangeSnapshot,
    Image
}

public sealed class PictureModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Name { get; set; }
    public CellAddress Anchor { get; set; }

    /// <summary>Horizontal sub-cell offset (in DIP pixels, EMU/9525) from the left edge of the
    /// <see cref="Anchor"/> cell to the picture's left edge, preserved from the authored anchor's
    /// <c>from/colOff</c> so the render reflects the true sub-cell position.</summary>
    public double AnchorOffsetX { get; set; }

    /// <summary>Vertical sub-cell offset (in DIP pixels, EMU/9525) from the top edge of the
    /// <see cref="Anchor"/> cell to the picture's top edge, preserved from the authored anchor's
    /// <c>from/rowOff</c>.</summary>
    public double AnchorOffsetY { get; set; }

    public PictureKind Kind { get; set; } = PictureKind.CellRangeSnapshot;
    public uint SourceRowCount { get; set; }
    public uint SourceColumnCount { get; set; }
    public bool IsLinkedToSourceRange { get; set; }
    public GridRange? LinkedSourceRange { get; set; }
    public string? LinkedSourceSheetName { get; set; }
    public List<PictureCellSnapshot> Cells { get; } = [];
    public byte[]? ImageBytes { get; set; }
    public string? ContentType { get; set; }
    public string? Title { get; set; }
    public string? AltText { get; set; }
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
    public bool IsSourceLoaded { get; set; }
}

public sealed record PictureCellSnapshot(
    uint RowOffset,
    uint ColumnOffset,
    string Text,
    CellStyle? Style = null,
    bool IsNumericOrDate = false);
