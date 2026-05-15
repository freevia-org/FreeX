namespace Freexcel.Core.Model;

public enum PictureKind
{
    CellRangeSnapshot,
    Image
}

public sealed class PictureModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public CellAddress Anchor { get; set; }
    public PictureKind Kind { get; set; } = PictureKind.CellRangeSnapshot;
    public uint SourceRowCount { get; set; }
    public uint SourceColumnCount { get; set; }
    public List<PictureCellSnapshot> Cells { get; } = [];
    public byte[]? ImageBytes { get; set; }
    public string? ContentType { get; set; }
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 140;
    public double RotationDegrees { get; set; }
}

public sealed record PictureCellSnapshot(uint RowOffset, uint ColumnOffset, string Text);
