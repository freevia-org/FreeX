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

    /// <summary>
    /// R90-app-accessibility-checker-5-2: true when the user explicitly marked this picture
    /// "decorative" (Excel's Alt Text pane "Mark as decorative" checkbox), from the
    /// <c>&lt;xdr:cNvPr&gt;&lt;a:extLst&gt;&lt;a:ext uri="{C183D7F6-B498-43B3-948B-1728B52AA6E4}"&gt;
    /// &lt;adec:decorative val="1"/&gt;</c> extension. A decorative picture is intentionally
    /// content-free (e.g. a divider graphic) and is exempt from the Accessibility Checker's
    /// "Missing alternative text" rule even when <see cref="AltText"/>/<see cref="Title"/>/
    /// <see cref="Name"/> are all blank -- matching real Excel's own Accessibility Checker.
    /// </summary>
    public bool IsDecorative { get; set; }
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

    /// <summary>
    /// The external relationship target (verbatim, e.g. an absolute path/URI such as
    /// <c>"file:///C:/Images/photo.png"</c>) for a picture inserted via Excel's "Link to File" — an
    /// <c>&lt;xdr:pic&gt;</c> whose <c>&lt;a:blip&gt;</c> carries <c>r:link</c> instead of <c>r:embed</c>,
    /// with no corresponding image part embedded in the package. Null for a normal embedded picture
    /// (the common case). A non-null value means <see cref="ImageBytes"/> is empty/null — there is
    /// nothing embedded to load — and this picture must round-trip its <c>r:link</c> + external
    /// relationship on save instead of being written (or silently dropped) as an embedded picture.
    /// See R65-io-image-drawing-6-1.
    /// </summary>
    public string? LinkedImageTarget { get; set; }

    /// <summary>
    /// The raw bytes of the vector <c>.svg</c> media part backing this picture, when it was inserted
    /// via Excel's Insert &gt; Icons/SVG -- Excel always keeps a PNG rasterization in
    /// <see cref="ImageBytes"/>/<see cref="ContentType"/> as the universal-compatibility fallback, but
    /// carries this vector original alongside it (via the <c>a:blip/a:extLst</c> <c>asvg:svgBlip</c>
    /// extension) so the picture stays editable as a vector in Excel's "Graphics Format" ribbon tab
    /// (recolor, "Convert to Shape"). Null for every ordinary raster picture. Must be re-emitted
    /// alongside the PNG fallback on save or the picture permanently downgrades to a flat raster the
    /// first time it is edited. See R80-io-drawing-image-5-3.
    /// </summary>
    public byte[]? SvgImageBytes { get; set; }
}

public sealed record PictureCellSnapshot(
    uint RowOffset,
    uint ColumnOffset,
    string Text,
    CellStyle? Style = null,
    bool IsNumericOrDate = false);
