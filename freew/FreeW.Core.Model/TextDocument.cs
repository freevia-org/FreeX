namespace FreeW.Core.Model;

/// <summary>
/// How an image relates to the surrounding text. <see cref="Inline"/> (the default) keeps the image in
/// the text flow, serialised as <c>wp:inline</c> exactly as before. The remaining modes make the image
/// <em>floating</em> (serialised as <c>wp:anchor</c>) with the matching OOXML wrap element:
/// <see cref="Square"/> → <c>wp:wrapSquare</c>; <see cref="Tight"/> → <c>wp:wrapTight</c> (no wrap polygon —
/// a deliberate simplification); <see cref="TopAndBottom"/> → <c>wp:wrapTopAndBottom</c>;
/// <see cref="Behind"/> → <c>wp:wrapNone</c> with <c>behindDoc="1"</c> (behind the text);
/// <see cref="InFront"/> → <c>wp:wrapNone</c> with <c>behindDoc="0"</c> (in front of the text).
/// </summary>
public enum ImageWrapping
{
    Inline,
    Square,
    Tight,
    TopAndBottom,
    Behind,
    InFront
}

/// <summary>
/// The horizontal frame a floating image's offset is measured from (<c>wp:positionH/@relativeFrom</c>).
/// Maps to "column" / "margin" / "page". Defaults to <see cref="Column"/>.
/// </summary>
public enum HorizontalAnchor
{
    Column,
    Margin,
    Page
}

/// <summary>
/// The vertical frame a floating image's offset is measured from (<c>wp:positionV/@relativeFrom</c>).
/// Maps to "paragraph" / "margin" / "page". Defaults to <see cref="Paragraph"/>.
/// </summary>
public enum VerticalAnchor
{
    Paragraph,
    Margin,
    Page
}

/// <summary>
/// The raster image format an <see cref="InlineImage"/> carries. Determines the media-part extension /
/// content-type the writer emits and is recovered on read from the relationship target's extension and/or
/// the bytes' magic number. <see cref="Png"/> is the historical default so existing images are unchanged.
/// EMF/WMF are vector metafiles rather than raster, but are carried the same way (Word embeds them as
/// pictures) so arbitrary picture formats round-trip without transcoding.
/// </summary>
public enum ImageFormat
{
    Png,
    Jpeg,
    Gif,
    Bmp,
    Tiff,
    Emf,
    Wmf
}

/// <summary>
/// An inline image carried by a <see cref="Run"/>. Modelled at the run level (rather than as
/// a block) so it round-trips through docx as an inline w:drawing without touching paragraph storage.
/// Carries the original image bytes plus their <see cref="Format"/>, so pictures in any supported format
/// (PNG/JPEG/GIF/BMP/TIFF/EMF/WMF) round-trip verbatim — they are never transcoded. Size is in points to
/// match the rest of the FreeW unit model.
///
/// By default an image is inline (<see cref="ImageWrapping.Inline"/>) and serialises as <c>wp:inline</c>
/// exactly as before. Setting <see cref="Wrapping"/> to a floating mode makes it serialise as a
/// <c>wp:anchor</c> positioned by <see cref="HorizontalOffsetPt"/>/<see cref="VerticalOffsetPt"/> relative
/// to <see cref="HorizontalAnchor"/>/<see cref="VerticalAnchor"/>. The position fields are ignored for an
/// inline image, so existing inline-image construction and round-trips are fully unaffected.
/// </summary>
public sealed class InlineImage(byte[] bytes, double widthPt, double heightPt, ImageFormat format = ImageFormat.Png)
{
    /// <summary>The raw image bytes, stored verbatim in their original <see cref="Format"/>.</summary>
    public byte[] Bytes { get; } = bytes;

    /// <summary>
    /// The image's binary format. Defaults to <see cref="ImageFormat.Png"/> so existing construction is
    /// unchanged. The writer emits the media part with the matching extension/content-type, and the reader
    /// recovers it from the part extension and/or the bytes' magic number.
    /// </summary>
    public ImageFormat Format { get; } = format;

    /// <summary>
    /// Backward-compatible alias for <see cref="Bytes"/> (the image was historically PNG-only). Kept so
    /// existing callers/tests that read <c>PngBytes</c> still compile; it returns the raw bytes whatever the
    /// actual <see cref="Format"/> is.
    /// </summary>
    public byte[] PngBytes => Bytes;

    public double WidthPt { get; set; } = widthPt;
    public double HeightPt { get; set; } = heightPt;

    /// <summary>
    /// Detects an <see cref="ImageFormat"/> from the leading magic bytes of <paramref name="bytes"/>,
    /// falling back to <see cref="ImageFormat.Png"/> for empty/unrecognised data (so callers always get a
    /// usable format). Recognises PNG (89 50 4E 47), JPEG (FF D8 FF), GIF (47 49 46 38), BMP (42 4D),
    /// TIFF (49 49 2A 00 / 4D 4D 00 2A), EMF (01 00 00 00 … " EMF" at offset 40) and the WMF placeable
    /// header (D7 CD C6 9A) / classic WMF header (01 00 09 00 / 02 00 09 00).
    /// </summary>
    public static ImageFormat DetectFormat(byte[] bytes)
    {
        if (bytes is null || bytes.Length < 2)
            return ImageFormat.Png;

        bool Starts(params byte[] sig)
        {
            if (bytes.Length < sig.Length)
                return false;
            for (var i = 0; i < sig.Length; i++)
                if (bytes[i] != sig[i])
                    return false;
            return true;
        }

        if (Starts(0x89, 0x50, 0x4E, 0x47))
            return ImageFormat.Png;
        if (Starts(0xFF, 0xD8, 0xFF))
            return ImageFormat.Jpeg;
        if (Starts(0x47, 0x49, 0x46, 0x38))
            return ImageFormat.Gif;
        if (Starts(0x42, 0x4D))
            return ImageFormat.Bmp;
        if (Starts(0x49, 0x49, 0x2A, 0x00) || Starts(0x4D, 0x4D, 0x00, 0x2A))
            return ImageFormat.Tiff;
        // EMF: a 0x00000001 record type then, at byte offset 40, the ASCII signature " EMF".
        if (Starts(0x01, 0x00, 0x00, 0x00) && bytes.Length >= 44
            && bytes[40] == 0x20 && bytes[41] == 0x45 && bytes[42] == 0x4D && bytes[43] == 0x46)
            return ImageFormat.Emf;
        // WMF: the placeable-metafile header (D7 CD C6 9A) or a classic WMF header (01/02 00 09 00).
        if (Starts(0xD7, 0xCD, 0xC6, 0x9A) || Starts(0x01, 0x00, 0x09, 0x00) || Starts(0x02, 0x00, 0x09, 0x00))
            return ImageFormat.Wmf;

        return ImageFormat.Png;
    }

    /// <summary>
    /// The lower-case media-part file extension (no dot) for an <see cref="ImageFormat"/>, e.g.
    /// <c>"png"</c>, <c>"jpeg"</c>. Used by the writer to name <c>imageN.&lt;ext&gt;</c> and to emit the
    /// matching <c>[Content_Types].xml</c> Default.
    /// </summary>
    public static string ExtensionFor(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "jpeg",
        ImageFormat.Gif => "gif",
        ImageFormat.Bmp => "bmp",
        ImageFormat.Tiff => "tiff",
        ImageFormat.Emf => "emf",
        ImageFormat.Wmf => "wmf",
        _ => "png"
    };

    /// <summary>
    /// Maps a media-part file extension (with or without a leading dot, case-insensitive) to an
    /// <see cref="ImageFormat"/>. Recognises both <c>jpg</c> and <c>jpeg</c>, and <c>tif</c>/<c>tiff</c>.
    /// Returns null for an unknown/empty extension so the caller can fall back to magic-byte detection.
    /// </summary>
    public static ImageFormat? FormatForExtension(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
            return null;
        return extension.TrimStart('.').ToLowerInvariant() switch
        {
            "png" => ImageFormat.Png,
            "jpg" or "jpeg" => ImageFormat.Jpeg,
            "gif" => ImageFormat.Gif,
            "bmp" => ImageFormat.Bmp,
            "tif" or "tiff" => ImageFormat.Tiff,
            "emf" => ImageFormat.Emf,
            "wmf" => ImageFormat.Wmf,
            _ => null
        };
    }

    /// <summary>
    /// Optional alternative text (accessibility description). When set it round-trips through docx as
    /// the <c>wp:docPr/@descr</c> attribute and surfaces as the editor tooltip / automation name.
    /// Defaults to null so existing image construction and round-trips are unaffected.
    /// </summary>
    public string? AltText { get; set; }

    /// <summary>
    /// How the image relates to the surrounding text. Defaults to <see cref="ImageWrapping.Inline"/> so
    /// existing images serialise as <c>wp:inline</c> unchanged; any other value makes the image floating
    /// (<c>wp:anchor</c>) with the matching wrap element.
    /// </summary>
    public ImageWrapping Wrapping { get; set; } = ImageWrapping.Inline;

    /// <summary>True when the image is floating (i.e. not <see cref="ImageWrapping.Inline"/>).</summary>
    public bool IsFloating => Wrapping != ImageWrapping.Inline;

    /// <summary>
    /// Horizontal offset in points from <see cref="HorizontalAnchor"/> for a floating image
    /// (<c>wp:positionH/wp:posOffset</c>). Ignored when <see cref="Wrapping"/> is
    /// <see cref="ImageWrapping.Inline"/>. Defaults to 0.
    /// </summary>
    public double HorizontalOffsetPt { get; set; }

    /// <summary>
    /// Vertical offset in points from <see cref="VerticalAnchor"/> for a floating image
    /// (<c>wp:positionV/wp:posOffset</c>). Ignored when <see cref="Wrapping"/> is
    /// <see cref="ImageWrapping.Inline"/>. Defaults to 0.
    /// </summary>
    public double VerticalOffsetPt { get; set; }

    /// <summary>The frame the horizontal offset is measured from (<c>wp:positionH/@relativeFrom</c>).</summary>
    public HorizontalAnchor HorizontalAnchor { get; set; } = HorizontalAnchor.Column;

    /// <summary>The frame the vertical offset is measured from (<c>wp:positionV/@relativeFrom</c>).</summary>
    public VerticalAnchor VerticalAnchor { get; set; } = VerticalAnchor.Paragraph;
}

/// <summary>
/// A contiguous span of text sharing one run formatting, or — when <see cref="Image"/> is set — an
/// inline image anchored in the run flow. An image run carries no text (<see cref="Text"/> is empty).
/// </summary>
public sealed class Run(string text, RunFormatting? formatting = null)
{
    public string Text { get; set; } = text;
    public RunFormatting Formatting { get; set; } = formatting ?? RunFormatting.Default;

    /// <summary>Optional inline image. When non-null this run renders/serialises as a picture.</summary>
    public InlineImage? Image { get; set; }

    /// <summary>
    /// Optional inline mathematical equation (OMML). When non-null this run is an inline equation rather
    /// than literal text: on save it serialises as an inline <c>m:oMath</c> in the run sequence (instead
    /// of a <c>w:r/w:t</c>), and the run's <see cref="Text"/> mirrors the equation's linear form so
    /// field-/math-unaware consumers still render something readable. Modelled at the run level — mirroring
    /// <see cref="Image"/> and the other optional run marks — so equations round-trip through the existing
    /// run flow without introducing a new block type.
    /// </summary>
    public Equation? Equation { get; set; }

    /// <summary>Creates a run that carries an inline equation. Its <see cref="Text"/> mirrors the linear form.</summary>
    public static Run FromEquation(Equation equation) =>
        new(equation.LinearText) { Equation = equation };

    /// <summary>
    /// Optional inline DrawingML shape or text box. When non-null this run serialises as an inline
    /// <c>w:drawing</c> wrapping a <c>wps:wsp</c> (preset geometry + optional fill + optional text-box
    /// content) rather than literal text, and the run carries no <see cref="Text"/> of its own (for a text
    /// box, <see cref="Text"/> mirrors the box's plain text so shape-unaware consumers still render
    /// something). Modelled at the run level — mirroring <see cref="Image"/> and <see cref="Equation"/> —
    /// so shapes round-trip through the existing run flow without a new block type.
    /// </summary>
    public Shape? Shape { get; set; }

    /// <summary>
    /// Creates a run that carries an inline shape. For a text box the run's <see cref="Text"/> mirrors the
    /// box's plain text; a plain (text-less) shape carries an empty <see cref="Text"/>.
    /// </summary>
    public static Run FromShape(Shape shape) =>
        new(shape.HasText ? shape.PlainText : string.Empty) { Shape = shape };

    /// <summary>
    /// Optional inline WordArt (decorative text). When non-null this run serialises as an inline
    /// <c>w:drawing</c> wrapping a <c>wps:wsp</c> text box whose run carries DrawingML text effects (fill
    /// gradient / outline / shadow chosen by the WordArt style preset) on its <c>a:rPr</c>, rather than
    /// literal text. The run's <see cref="Text"/> mirrors the WordArt text so effect-unaware consumers still
    /// render something. Modelled at the run level — mirroring <see cref="Shape"/> and <see cref="Image"/> —
    /// so WordArt round-trips through the existing run flow without a new block type.
    /// </summary>
    public WordArt? WordArt { get; set; }

    /// <summary>Creates a run that carries inline WordArt. Its <see cref="Text"/> mirrors the WordArt text.</summary>
    public static Run FromWordArt(WordArt wordArt) =>
        new(wordArt.Text) { WordArt = wordArt };

    /// <summary>
    /// Optional inline chart (DrawingML). When non-null this run is an inline chart rather than literal
    /// text: on save it serialises as a separate chart part (<c>word/charts/chartN.xml</c>) referenced by an
    /// inline <c>w:drawing</c> in the run sequence, exactly as <see cref="Image"/> serialises a picture.
    /// Carries no literal text of its own. Modelled at the run level — mirroring <see cref="Image"/> and
    /// <see cref="Equation"/> — so charts round-trip through the existing run flow without a new block type.
    /// </summary>
    public Chart? Chart { get; set; }

    /// <summary>Creates a run that carries an inline chart instead of text.</summary>
    public static Run FromChart(Chart chart) => new(string.Empty) { Chart = chart };

    /// <summary>
    /// Optional inline embedded OLE object (e.g. an embedded Excel sheet). When non-null this run is an
    /// embedded object rather than literal text: on save it serialises as a classic <c>w:object</c> wrapping
    /// a VML <c>v:shape</c>/<c>o:OLEObject</c>, with the payload bytes written to a separate embeddings part
    /// (<c>word/embeddings/oleObjectN.bin</c>) referenced by relationship id and the presentation icon
    /// written as a media part — mirroring how <see cref="Chart"/> and <see cref="Image"/> serialise as
    /// referenced parts. Carries no literal text of its own. Modelled at the run level — mirroring
    /// <see cref="Chart"/> and <see cref="Image"/> — so embedded objects round-trip through the existing run
    /// flow without a new block type.
    /// </summary>
    public EmbeddedObject? EmbeddedObject { get; set; }

    /// <summary>Creates a run that carries an inline embedded OLE object instead of text.</summary>
    public static Run FromEmbeddedObject(EmbeddedObject embeddedObject) =>
        new(string.Empty) { EmbeddedObject = embeddedObject };

    /// <summary>
    /// Optional inline SmartArt / DrawingML diagram. When non-null this run is an inline diagram rather than
    /// literal text: on save it serialises as four diagram parts
    /// (<c>word/diagrams/{data,layout,quickStyle,colors}N.xml</c>) referenced by an inline <c>w:drawing</c>
    /// whose <c>dgm:relIds</c> holds the four relationship ids — the node texts/hierarchy live in the data
    /// part, exactly as <see cref="Chart"/> serialises a chart part. Carries no literal text of its own.
    /// Modelled at the run level — mirroring <see cref="Chart"/> and <see cref="Image"/> — so diagrams
    /// round-trip through the existing run flow without a new block type.
    /// </summary>
    public SmartArt? SmartArt { get; set; }

    /// <summary>Creates a run that carries an inline SmartArt diagram instead of text.</summary>
    public static Run FromSmartArt(SmartArt smartArt) => new(string.Empty) { SmartArt = smartArt };

    /// <summary>
    /// Optional verbatim-preserved inline drawing FreeW does not model (e.g. a <c>w:drawing</c> referencing a
    /// <c>chart</c>/<c>chartex</c> part whose structure FreeW's reader does not recognise as a
    /// <see cref="Chart"/>). When non-null this run re-emits the captured drawing XML unchanged inside the run,
    /// rather than dropping it — keeping the inline reference alive while the chart part(s) + media it references
    /// survive as <see cref="PreservedParts.Parts"/>. Carries no literal text of its own. Modelled at the run
    /// level — mirroring <see cref="Chart"/> — so an unread chart round-trips instead of vanishing.
    /// </summary>
    public PreservedDrawing? PreservedDrawing { get; set; }

    /// <summary>Creates a run that re-emits a verbatim-preserved inline drawing instead of text.</summary>
    public static Run FromPreservedDrawing(PreservedDrawing drawing) =>
        new(string.Empty) { PreservedDrawing = drawing };

    /// <summary>
    /// Optional external hyperlink target (absolute URL). When non-null the run is wrapped in a
    /// w:hyperlink on save, with the URL stored as an external relationship, and rendered as a link.
    /// Mutually exclusive with <see cref="HyperlinkAnchor"/>: a run links either externally or
    /// internally, never both.
    /// </summary>
    public string? HyperlinkUrl { get; set; }

    /// <summary>
    /// Optional internal hyperlink target: the name of a bookmark elsewhere in this document (see
    /// <see cref="Paragraph.BookmarkName"/>). When non-null the run is wrapped in a
    /// w:hyperlink w:anchor="…" on save (no relationship) and rendered as a link that jumps to the
    /// bookmark. Mutually exclusive with <see cref="HyperlinkUrl"/>.
    /// </summary>
    public string? HyperlinkAnchor { get; set; }

    /// <summary>
    /// Optional ScreenTip (tooltip) shown when hovering the hyperlink. Applies to either an external
    /// (<see cref="HyperlinkUrl"/>) or internal (<see cref="HyperlinkAnchor"/>) link. When set it
    /// serialises as the <c>w:tooltip</c> attribute on the wrapping <c>w:hyperlink</c>. Defaults to
    /// null so existing hyperlinks (without a ScreenTip) round-trip unchanged.
    /// </summary>
    public string? HyperlinkTooltip { get; set; }

    /// <summary>
    /// When set, this run is a simple field rather than literal text — e.g. a PAGE field whose value
    /// is the current page number. The run's <see cref="Text"/> doubles as cached/fallback display
    /// text (the last computed value), so non-field-aware consumers still render something sensible.
    /// </summary>
    public RunFieldKind FieldKind { get; set; } = RunFieldKind.None;

    /// <summary>
    /// When non-null, this run is a table-cell formula field (Word's Table &gt; Data &gt; Formula) — e.g.
    /// <c>=SUM(ABOVE)</c> with an optional number format. It serialises as a <c>w:fldSimple</c> whose
    /// <c>w:instr</c> is <c> =SUM(ABOVE) \# "#,##0.00" </c> wrapping a cached result run; the run's
    /// <see cref="Text"/> doubles as that cached/last-computed result so field-unaware consumers still render
    /// a value. Modelled as an optional run mark, mirroring <see cref="FieldKind"/>, so the field round-trips
    /// through the existing run flow without a new block type.
    /// </summary>
    public TableFormulaField? TableFormula { get; set; }

    /// <summary>Creates a table-formula field run carrying the cached result as its <see cref="Text"/>.</summary>
    public static Run TableFormulaFieldRun(TableFormulaField formula, string cachedResult = "", RunFormatting? formatting = null) =>
        new(cachedResult, formatting) { TableFormula = formula };

    /// <summary>
    /// When non-null, this run is a hidden Mark Citation field (Word's References &gt; Mark Citation) — the
    /// invisible <c>TA</c> field that records a legal citation for a Table of Authorities. It serialises as a
    /// <c>w:fldSimple</c> whose <c>w:instr</c> is the TA instruction (<c> TA \l "long" \s "short" \c N </c>)
    /// wrapping a vanished run, so it round-trips like Word's and produces no visible glyph. The same data is
    /// also mirrored into <see cref="TextDocument.Citations"/> for building the table. Modelled as an optional
    /// run mark, mirroring <see cref="TableFormula"/>, so it round-trips without a new block type. The run
    /// carries no literal text, so it produces no visible glyph — matching Word's hidden TA field.
    /// </summary>
    public Citation? Citation { get; set; }

    /// <summary>Creates a hidden Mark Citation (TA) field run for <paramref name="citation"/>.</summary>
    public static Run CitationMark(Citation citation) =>
        new(string.Empty) { Citation = citation };

    /// <summary>
    /// When non-null, this run is a cross-reference field (Word's References &gt; Cross-reference) — a
    /// <c>REF</c>/<c>PAGEREF</c>/<c>NOTEREF</c> field over a bookmark name or note id, optionally as a
    /// clickable hyperlink. It serialises as a <c>w:fldSimple</c> whose <c>w:instr</c> is the field
    /// instruction (e.g. <c> REF _Ref1 \h </c>) wrapping a cached result run; the run's
    /// <see cref="Text"/> doubles as that cached/last-resolved display text so field-unaware consumers
    /// still render a value. Modelled as an optional run mark, mirroring <see cref="TableFormula"/> and
    /// <see cref="Citation"/>, so it round-trips without a new block type.
    /// </summary>
    public CrossReferenceField? CrossReference { get; set; }

    /// <summary>
    /// Creates a cross-reference field run carrying the cached resolved text as its <see cref="Text"/>.
    /// </summary>
    public static Run CrossReferenceFieldRun(CrossReferenceField field, string cached = "", RunFormatting? formatting = null) =>
        new(cached, formatting) { CrossReference = field };

    /// <summary>
    /// When set, this run is a footnote reference marker pointing at the footnote with this id in
    /// <see cref="TextDocument.Footnotes"/>. It carries no literal text of its own; the marker number
    /// is the id. Serialises as a superscript run wrapping a w:footnoteReference w:id="N".
    /// </summary>
    public int? FootnoteId { get; set; }

    /// <summary>
    /// When set, this run is an endnote reference marker pointing at the endnote with this id in
    /// <see cref="TextDocument.Endnotes"/>. It carries no literal text of its own; the marker number
    /// is the id. Serialises as a superscript run wrapping a w:endnoteReference w:id="N". Mirrors
    /// <see cref="FootnoteId"/> but collected at the document end (word/endnotes.xml).
    /// </summary>
    public int? EndnoteId { get; set; }

    /// <summary>
    /// When set, this run is covered by the review comment with this id in
    /// <see cref="TextDocument.Comments"/>. The covered span serialises with a w:commentRangeStart /
    /// w:commentRangeEnd pair bracketing the run(s), and a trailing reference run (see
    /// <see cref="IsCommentReference"/>) carries the w:commentReference. Consecutive runs sharing the
    /// same id form one comment range.
    /// </summary>
    public int? CommentId { get; set; }

    /// <summary>
    /// When true together with <see cref="CommentId"/>, this run is the comment's anchor marker — it
    /// carries no literal text and serialises as a run wrapping w:commentReference w:id="N". One such
    /// run is emitted immediately after the commented range's w:commentRangeEnd.
    /// </summary>
    public bool IsCommentReference { get; set; }

    /// <summary>
    /// When true, this run is a manual page break (<c>w:br w:type="page"</c>): it carries no text and
    /// forces the following content onto a new page, mirroring Ctrl+Enter in Word. Modelled as an optional
    /// run mark like <see cref="IsCommentReference"/>; on save it serialises as a run wrapping
    /// <c>w:br w:type="page"</c>, and the editor splits the paragraph at the break so the WPF paginator
    /// starts a new page. Dropping these (the previous behaviour) made FreeW under-paginate badly versus
    /// Word (e.g. a page-break-only document collapsed to a single page).
    /// </summary>
    public bool IsPageBreak { get; set; }

    /// <summary>
    /// Tracked-change (revision) mark on this run. <see cref="RevisionKind.None"/> is an ordinary run;
    /// <see cref="RevisionKind.Inserted"/> is a tracked insertion (serialises wrapped in w:ins, rendered
    /// underlined in the revision colour); <see cref="RevisionKind.Deleted"/> is a tracked deletion (the
    /// text is kept in the model but serialises wrapped in w:del with w:delText, rendered struck-through).
    /// Mirrors how <see cref="CommentId"/>/<see cref="FootnoteId"/> are modelled as optional run marks.
    /// </summary>
    public RevisionKind Revision { get; set; } = RevisionKind.None;

    /// <summary>
    /// Optional structured-document-tag (content control) mark. When non-null this run is the content
    /// of a content control: on save the run(s) sharing this control are wrapped in a w:sdt
    /// (w:sdtPr + w:sdtContent), and the editor renders the run with a shaded control region so it is
    /// visibly a control. Consecutive runs carrying the same <see cref="ContentControl"/> instance
    /// coalesce into one w:sdt, mirroring how w:ins/w:hyperlink wrap runs. For a checkbox the run's
    /// <see cref="Text"/> carries the checked/unchecked glyph (☒/☐) and the control's
    /// <see cref="ContentControl.Checked"/> records the state. Kept optional so existing runs are
    /// unaffected.
    /// </summary>
    public ContentControl? Control { get; set; }

    /// <summary>The revision author (w:author on w:ins/w:del). Null when the run carries no revision.</summary>
    public string? RevisionAuthor { get; set; }

    /// <summary>
    /// The revision timestamp as a W3CDTF string (the w:date on w:ins/w:del), or null when unset. Kept
    /// as an explicit string (never auto-stamped) so the writer stays deterministic, matching how
    /// <see cref="Comment.DateXml"/> is modelled.
    /// </summary>
    public string? RevisionDateXml { get; set; }

    /// <summary>Creates a run that carries an inline image instead of text.</summary>
    public static Run FromImage(InlineImage image) => new(string.Empty) { Image = image };

    /// <summary>Creates a manual page-break run (<c>w:br w:type="page"</c>).</summary>
    public static Run PageBreak() => new(string.Empty) { IsPageBreak = true };

    /// <summary>Creates a page-number field run (renders as the current page number).</summary>
    public static Run PageNumberField(RunFormatting? formatting = null) =>
        new("1", formatting) { FieldKind = RunFieldKind.PageNumber };

    /// <summary>
    /// Creates a DATE field run. <paramref name="cached"/> is the last-computed display text, kept as a
    /// fallback for field-unaware consumers; the app layer may resolve it to the current date at render.
    /// </summary>
    public static Run DateField(string cached = "", RunFormatting? formatting = null) =>
        new(cached, formatting) { FieldKind = RunFieldKind.Date };

    /// <summary>
    /// Creates a TIME field run. <paramref name="cached"/> is the last-computed display text, kept as a
    /// fallback for field-unaware consumers; the app layer may resolve it to the current time at render.
    /// </summary>
    public static Run TimeField(string cached = "", RunFormatting? formatting = null) =>
        new(cached, formatting) { FieldKind = RunFieldKind.Time };

    /// <summary>
    /// Creates a FILENAME field run. <paramref name="cached"/> is the last-computed display text, kept as
    /// a fallback; the app layer may resolve it to the current document's file name at render.
    /// </summary>
    public static Run FileNameField(string cached = "", RunFormatting? formatting = null) =>
        new(cached, formatting) { FieldKind = RunFieldKind.FileName };

    /// <summary>
    /// Creates an AUTHOR field run. <paramref name="cached"/> is the last-computed display text, kept as a
    /// fallback; the app layer may resolve it from <see cref="DocumentProperties.Author"/> at render.
    /// </summary>
    public static Run AuthorField(string cached = "", RunFormatting? formatting = null) =>
        new(cached, formatting) { FieldKind = RunFieldKind.Author };

    /// <summary>
    /// Creates a NUMPAGES field run. <paramref name="cached"/> is the last-computed display text, kept as
    /// a fallback; the app layer may resolve it to a best-effort page count at render.
    /// </summary>
    public static Run NumPagesField(string cached = "", RunFormatting? formatting = null) =>
        new(cached, formatting) { FieldKind = RunFieldKind.NumPages };

    /// <summary>
    /// Creates a footnote-reference run for the footnote with id <paramref name="footnoteId"/>. The
    /// run renders as a superscript marker; its <see cref="Text"/> mirrors the id for field-unaware
    /// consumers. The matching content lives in <see cref="TextDocument.Footnotes"/>.
    /// </summary>
    public static Run FootnoteReference(int footnoteId, RunFormatting? formatting = null) =>
        new(footnoteId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            formatting ?? new RunFormatting { VerticalAlign = VerticalAlign.Superscript })
        {
            FootnoteId = footnoteId
        };

    /// <summary>
    /// Creates an endnote-reference run for the endnote with id <paramref name="endnoteId"/>. The
    /// run renders as a superscript marker; its <see cref="Text"/> mirrors the id for field-unaware
    /// consumers. The matching content lives in <see cref="TextDocument.Endnotes"/>.
    /// </summary>
    public static Run EndnoteReference(int endnoteId, RunFormatting? formatting = null) =>
        new(endnoteId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            formatting ?? new RunFormatting { VerticalAlign = VerticalAlign.Superscript })
        {
            EndnoteId = endnoteId
        };

    /// <summary>
    /// Creates the textless anchor run for the comment with id <paramref name="commentId"/>. It
    /// serialises as a run wrapping a w:commentReference and is emitted just after the commented
    /// range's w:commentRangeEnd. The matching content lives in <see cref="TextDocument.Comments"/>.
    /// </summary>
    public static Run CommentReference(int commentId) =>
        new(string.Empty) { CommentId = commentId, IsCommentReference = true };

    /// <summary>
    /// Creates a plain-text content control run carrying <paramref name="text"/> as its content, tagged
    /// with the optional <paramref name="tag"/> / <paramref name="alias"/>. Serialises as a w:sdt
    /// (plain-text) wrapping the run.
    /// </summary>
    public static Run PlainTextControl(string text, string? tag = null, string? alias = null) =>
        new(text) { Control = new ContentControl(ContentControlKind.PlainText, tag, alias) };

    /// <summary>
    /// Creates a checkbox content control run. The run's <see cref="Text"/> is the checked (☒) or
    /// unchecked (☐) glyph matching <paramref name="checked"/>, and the control records the state.
    /// Serialises as a w:sdt with a checkbox w:sdtPr wrapping the glyph run.
    /// </summary>
    public static Run CheckBoxControl(bool @checked, string? tag = null, string? alias = null) =>
        new(@checked ? ContentControl.CheckedGlyph : ContentControl.UncheckedGlyph)
        {
            Control = new ContentControl(ContentControlKind.CheckBox, tag, alias, @checked)
        };

    /// <summary>
    /// Creates a rich-text content control run carrying <paramref name="text"/> as its content, tagged
    /// with the optional <paramref name="tag"/> / <paramref name="alias"/>. Serialises as a w:sdt
    /// (w:richText) wrapping the run.
    /// </summary>
    public static Run RichTextControl(string text, string? tag = null, string? alias = null) =>
        new(text) { Control = new ContentControl(ContentControlKind.RichText, tag, alias) };

    /// <summary>
    /// Creates a date-picker content control run. The run's <see cref="Text"/> is the displayed date text
    /// and <paramref name="dateFormat"/> is the control's w:dateFormat (defaults to <see
    /// cref="ContentControl.DefaultDateFormat"/>). Serialises as a w:sdt with a w:date w:sdtPr.
    /// </summary>
    public static Run DatePickerControl(
        string text, string? tag = null, string? alias = null, string? dateFormat = null) =>
        new(text)
        {
            Control = new ContentControl(
                ContentControlKind.DatePicker, tag, alias,
                DateFormat: dateFormat ?? ContentControl.DefaultDateFormat)
        };

    /// <summary>
    /// Creates a drop-down-list content control run offering <paramref name="items"/>; the run's
    /// <see cref="Text"/> is the currently displayed item (the first item's display text when none is
    /// given). Serialises as a w:sdt with a w:dropDownList w:sdtPr carrying w:listItem entries.
    /// </summary>
    public static Run DropDownListControl(
        IReadOnlyList<ContentControlListItem> items, string? selectedText = null,
        string? tag = null, string? alias = null) =>
        new(selectedText ?? (items.Count > 0 ? items[0].DisplayText : string.Empty))
        {
            Control = new ContentControl(ContentControlKind.DropDownList, tag, alias, ListItems: items)
        };

    /// <summary>
    /// Creates a combo-box content control run offering <paramref name="items"/> (and allowing free text);
    /// the run's <see cref="Text"/> is the currently displayed value. Serialises as a w:sdt with a
    /// w:comboBox w:sdtPr carrying w:listItem entries.
    /// </summary>
    public static Run ComboBoxControl(
        IReadOnlyList<ContentControlListItem> items, string? selectedText = null,
        string? tag = null, string? alias = null) =>
        new(selectedText ?? (items.Count > 0 ? items[0].DisplayText : string.Empty))
        {
            Control = new ContentControl(ContentControlKind.ComboBox, tag, alias, ListItems: items)
        };
}

/// <summary>
/// The kind of content control (structured document tag, w:sdt) a <see cref="Run"/> belongs to.
/// <see cref="PlainText"/> is a plain-text control (w:sdtPr/w:text); <see cref="CheckBox"/> is a
/// checkbox control (w:sdtPr/w14:checkbox or w:checkbox) whose run carries the checked/unchecked glyph;
/// <see cref="RichText"/> is a rich-text control (w:sdtPr/w:richText) that may hold formatted content;
/// <see cref="DatePicker"/> is a date picker (w:sdtPr/w:date) whose run carries the displayed date;
/// <see cref="DropDownList"/> is a drop-down list (w:sdtPr/w:dropDownList + w:listItem entries) the user
/// can only pick from; <see cref="ComboBox"/> is a combo box (w:sdtPr/w:comboBox + w:listItem entries)
/// that additionally allows free text.
/// </summary>
public enum ContentControlKind
{
    PlainText,
    CheckBox,
    RichText,
    DatePicker,
    DropDownList,
    ComboBox
}

/// <summary>
/// A single choice (w:listItem) of a drop-down list or combo box content control: the visible
/// <see cref="DisplayText"/> (w:displayText) and the stored <see cref="Value"/> (w:value). Modelled as
/// an immutable record so list items can be shared/compared like the other small marks.
/// </summary>
public sealed record ContentControlListItem(string DisplayText, string Value)
{
    /// <summary>Convenience for a list item whose stored value equals its display text.</summary>
    public ContentControlListItem(string displayText) : this(displayText, displayText) { }
}

/// <summary>
/// An immutable content-control (structured document tag / w:sdt) mark carried by a <see cref="Run"/>.
/// Records the control <see cref="Kind"/>, an optional <see cref="Tag"/> (w:tag) and <see cref="Alias"/>
/// (w:alias), and the kind-specific extras: <see cref="Checked"/> (checkbox state), <see cref="DateFormat"/>
/// (a date picker's w:dateFormat string), and <see cref="ListItems"/> (the w:listItem choices of a
/// drop-down list or combo box). Modelled as an immutable record so it mirrors how other small marks
/// (<see cref="PageBorder"/>, <see cref="TableFormatting"/>) are modelled and so consecutive runs can
/// share one instance to coalesce into a single w:sdt on save.
/// </summary>
public sealed record ContentControl(
    ContentControlKind Kind,
    string? Tag = null,
    string? Alias = null,
    bool Checked = false,
    string? DateFormat = null,
    IReadOnlyList<ContentControlListItem>? ListItems = null)
{
    /// <summary>The glyph used in a checkbox run's text when the box is checked (☒, U+2612).</summary>
    public const string CheckedGlyph = "☒";

    /// <summary>The glyph used in a checkbox run's text when the box is unchecked (☐, U+2610).</summary>
    public const string UncheckedGlyph = "☐";

    /// <summary>The default date format (matching Word's date picker default) used when none is set.</summary>
    public const string DefaultDateFormat = "M/d/yyyy";

    /// <summary>The list items of a drop-down/combo control, never null (empty for other kinds).</summary>
    public IReadOnlyList<ContentControlListItem> Items => ListItems ?? System.Array.Empty<ContentControlListItem>();
}

/// <summary>
/// A single footnote: an id (matching a body <see cref="Run.FootnoteId"/>) and its block content,
/// a list of paragraphs. Maps onto a w:footnote element inside word/footnotes.xml.
/// </summary>
public sealed class Footnote(int id)
{
    public int Id { get; } = id;
    public List<Paragraph> Content { get; } = [];

    public Footnote(int id, string text) : this(id) => Content.Add(new Paragraph(text));

    public string PlainText => string.Join("\n", Content.Select(p => p.PlainText));
}

/// <summary>
/// A single endnote: an id (matching a body <see cref="Run.EndnoteId"/>) and its block content,
/// a list of paragraphs. Maps onto a w:endnote element inside word/endnotes.xml. Mirrors
/// <see cref="Footnote"/> but collected at the document end.
/// </summary>
public sealed class Endnote(int id)
{
    public int Id { get; } = id;
    public List<Paragraph> Content { get; } = [];

    public Endnote(int id, string text) : this(id) => Content.Add(new Paragraph(text));

    public string PlainText => string.Join("\n", Content.Select(p => p.PlainText));
}

/// <summary>
/// A single review comment: an id (matching the body runs' <see cref="Run.CommentId"/>), an author
/// and initials, an optional explicit date, and the comment's block content as a list of paragraphs.
/// Maps onto a w:comment element inside word/comments.xml. The date is an explicit model value (never
/// auto-stamped) so the writer stays deterministic — it is only emitted when set.
///
/// Modern (threaded) Word comments are modelled by nesting <see cref="Replies"/> — an ordered list of
/// child comments, each a full <see cref="Comment"/> with its own globally-unique id — under the
/// top-level comment that anchors the body range, and by a <see cref="Resolved"/> flag on the top-level
/// comment. Only the top-level comment is keyed in <see cref="TextDocument.Comments"/> / referenced by
/// body runs; replies live only inside their parent's list. In docx the parent and every reply are flat
/// <c>w:comment</c> entries in comments.xml, with the thread shape (parent/child) and resolved state
/// captured in word/commentsExtended.xml (w15:commentEx, via w15:paraId / w15:paraIdParent / w15:done).
/// </summary>
public sealed class Comment(int id)
{
    public int Id { get; } = id;

    /// <summary>The comment author's display name (w:author). Empty when unknown.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>The author's initials (w:initials). Empty when unknown.</summary>
    public string Initials { get; set; } = string.Empty;

    /// <summary>
    /// The comment's timestamp as a W3CDTF string (w:date), or null when unset. Kept as a string so
    /// the writer never stamps a non-deterministic <c>DateTime.Now</c>; callers set it explicitly.
    /// </summary>
    public string? DateXml { get; set; }

    public List<Paragraph> Content { get; } = [];

    /// <summary>
    /// The ordered thread of replies to this comment (each a full <see cref="Comment"/> with its own
    /// unique id). Only meaningful on a top-level comment; a reply itself carries an empty list. Maps to
    /// child w15:commentEx entries (w15:paraIdParent pointing at this comment's last paragraph).
    /// </summary>
    public List<Comment> Replies { get; } = [];

    /// <summary>
    /// True when the comment thread is marked resolved/done (Word's "Resolve"). Maps to w15:done="1" on
    /// this comment's w15:commentEx entry. Only meaningful on a top-level comment.
    /// </summary>
    public bool Resolved { get; set; }

    public Comment(int id, string text, string author = "", string initials = "") : this(id)
    {
        Author = author;
        Initials = initials;
        Content.Add(new Paragraph(text));
    }

    public string PlainText => string.Join("\n", Content.Select(p => p.PlainText));

    /// <summary>
    /// Adds a reply with the given text/author to this comment's thread and returns it. The reply's id
    /// must be unique across the whole document (use <see cref="TextDocument.NextCommentId"/>).
    /// </summary>
    public Comment AddReply(int id, string text, string author = "", string initials = "")
    {
        var reply = new Comment(id, text, author, initials);
        Replies.Add(reply);
        return reply;
    }

    /// <summary>This comment together with its replies, in thread order (parent first).</summary>
    public IEnumerable<Comment> ThreadInOrder()
    {
        yield return this;
        foreach (var reply in Replies)
            yield return reply;
    }
}

/// <summary>
/// The kind of work a <see cref="Source"/> describes, which selects how its bibliography entry is
/// formatted (a journal article cites its journal/volume/pages, a web site its URL, etc.). The numeric
/// values are stable so a chosen type can be persisted, and <see cref="SourceType.Book"/> is the default
/// (value 0). The names match Word's bibliography source types (<c>b:SourceType</c>).
/// </summary>
public enum SourceType
{
    /// <summary>A book (author, title, publisher, year). The default.</summary>
    Book = 0,

    /// <summary>An article in a periodical (adds journal name, volume, issue and page range).</summary>
    JournalArticle = 1,

    /// <summary>A web page (adds its URL and an accessed date).</summary>
    WebSite = 2,
}

/// <summary>
/// A bibliographic source the document can cite: a short <see cref="Tag"/> (a stable identifier used
/// to reference the source, e.g. <c>"Knuth1997"</c>) plus author/title/year and an optional publisher.
/// A <see cref="SourceType"/> selects type-specific formatting and carries the extra fields that type
/// needs (journal/volume/issue/pages for an article, url/accessed for a web site). Kept deliberately
/// small and immutable-friendly (init-only properties) so it round-trips cleanly and the
/// citation/bibliography formatting helpers (see <see cref="Citations"/>) can stay pure. Missing fields
/// are represented as empty strings / null and handled gracefully by the formatters.
/// </summary>
public sealed class Source
{
    /// <summary>A short, stable identifier for the source (used to reference it). May be empty.</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>The kind of work, selecting type-specific bibliography formatting. Defaults to <see cref="SourceType.Book"/>.</summary>
    public SourceType Type { get; init; } = SourceType.Book;

    /// <summary>The author (or authors) of the work. Empty when unknown.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>The title of the work. Empty when unknown.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The year of publication. Empty when unknown.</summary>
    public string Year { get; init; } = string.Empty;

    /// <summary>The publisher of the work, or null when unknown / not applicable.</summary>
    public string? Publisher { get; init; }

    /// <summary>The periodical name for a <see cref="SourceType.JournalArticle"/>; null otherwise / when unknown.</summary>
    public string? Journal { get; init; }

    /// <summary>The volume number for a <see cref="SourceType.JournalArticle"/>; null when unknown.</summary>
    public string? Volume { get; init; }

    /// <summary>The issue number for a <see cref="SourceType.JournalArticle"/>; null when unknown.</summary>
    public string? Issue { get; init; }

    /// <summary>The page (range) for a <see cref="SourceType.JournalArticle"/>, e.g. <c>"12-20"</c>; null when unknown.</summary>
    public string? Pages { get; init; }

    /// <summary>The URL for a <see cref="SourceType.WebSite"/>; null otherwise / when unknown.</summary>
    public string? Url { get; init; }

    /// <summary>The accessed date for a <see cref="SourceType.WebSite"/>, free-text (e.g. <c>"3 May 2024"</c>); null when unknown.</summary>
    public string? Accessed { get; init; }
}

/// <summary>
/// A marked index entry: a single term the document wants to list in its generated index (see
/// <see cref="DocumentIndex"/>). Kept deliberately small (just the <see cref="Term"/>) and as a model
/// side-store on <see cref="TextDocument.IndexEntries"/> rather than a run-level mark, so marking text
/// for the index never disturbs run storage and needs no docx I/O changes — the generated index is
/// ordinary styled paragraphs that already round-trip.
/// </summary>
public sealed class IndexEntry
{
    /// <summary>The term to list in the index. Trimmed of surrounding whitespace at construction.</summary>
    public string Term { get; }

    public IndexEntry(string term) => Term = (term ?? string.Empty).Trim();
}

/// <summary>
/// The standard categories Word groups a Table of Authorities by (References &gt; Table of Authorities).
/// The numeric values match Word's built-in category numbers (1 = Cases, 2 = Statutes, …) which are
/// what the TA field's <c>\c</c> switch carries, so they round-trip faithfully.
/// </summary>
public enum CitationCategory
{
    Cases = 1,
    Statutes = 2,
    OtherAuthorities = 3,
    Rules = 4,
    Treatises = 5,
    Regulations = 6,
    ConstitutionalProvisions = 7
}

/// <summary>
/// A marked legal citation for a Table of Authorities (Word's References &gt; Mark Citation). It carries
/// the citation's <see cref="Category"/> plus its <see cref="LongCitation"/> (the full form listed in the
/// table) and an optional <see cref="ShortCitation"/> (the abbreviated form). Modelled as a model
/// side-store on <see cref="TextDocument.Citations"/>, mirroring <see cref="IndexEntry"/>: the generated
/// Table of Authorities is ordinary styled paragraphs that already round-trip, and the marks themselves
/// serialise as hidden <c>TA</c> fields (see <c>DocxWriter</c>/<c>DocxReader</c>) so they survive a
/// save/open exactly like Word's.
/// </summary>
public sealed class Citation
{
    /// <summary>The legal-authority category this citation belongs to (Cases, Statutes, …).</summary>
    public CitationCategory Category { get; }

    /// <summary>The full citation text listed in the Table of Authorities. Trimmed at construction.</summary>
    public string LongCitation { get; }

    /// <summary>
    /// The abbreviated/short form Word matches subsequent occurrences against, or empty when none was
    /// given. Trimmed at construction. Not listed in the table; carried for faithful round-trip.
    /// </summary>
    public string ShortCitation { get; }

    public Citation(string longCitation, CitationCategory category = CitationCategory.Cases, string? shortCitation = null)
    {
        LongCitation = (longCitation ?? string.Empty).Trim();
        Category = category;
        ShortCitation = (shortCitation ?? string.Empty).Trim();
    }
}

/// <summary>
/// The kind of simple field a <see cref="Run"/> represents. <see cref="None"/> is an ordinary text
/// run; the others each map to a WordprocessingML simple field (w:fldSimple) whose w:instr is the
/// matching keyword — e.g. <see cref="PageNumber"/> is " PAGE ", <see cref="Date"/> is " DATE ".
/// The run's <see cref="Run.Text"/> doubles as the field's cached/last-computed display value.
/// </summary>
public enum RunFieldKind
{
    None,
    PageNumber,
    Date,
    Time,
    FileName,
    Author,
    NumPages
}

/// <summary>
/// The tracked-change state of a <see cref="Run"/>. <see cref="None"/> is an ordinary run;
/// <see cref="Inserted"/> is a tracked insertion (w:ins); <see cref="Deleted"/> is a tracked deletion
/// (w:del, whose text serialises as w:delText and is kept in the model until the change is accepted).
/// </summary>
public enum RevisionKind
{
    None,
    Inserted,
    Deleted
}

/// <summary>
/// A top-level document block. The document body is an ordered sequence of blocks; today that is
/// paragraphs and tables, mirroring how WordprocessingML interleaves w:p and w:tbl inside w:body.
/// </summary>
public abstract class Block
{
}

/// <summary>A paragraph: an ordered sequence of runs plus paragraph formatting and an optional style.</summary>
public sealed class Paragraph : Block
{
    public List<Run> Runs { get; } = [];
    public ParagraphFormatting Formatting { get; set; } = ParagraphFormatting.Default;
    public string? StyleId { get; set; }

    /// <summary>
    /// Optional bookmark name marking this paragraph as a navigation target. When non-null the
    /// paragraph is bracketed by w:bookmarkStart/w:bookmarkEnd on save, and runs elsewhere can point
    /// to it via <see cref="Run.HyperlinkAnchor"/>. Bookmarks are invisible markers (no glyphs).
    /// </summary>
    public string? BookmarkName { get; set; }

    /// <summary>
    /// Optional section break carried by this paragraph. When non-null this paragraph is the <em>last</em>
    /// paragraph of a section, and the marker holds that section's <see cref="Section.Page"/> setup and
    /// <see cref="Section.BreakKind"/>. On save the section's w:sectPr is emitted inside this paragraph's
    /// w:pPr (with w:type), exactly as WordprocessingML stores a non-final section's properties. Null (the
    /// default) means the paragraph does not end a section, so single-section documents are unaffected.
    /// </summary>
    public Section? SectionBreak { get; set; }

    /// <summary>
    /// The original <c>w:numPr</c> (numId + ilvl) this paragraph carried on read when FreeW did <em>not</em>
    /// model it as one of its own lists (see <see cref="ParagraphFormatting.ListKind"/>). Null (the default)
    /// when the paragraph carries no numbering, or when FreeW maps its numbering to a <see cref="ListKind"/>
    /// (in which case FreeW's own model is authoritative and re-emits FreeW's numbering instead).
    /// <para>
    /// Captured alongside <see cref="PreservedParts.OriginalNumbering"/> so a document whose numbering FreeW
    /// cannot fully represent (rich multilevel/legal/custom-format definitions) keeps both its original
    /// <c>word/numbering.xml</c> and the paragraphs' <c>w:numPr</c> across a round-trip. The writer emits this
    /// paragraph's <c>numPr</c> from the (possibly remapped) preserved id, never from FreeW's fixed list ids.
    /// </para>
    /// </summary>
    public PreservedNumbering? PreservedNumbering { get; set; }

    public Paragraph() { }

    public Paragraph(string text)
    {
        if (text.Length > 0)
            Runs.Add(new Run(text));
    }

    public string PlainText => string.Concat(Runs.Select(r => r.Text));
}

/// <summary>A single table cell: a list of paragraphs (matching w:tc, which holds block content).</summary>
public sealed class TableCell
{
    public List<Paragraph> Paragraphs { get; } = [];

    /// <summary>
    /// Cell background shading as an RRGGBB hex (e.g. <c>"#FFFF00"</c>). Null means no shading.
    /// Round-trips to docx as cell shading (<c>tc/tcPr/w:shd w:fill</c>), mirroring
    /// <see cref="ParagraphFormatting.ShadingColorHex"/> and <see cref="RunFormatting.HighlightColorHex"/>.
    /// </summary>
    public string? ShadingColorHex { get; set; }

    /// <summary>
    /// Preferred cell width in points (<c>tc/tcPr/w:tcW</c>), or null for automatic width. Optional so
    /// existing cells are unaffected.
    /// </summary>
    public double? WidthPt { get; set; }

    /// <summary>
    /// Horizontal merge: how many grid columns this cell spans (<c>tc/tcPr/w:gridSpan w:val</c>). The
    /// default of <c>1</c> means no horizontal merge, so existing tables are unaffected. When merging
    /// cells horizontally the surviving (left-most) cell's <see cref="GridSpan"/> is increased and the
    /// absorbed cells are dropped from the row.
    /// </summary>
    public int GridSpan { get; set; } = 1;

    /// <summary>
    /// Vertical merge state (<c>tc/tcPr/w:vMerge</c>). <see cref="VerticalMergeState.None"/> (the default)
    /// means the cell is not part of a vertical merge, so existing tables are unaffected.
    /// <see cref="VerticalMergeState.Restart"/> is the top cell of a merged run (<c>w:vMerge w:val="restart"</c>)
    /// and <see cref="VerticalMergeState.Continue"/> is a cell below it that is absorbed into the restart
    /// cell (<c>w:vMerge</c> with no value / <c>w:val="continue"</c>).
    /// </summary>
    public VerticalMergeState VerticalMerge { get; set; } = VerticalMergeState.None;

    public TableCell() { }

    public TableCell(string text) => Paragraphs.Add(new Paragraph(text));

    public string PlainText => string.Join("\n", Paragraphs.Select(p => p.PlainText));
}

/// <summary>
/// Vertical-merge state of a table cell (<c>tc/tcPr/w:vMerge</c>). <see cref="None"/> means the cell
/// stands alone; <see cref="Restart"/> begins a vertically merged run (the cell whose content survives);
/// <see cref="Continue"/> is a cell below the restart that is visually absorbed into it.
/// </summary>
public enum VerticalMergeState
{
    None,
    Restart,
    Continue
}

/// <summary>A table row: an ordered sequence of cells (w:tr).</summary>
public sealed class TableRow
{
    public List<TableCell> Cells { get; } = [];
}

/// <summary>
/// Minimal table-level formatting: whether cell borders are drawn plus the three table-style toggles.
/// <see cref="HeaderRow"/> styles the first row as a header (bold + shaded fill); <see cref="BandedRows"/>
/// shades alternate body rows; <see cref="RepeatHeaderRow"/> repeats the header row across page breaks.
/// All three default to false so existing tables round-trip unchanged.
/// </summary>
public sealed record TableFormatting
{
    public bool Borders { get; init; } = true;

    /// <summary>
    /// When true, the first row is styled as a header (its cells render bold over a light shaded fill).
    /// Round-trips via <c>w:tblPr/w:tblLook w:firstRow="1"</c>. Default false.
    /// </summary>
    public bool HeaderRow { get; init; }

    /// <summary>
    /// When true, alternate body rows are shaded with a light fill (banded rows). Round-trips via
    /// <c>w:tblPr/w:tblLook w:noHBand="0"</c> (vs <c>"1"</c> when off). Default false.
    /// </summary>
    public bool BandedRows { get; init; }

    /// <summary>
    /// When true, the header (first) row repeats at the top of each page the table spans. Round-trips
    /// via <c>w:trPr/w:tblHeader</c> on the first row. Default false.
    /// </summary>
    public bool RepeatHeaderRow { get; init; }

    public static readonly TableFormatting Default = new();
}

/// <summary>A table block: rows of cells, each cell holding paragraphs (w:tbl / w:tr / w:tc).</summary>
public sealed class Table : Block
{
    public List<TableRow> Rows { get; } = [];
    public TableFormatting Formatting { get; set; } = TableFormatting.Default;

    /// <summary>
    /// Per-column widths in points, one entry per column, matching the docx table grid
    /// (<c>w:tbl/w:tblGrid/w:gridCol</c>). Empty when no explicit grid is known (the default), so
    /// existing tables are unaffected.
    /// </summary>
    public List<double> ColumnWidthsPt { get; } = [];

    public Table() { }

    /// <summary>Create a uniform <paramref name="rows"/>x<paramref name="columns"/> table of empty cells.</summary>
    public static Table Create(int rows, int columns)
    {
        var table = new Table();
        for (var r = 0; r < rows; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < columns; c++)
                row.Cells.Add(new TableCell(string.Empty));
            table.Rows.Add(row);
        }
        return table;
    }

    public int RowCount => Rows.Count;

    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Cells.Count);
}

/// <summary>
/// Document-level metadata, mapping onto the OPC core properties part (docProps/core.xml). All
/// fields are optional; timestamps are explicit (never auto-stamped at construction) so the model
/// and writer stay deterministic. The writer emits only the values that are set.
/// </summary>
public sealed class DocumentProperties
{
    /// <summary>dc:title</summary>
    public string? Title { get; set; }

    /// <summary>dc:creator (the document's author).</summary>
    public string? Author { get; set; }

    /// <summary>dc:subject</summary>
    public string? Subject { get; set; }

    /// <summary>cp:keywords</summary>
    public string? Keywords { get; set; }

    /// <summary>dc:description (free-form comments).</summary>
    public string? Comments { get; set; }

    /// <summary>cp:lastModifiedBy</summary>
    public string? LastModifiedBy { get; set; }

    /// <summary>dcterms:created (W3CDTF).</summary>
    public DateTimeOffset? Created { get; set; }

    /// <summary>dcterms:modified (W3CDTF).</summary>
    public DateTimeOffset? Modified { get; set; }
}

/// <summary>
/// How the document restricts editing (document protection, w:settings/w:documentProtection).
/// <see cref="None"/> is an unprotected document (the default — no settings part is emitted);
/// <see cref="ReadOnly"/> locks the whole document against edits; <see cref="CommentsOnly"/> permits
/// only the insertion of comments; <see cref="TrackChangesOnly"/> permits edits but forces them to be
/// tracked revisions. Maps onto w:documentProtection/@w:edit ("readOnly"/"comments"/"trackedChanges").
/// </summary>
public enum ProtectionMode
{
    None,
    ReadOnly,
    CommentsOnly,
    TrackChangesOnly
}

/// <summary>
/// Document protection (restrict-editing) settings, mapping onto word/settings.xml's
/// w:documentProtection. Immutable so it round-trips cleanly and can be shared; the default
/// (<see cref="ProtectionMode.None"/>, see <see cref="Unprotected"/>) leaves existing documents
/// unaffected — no settings part is emitted and the reader maps a missing/absent protection to None.
/// When <see cref="Mode"/> is not None the writer emits w:documentProtection with w:enforcement="1".
/// </summary>
public sealed record ProtectionSettings(ProtectionMode Mode = ProtectionMode.None)
{
    /// <summary>The default, unprotected settings (<see cref="ProtectionMode.None"/>).</summary>
    public static readonly ProtectionSettings Unprotected = new(ProtectionMode.None);

    /// <summary>True when the document is protected in some mode (i.e. not <see cref="ProtectionMode.None"/>).</summary>
    public bool IsProtected => Mode != ProtectionMode.None;
}

/// <summary>
/// A page header or footer: an ordered list of paragraphs shown in the top (header) or bottom
/// (footer) margin of every page. Maps onto a WordprocessingML header/footer part (w:hdr / w:ftr).
/// A footer paragraph may contain a page-number field run (see <see cref="Run.PageNumberField"/>).
/// </summary>
public sealed class HeaderFooter
{
    public List<Paragraph> Paragraphs { get; } = [];

    public HeaderFooter() { }

    public HeaderFooter(string text) => Paragraphs.Add(new Paragraph(text));

    /// <summary>True when there is no visible content (no paragraphs, or only empty ones).</summary>
    public bool IsEmpty => Paragraphs.Count == 0 || Paragraphs.All(p => p.Runs.Count == 0);

    public string PlainText => string.Join("\n", Paragraphs.Select(p => p.PlainText));
}

/// <summary>
/// An immutable page border (w:sectPr/w:pgBorders). A uniform box drawn around the page with one
/// colour and width (points). Null on <see cref="PageSettings.PageBorder"/> means no page border, so
/// existing documents are unaffected. Mirrors how <see cref="ParagraphBorder"/> is modelled.
/// </summary>
public sealed record PageBorder(string ColorHex = "#000000", double WidthPt = 1.0)
{
    /// <summary>
    /// The line style of every page-border edge (w:val). Defaults to <see cref="BorderLineStyle.Single"/>,
    /// matching what the writer previously emitted, so existing documents round-trip byte-unchanged.
    /// </summary>
    public BorderLineStyle LineStyle { get; init; } = BorderLineStyle.Single;
}

/// <summary>
/// How (and whether) lines are numbered in the page margin (w:sectPr/w:lnNumType).
/// <see cref="None"/> emits no w:lnNumType (the default — existing documents are unaffected);
/// <see cref="Continuous"/> numbers lines continuously across pages (w:restart="continuous");
/// <see cref="RestartEachPage"/> restarts numbering at 1 on every page (w:restart="newPage").
/// </summary>
public enum LineNumberMode
{
    None,
    Continuous,
    RestartEachPage
}

/// <summary>
/// How page content is aligned vertically within the text area (w:sectPr/w:vAlign).
/// <see cref="Top"/> is the default ("top", or no w:vAlign emitted — existing documents are unaffected);
/// <see cref="Center"/> centres the content ("center"); <see cref="Justified"/> spreads it to fill the page
/// ("both"); <see cref="Bottom"/> aligns to the bottom ("bottom").
/// </summary>
public enum PageVerticalAlignment
{
    Top,
    Center,
    Justified,
    Bottom
}

/// <summary>Page geometry for a section (points; US Letter with 1in margins by default).</summary>
public sealed class PageSettings
{
    public double WidthPt { get; set; } = 612;
    public double HeightPt { get; set; } = 792;
    public double MarginLeftPt { get; set; } = 72;
    public double MarginRightPt { get; set; } = 72;
    public double MarginTopPt { get; set; } = 72;
    public double MarginBottomPt { get; set; } = 72;
    public bool Landscape { get; set; }

    /// <summary>
    /// The number of equal-width text columns the page content flows into (w:sectPr/w:cols w:num).
    /// Defaults to 1 (single column) so existing documents are unaffected. Always at least 1.
    /// </summary>
    public int ColumnCount { get; set; } = 1;

    /// <summary>
    /// The gap between adjacent columns in points (w:sectPr/w:cols w:space). Defaults to 36 points
    /// (half an inch), Word's default column spacing. Only meaningful when <see cref="ColumnCount"/> &gt; 1.
    /// Ignored when <see cref="ColumnWidthsPt"/> carries explicit unequal columns (each column then
    /// supplies its own trailing space).
    /// </summary>
    public double ColumnSpacingPt { get; set; } = 36;

    /// <summary>
    /// Whether a vertical line is drawn between adjacent columns (w:sectPr/w:cols w:sep). Defaults to
    /// false so existing documents round-trip unchanged — no w:sep is emitted. Only meaningful when
    /// <see cref="ColumnCount"/> &gt; 1; the print preview draws the divider lines when set.
    /// </summary>
    public bool ColumnsLineBetween { get; set; }

    /// <summary>
    /// Optional explicit per-column widths in points for an <em>unequal</em> column layout (Word's
    /// "Left" / "Right" presets and custom widths). Null — the default — means equal-width columns
    /// derived from <see cref="ColumnCount"/> and <see cref="ColumnSpacingPt"/>, so existing documents
    /// are unaffected. When non-null it holds exactly <see cref="ColumnCount"/> widths and the writer
    /// emits w:cols/@w:equalWidth="0" with one w:col (w:w + trailing w:space) per column. The trailing
    /// space of all but the last column is <see cref="ColumnSpacingPt"/>.
    /// </summary>
    public IReadOnlyList<double>? ColumnWidthsPt { get; set; }

    /// <summary>
    /// Optional page border drawn around the whole page (w:sectPr/w:pgBorders), or null for none.
    /// Nullable/default so existing documents round-trip unchanged. Mirrors
    /// <see cref="ParagraphFormatting.Border"/>; round-trips to docx as the four w:pgBorders edges.
    /// </summary>
    public PageBorder? PageBorder { get; set; }

    /// <summary>
    /// Optional diagonal text watermark shown faintly behind the page content, or null for none.
    /// Persisted best-effort as a custom document property (docProps/custom.xml) so it round-trips,
    /// and rendered as an editor/preview visual. Nullable so existing documents are unaffected.
    /// </summary>
    public string? Watermark { get; set; }

    /// <summary>
    /// Line-numbering mode shown in the left page margin (w:sectPr/w:lnNumType). Defaults to
    /// <see cref="LineNumberMode.None"/> so existing documents round-trip unchanged — no w:lnNumType
    /// is emitted. When not None the writer emits w:lnNumType with the matching w:restart, and the
    /// print preview draws line numbers in the margin.
    /// </summary>
    public LineNumberMode LineNumberMode { get; set; } = LineNumberMode.None;

    /// <summary>
    /// The interval at which line numbers are shown (w:lnNumType/@w:countBy): every Nth line is
    /// numbered. Defaults to 1 (every line). Only meaningful when <see cref="LineNumberMode"/> is not
    /// <see cref="LineNumberMode.None"/>. Always at least 1.
    /// </summary>
    public int LineNumberCountBy { get; set; } = 1;

    /// <summary>
    /// Whether automatic hyphenation is enabled for the document (word/settings.xml's
    /// w:autoHyphenation toggle). Defaults to false so existing documents are unaffected — no
    /// w:autoHyphenation is emitted (and the settings part is only emitted when something needs it).
    /// When true the writer emits w:autoHyphenation and the reader maps it back here.
    /// </summary>
    public bool AutoHyphenation { get; set; }

    /// <summary>
    /// The hyphenation zone in points (word/settings.xml's w:hyphenationZone, stored in twips). This is the
    /// maximum amount of whitespace allowed at the end of a line before automatic hyphenation kicks in: a
    /// word is only broken when the gap left at the line end would otherwise exceed this zone. A wider zone
    /// means fewer hyphens (and a more ragged right edge); a narrower zone means more. Defaults to 0, which —
    /// like Word — is treated as the default zone (0.25" / 360 twips) and is not emitted unless changed.
    /// Only meaningful when <see cref="AutoHyphenation"/> is on.
    /// </summary>
    public double HyphenationZonePt { get; set; }

    /// <summary>
    /// The maximum number of consecutive lines that may end with a hyphen (word/settings.xml's
    /// w:consecutiveHyphenLimit). 0 (the default) means no limit — Word's "Limit consecutive hyphens to: No
    /// limit". Emitted only when greater than 0. Only meaningful when <see cref="AutoHyphenation"/> is on.
    /// </summary>
    public int ConsecutiveHyphenLimit { get; set; }

    /// <summary>
    /// When true, words in ALL CAPITALS are not automatically hyphenated (word/settings.xml's
    /// w:doNotHyphenateCaps — Word's "Hyphenate words in CAPS" checkbox, inverted: checked = hyphenate caps =
    /// this false). Defaults to false (caps are hyphenated) so existing documents are unaffected; emitted only
    /// when true. Only meaningful when <see cref="AutoHyphenation"/> is on.
    /// </summary>
    public bool DoNotHyphenateCaps { get; set; }

    /// <summary>
    /// How page content is aligned vertically within the text area (w:sectPr/w:vAlign). Defaults to
    /// <see cref="PageVerticalAlignment.Top"/> so existing documents round-trip unchanged — no
    /// w:vAlign is emitted. When not Top the writer emits w:vAlign with the matching value
    /// (Justified→"both") and the reader maps it back here. Note: this is a docx round-trip + Word
    /// honoured setting; FreeW's fixed-page print preview does not currently re-flow content to reflect
    /// the alignment (a known view limitation — Word applies it on open).
    /// </summary>
    public PageVerticalAlignment VerticalAlignment { get; set; } = PageVerticalAlignment.Top;

    /// <summary>
    /// Whether the section uses a distinct first-page header/footer (w:sectPr/w:titlePg toggle).
    /// Defaults to false so existing documents are unaffected — no w:titlePg is emitted. When true the
    /// writer emits w:titlePg so Word honours "different first page"; FreeW stores a single
    /// header/footer (a genuinely separate first-page header part is out of scope).
    /// </summary>
    public bool DifferentFirstPage { get; set; }

    /// <summary>
    /// Whether the document uses distinct headers/footers on odd and even pages (the document-level
    /// w:settings/w:evenAndOddHeaders toggle). Defaults to false so existing documents are unaffected —
    /// no w:evenAndOddHeaders is emitted and no settings part is forced. When true the writer emits the
    /// toggle in word/settings.xml, emits the even header/footer parts (header2.xml / footer2.xml) and
    /// adds w:headerReference/w:footerReference w:type="even" to the section; the even content lives in
    /// <see cref="TextDocument.EvenHeader"/> / <see cref="TextDocument.EvenFooter"/>. Unlike the other
    /// page properties this is a document-wide setting, not a per-section one — it is read/written on the
    /// body-level (final-section) page settings.
    /// </summary>
    public bool DifferentOddEvenPages { get; set; }

    /// <summary>
    /// Optional page background colour as an RRGGBB hex (e.g. <c>"#FFFFCC"</c>), or null for none (the
    /// default — existing documents are unaffected). When set the writer emits w:background w:color as the
    /// first child of w:document (before w:body) and w:displayBackgroundShape in word/settings.xml so Word
    /// actually paints it. Like <see cref="DifferentOddEvenPages"/> this is a document-wide setting carried
    /// on the body-level page settings. The '#' prefix is optional and stripped on write.
    /// </summary>
    public string? BackgroundColorHex { get; set; }

    /// <summary>
    /// Returns a deep copy of these page settings. Used when a document is split into multiple
    /// sections (see <see cref="Section"/>) so each section owns an independent <see cref="PageSettings"/>
    /// that can be edited without disturbing the others. <see cref="PageBorder"/> is an immutable record,
    /// so copying the reference is safe.
    /// </summary>
    public PageSettings Clone() => new()
    {
        WidthPt = WidthPt,
        HeightPt = HeightPt,
        MarginLeftPt = MarginLeftPt,
        MarginRightPt = MarginRightPt,
        MarginTopPt = MarginTopPt,
        MarginBottomPt = MarginBottomPt,
        Landscape = Landscape,
        ColumnCount = ColumnCount,
        ColumnSpacingPt = ColumnSpacingPt,
        ColumnsLineBetween = ColumnsLineBetween,
        ColumnWidthsPt = ColumnWidthsPt is null ? null : new List<double>(ColumnWidthsPt),
        PageBorder = PageBorder,
        Watermark = Watermark,
        LineNumberMode = LineNumberMode,
        LineNumberCountBy = LineNumberCountBy,
        AutoHyphenation = AutoHyphenation,
        HyphenationZonePt = HyphenationZonePt,
        ConsecutiveHyphenLimit = ConsecutiveHyphenLimit,
        DoNotHyphenateCaps = DoNotHyphenateCaps,
        VerticalAlignment = VerticalAlignment,
        DifferentFirstPage = DifferentFirstPage,
        DifferentOddEvenPages = DifferentOddEvenPages,
        BackgroundColorHex = BackgroundColorHex
    };
}

/// <summary>
/// The kind of section break that begins a WordprocessingML section (w:sectPr/w:type w:val).
/// <see cref="NextPage"/> (Word's default for an inserted section break) starts the new section on the
/// next page; <see cref="Continuous"/> starts it on the same page (no page break); <see cref="EvenPage"/>
/// / <see cref="OddPage"/> start it on the next even/odd page. The final (body-level) section carries a
/// break kind too, but Word ignores it there — it only matters for non-final sections.
/// </summary>
public enum SectionBreakKind
{
    Continuous,
    NextPage,
    EvenPage,
    OddPage
}

/// <summary>
/// The per-section set of page headers and footers (parity gap W4/Z3 extension). Each WordprocessingML
/// section can reference its own header/footer parts via the w:headerReference/w:footerReference elements
/// in its w:sectPr, keyed by w:type: "default" (every page, or odd pages when different-odd-even is on),
/// "even" (even pages) and "first" (the first page when w:titlePg is set). Modelling them per-section (on
/// <see cref="Section.HeadersFooters"/>) rather than only document-wide means multi-section documents and
/// page-specific (first-page) headers/footers round-trip instead of collapsing onto one document-level
/// header/footer. All six slots are optional; null means the section does not reference that header/footer
/// type. The document-level <see cref="TextDocument.Header"/> etc. are a view onto the final section's
/// instance, so existing single-section callers are unaffected.
/// </summary>
public sealed class SectionHeadersFooters
{
    /// <summary>The default header (w:headerReference w:type="default"), or null when none.</summary>
    public HeaderFooter? Header { get; set; }

    /// <summary>The default footer (w:footerReference w:type="default"), or null when none.</summary>
    public HeaderFooter? Footer { get; set; }

    /// <summary>The even-page header (w:headerReference w:type="even"), or null when none.</summary>
    public HeaderFooter? EvenHeader { get; set; }

    /// <summary>The even-page footer (w:footerReference w:type="even"), or null when none.</summary>
    public HeaderFooter? EvenFooter { get; set; }

    /// <summary>The first-page header (w:headerReference w:type="first"), or null when none.</summary>
    public HeaderFooter? FirstHeader { get; set; }

    /// <summary>The first-page footer (w:footerReference w:type="first"), or null when none.</summary>
    public HeaderFooter? FirstFooter { get; set; }

    /// <summary>True when no header/footer slot carries visible content.</summary>
    public bool IsEmpty =>
        (Header is null || Header.IsEmpty)
        && (Footer is null || Footer.IsEmpty)
        && (EvenHeader is null || EvenHeader.IsEmpty)
        && (EvenFooter is null || EvenFooter.IsEmpty)
        && (FirstHeader is null || FirstHeader.IsEmpty)
        && (FirstFooter is null || FirstFooter.IsEmpty);
}

/// <summary>
/// One section of a multi-section document: its own <see cref="PageSettings"/> (page size, margins,
/// orientation, columns, borders, line numbers, …) plus the <see cref="BreakKind"/> describing how the
/// section begins (continuous / next-page / even-page / odd-page) and its own per-section
/// <see cref="HeadersFooters"/> (default/even/first header &amp; footer).
///
/// Sections are modelled as a <em>marker on the paragraph that ends them</em>: setting
/// <see cref="Paragraph.SectionBreak"/> on a paragraph makes that paragraph the last paragraph of a
/// section, carrying the section's page setup — exactly mirroring WordprocessingML, where a non-final
/// section's w:sectPr lives in the w:pPr of its last paragraph. The document-wide
/// <see cref="TextDocument.Page"/> remains the <em>final</em> section's settings (the body-level
/// w:sectPr), so a document with no <see cref="Paragraph.SectionBreak"/> markers behaves exactly as a
/// single-section document did before. <see cref="TextDocument.Sections"/> exposes the ordered section
/// view reconstructed from these markers plus the final <see cref="TextDocument.Page"/>.
/// </summary>
public sealed class Section(PageSettings page, SectionBreakKind breakKind = SectionBreakKind.NextPage)
{
    /// <summary>This section's page geometry / layout. Each section owns an independent instance.</summary>
    public PageSettings Page { get; set; } = page;

    /// <summary>How this section begins relative to the previous one (w:sectPr/w:type).</summary>
    public SectionBreakKind BreakKind { get; set; } = breakKind;

    /// <summary>
    /// This section's own header/footer set (default/even/first). Each section owns an independent instance
    /// so multi-section documents keep page-specific headers/footers distinct per section.
    /// </summary>
    public SectionHeadersFooters HeadersFooters { get; set; } = new();
}

/// <summary>
/// The FreeW text document: ordered paragraphs, a style catalog, document-level defaults, and
/// page settings. Still intentionally lean, but now rich enough to carry real formatting and to
/// map onto WordprocessingML (document.xml / styles.xml) in a later milestone.
/// </summary>
public sealed class TextDocument
{
    /// <summary>The document body: an ordered sequence of blocks (paragraphs and tables).</summary>
    public List<Block> Blocks { get; } = [];
    public Dictionary<string, DocumentStyle> Styles { get; } = [];
    public RunFormatting DefaultRun { get; set; } = new() { FontFamily = "Calibri", FontSizePt = 11 };
    public ParagraphFormatting DefaultParagraph { get; set; } = ParagraphFormatting.Default;

    /// <summary>
    /// The page settings of the <em>final</em> (or only) section — the body-level w:sectPr. A document
    /// with no <see cref="Paragraph.SectionBreak"/> markers is single-section and these are its only page
    /// settings, so existing single-section behaviour is unchanged. Earlier sections carry their own
    /// <see cref="PageSettings"/> on their ending paragraph's <see cref="Paragraph.SectionBreak"/>.
    /// </summary>
    public PageSettings Page { get; } = new();

    /// <summary>
    /// The document's sections in order. Reconstructed from the <see cref="Paragraph.SectionBreak"/>
    /// markers (one section per top-level paragraph that ends a section) followed by the final section,
    /// whose settings are <see cref="Page"/>. A document with no markers yields a single section whose
    /// page settings are <see cref="Page"/>, matching the single-section model exactly.
    /// </summary>
    public IReadOnlyList<Section> Sections
    {
        get
        {
            var sections = new List<Section>();
            foreach (var block in Blocks)
                if (block is Paragraph { SectionBreak: { } sectionBreak })
                    sections.Add(sectionBreak);
            // The trailing section is always the body-level page settings (the final w:sectPr). Its break
            // kind is not meaningful (Word ignores w:type on the last section), so report it as NextPage.
            // Its header/footer set is the stable document-level instance, so the document-level Header /
            // Footer / … views (below) and this final section share one instance.
            sections.Add(new Section(Page, SectionBreakKind.NextPage)
            {
                HeadersFooters = FinalSectionHeadersFooters
            });
            return sections;
        }
    }

    /// <summary>
    /// The final (or only) section's header/footer set — the body-level w:sectPr's header/footer
    /// references. The document-level <see cref="Header"/> / <see cref="Footer"/> / <see cref="EvenHeader"/>
    /// / <see cref="EvenFooter"/> / <see cref="FirstHeader"/> / <see cref="FirstFooter"/> are a view onto
    /// this instance, so a single-section document's headers/footers live here and existing callers are
    /// unaffected. Non-final sections carry their own instance on their <see cref="Section.HeadersFooters"/>.
    /// </summary>
    public SectionHeadersFooters FinalSectionHeadersFooters { get; } = new();

    /// <summary>
    /// The default page header (top margin), or null when the document has no header. A view onto the
    /// final section's <see cref="FinalSectionHeadersFooters"/>. Maps to a word/headerN.xml part referenced
    /// from the body-level w:sectPr via w:headerReference w:type="default".
    /// </summary>
    public HeaderFooter? Header
    {
        get => FinalSectionHeadersFooters.Header;
        set => FinalSectionHeadersFooters.Header = value;
    }

    /// <summary>
    /// The default page footer (bottom margin), or null when the document has no footer. A view onto the
    /// final section's <see cref="FinalSectionHeadersFooters"/>. Maps to a word/footerN.xml part referenced
    /// from the body-level w:sectPr via w:footerReference w:type="default".
    /// </summary>
    public HeaderFooter? Footer
    {
        get => FinalSectionHeadersFooters.Footer;
        set => FinalSectionHeadersFooters.Footer = value;
    }

    /// <summary>
    /// The even-page header, or null when the document has none. A view onto the final section's
    /// <see cref="FinalSectionHeadersFooters"/>. Only meaningful when
    /// <see cref="PageSettings.DifferentOddEvenPages"/> is set (the default <see cref="Header"/> then
    /// applies to odd pages). Maps to a word/headerN.xml part referenced from w:sectPr via
    /// w:headerReference w:type="even". Mirrors <see cref="Header"/>.
    /// </summary>
    public HeaderFooter? EvenHeader
    {
        get => FinalSectionHeadersFooters.EvenHeader;
        set => FinalSectionHeadersFooters.EvenHeader = value;
    }

    /// <summary>
    /// The even-page footer, or null when the document has none. A view onto the final section's
    /// <see cref="FinalSectionHeadersFooters"/>. Only meaningful when
    /// <see cref="PageSettings.DifferentOddEvenPages"/> is set (the default <see cref="Footer"/> then
    /// applies to odd pages). Maps to a word/footerN.xml part referenced from w:sectPr via
    /// w:footerReference w:type="even". Mirrors <see cref="Footer"/>.
    /// </summary>
    public HeaderFooter? EvenFooter
    {
        get => FinalSectionHeadersFooters.EvenFooter;
        set => FinalSectionHeadersFooters.EvenFooter = value;
    }

    /// <summary>
    /// The first-page header, or null when the document has none. A view onto the final section's
    /// <see cref="FinalSectionHeadersFooters"/>. Only meaningful when
    /// <see cref="PageSettings.DifferentFirstPage"/> is set (the default <see cref="Header"/> then applies
    /// to the remaining pages). Maps to a word/headerN.xml part referenced from w:sectPr via
    /// w:headerReference w:type="first". Mirrors <see cref="Header"/>.
    /// </summary>
    public HeaderFooter? FirstHeader
    {
        get => FinalSectionHeadersFooters.FirstHeader;
        set => FinalSectionHeadersFooters.FirstHeader = value;
    }

    /// <summary>
    /// The first-page footer, or null when the document has none. A view onto the final section's
    /// <see cref="FinalSectionHeadersFooters"/>. Only meaningful when
    /// <see cref="PageSettings.DifferentFirstPage"/> is set (the default <see cref="Footer"/> then applies
    /// to the remaining pages). Maps to a word/footerN.xml part referenced from w:sectPr via
    /// w:footerReference w:type="first". Mirrors <see cref="Footer"/>.
    /// </summary>
    public HeaderFooter? FirstFooter
    {
        get => FinalSectionHeadersFooters.FirstFooter;
        set => FinalSectionHeadersFooters.FirstFooter = value;
    }

    /// <summary>Document-level metadata (maps to docProps/core.xml).</summary>
    public DocumentProperties Properties { get; } = new();

    /// <summary>
    /// Document protection (restrict-editing) settings. Defaults to
    /// <see cref="ProtectionSettings.Unprotected"/> (<see cref="ProtectionMode.None"/>) so existing
    /// documents are unaffected and no word/settings.xml part is emitted. When set to a protected mode
    /// the writer emits w:settings/w:documentProtection and the reader maps it back here.
    /// </summary>
    public ProtectionSettings Protection { get; set; } = ProtectionSettings.Unprotected;

    /// <summary>
    /// The document's persisted theme — the colour/font scheme that maps to <c>word/theme/theme1.xml</c>.
    /// Defaults to <see cref="DocumentTheme.Default"/> ("Office"), so existing documents are unchanged.
    /// The writer always emits a theme part (mirroring real Word documents, which always carry one); the
    /// reader infers the closest preset from the theme's accent colours and major/minor fonts, falling
    /// back to "Office" when no preset matches. Applying a theme to the document's styles is separate
    /// (<see cref="DocumentTheme.Apply"/>); this property records which theme is in effect.
    /// </summary>
    public DocumentTheme Theme { get; set; } = DocumentTheme.Default;

    /// <summary>
    /// The document's footnotes, keyed by footnote id (matching <see cref="Run.FootnoteId"/> on the
    /// body reference runs). Maps to word/footnotes.xml (w:footnotes / w:footnote w:id="N"). Empty
    /// when the document has no footnotes, in which case no footnotes part is emitted.
    /// </summary>
    public Dictionary<int, Footnote> Footnotes { get; } = [];

    /// <summary>The next unused footnote id (1-based; ignores the reserved separator ids -1 and 0).</summary>
    public int NextFootnoteId() => Footnotes.Count == 0 ? 1 : Math.Max(0, Footnotes.Keys.Max()) + 1;

    /// <summary>
    /// The document's endnotes, keyed by endnote id (matching <see cref="Run.EndnoteId"/> on the
    /// body reference runs). Maps to word/endnotes.xml (w:endnotes / w:endnote w:id="N"). Empty
    /// when the document has no endnotes, in which case no endnotes part is emitted.
    /// </summary>
    public Dictionary<int, Endnote> Endnotes { get; } = [];

    /// <summary>The next unused endnote id (1-based; ignores the reserved separator ids -1 and 0).</summary>
    public int NextEndnoteId() => Endnotes.Count == 0 ? 1 : Math.Max(0, Endnotes.Keys.Max()) + 1;

    /// <summary>
    /// The document's review comments, keyed by comment id (matching the body runs' <see cref="Run.CommentId"/>).
    /// Maps to word/comments.xml (w:comments / w:comment w:id="N"). Empty when the document has no
    /// comments, in which case no comments part is emitted.
    /// </summary>
    public Dictionary<int, Comment> Comments { get; } = [];

    /// <summary>
    /// The next unused comment id (0-based, as Word numbers comments from 0). Scans top-level comments
    /// AND their replies, since every reply is also a flat w:comment with a globally-unique id.
    /// </summary>
    public int NextCommentId() =>
        Comments.Count == 0
            ? 0
            : Comments.Values.SelectMany(c => c.ThreadInOrder()).Max(c => c.Id) + 1;

    /// <summary>
    /// The document's bibliographic sources, in insertion order. Citations reference a source's
    /// <see cref="Source.Tag"/>; <see cref="Citations.BuildBibliography(TextDocument)"/> renders them as
    /// ordinary styled paragraphs. These are pure model data (no docx part of their own) — inserted
    /// in-text citations and the bibliography are ordinary text/paragraphs that already round-trip.
    /// </summary>
    public List<Source> Sources { get; } = [];

    /// <summary>
    /// The selected bibliographic <see cref="CitationStyle"/> (APA / MLA / Chicago / IEEE) governing how
    /// in-text citations and the bibliography are formatted. Chosen from the References &gt; Citation Style
    /// combo; persisted to / restored from the docx bibliography part (<c>b:Sources/@SelectedStyle</c>) so it
    /// survives a save/load. Defaults to <see cref="CitationStyle.Apa"/>.
    /// </summary>
    public CitationStyle BibliographyStyle { get; set; } = CitationStyle.Apa;

    /// <summary>
    /// The terms marked for the document index, in mark order. <see cref="DocumentIndex.Build(TextDocument)"/>
    /// renders the distinct, alphabetically sorted terms as ordinary styled paragraphs. Like
    /// <see cref="Sources"/> these are pure model data (no docx part of their own) — the generated index is
    /// ordinary styled paragraphs that already round-trip. Empty when nothing has been marked.
    /// </summary>
    public List<IndexEntry> IndexEntries { get; } = [];

    /// <summary>
    /// The legal citations marked for a Table of Authorities, in mark order.
    /// <see cref="TableOfAuthorities.Build(TextDocument)"/> renders them grouped by
    /// <see cref="CitationCategory"/> as ordinary styled paragraphs. Unlike <see cref="IndexEntries"/>, the
    /// marks themselves also serialise as hidden <c>TA</c> fields in the body (so they round-trip like
    /// Word's), and the reader rebuilds this list from those fields. Empty when nothing has been marked.
    /// </summary>
    public List<Citation> Citations { get; } = [];

    /// <summary>
    /// The fonts embedded in the document, one <see cref="EmbeddedFont"/> per family. Empty (the default)
    /// means no fonts are embedded, so no <c>word/fontTable.xml</c> part is emitted and existing documents
    /// round-trip unchanged. When non-empty the writer emits the fontTable part, the obfuscated
    /// <c>word/fonts/fontN.odttf</c> font parts and <c>w:embedTrueTypeFonts</c> in word/settings.xml; the
    /// reader de-obfuscates the parts back into the original font bytes here.
    /// </summary>
    public List<EmbeddedFont> EmbeddedFonts { get; } = [];

    /// <summary>
    /// Package parts FreeW does not model but preserves verbatim across a docx round-trip: the original
    /// <c>word/settings.xml</c> (overlaid with FreeW's modelled toggles on write) plus pass-through parts such
    /// as <c>customXml/*</c> and <c>word/webSettings.xml</c>. Empty (the default) for a document authored from
    /// scratch, so such a document emits none of these and round-trips byte-equivalently to before. Populated by
    /// <see cref="FreeW.Core.IO.DocxReader"/> on read and re-emitted by the writer.
    /// </summary>
    public PreservedParts Preserved { get; } = new();

    /// <summary>The body's paragraphs (top-level only; table cell paragraphs are not included).</summary>
    public IEnumerable<Paragraph> Paragraphs => Blocks.OfType<Paragraph>();

    public static TextDocument CreateEmpty()
    {
        var doc = new TextDocument();
        doc.AddBuiltInStyles();
        doc.Blocks.Add(new Paragraph());
        return doc;
    }

    public string PlainText => string.Join("\n", Blocks.Select(BlockPlainText));

    private static string BlockPlainText(Block block) => block switch
    {
        Paragraph p => p.PlainText,
        Table t => string.Join("\n", t.Rows.Select(r => string.Join("\t", r.Cells.Select(c => c.PlainText)))),
        _ => string.Empty
    };

    private void AddBuiltInStyles()
    {
        Styles["Normal"] = new DocumentStyle { Id = "Normal", Name = "Normal" };
        Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "Heading 1",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 16, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 12, SpaceAfterPt = 4 }
        };
        Styles["Heading2"] = new DocumentStyle
        {
            Id = "Heading2",
            Name = "Heading 2",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 13, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 10, SpaceAfterPt = 4 }
        };
        Styles["Heading3"] = new DocumentStyle
        {
            Id = "Heading3",
            Name = "Heading 3",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 12, ColorHex = "#1F3864" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 8, SpaceAfterPt = 4 }
        };
        Styles["Title"] = new DocumentStyle
        {
            Id = "Title",
            Name = "Title",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 28 },
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 8 }
        };
        Styles["Subtitle"] = new DocumentStyle
        {
            Id = "Subtitle",
            Name = "Subtitle",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Italic = true, FontSizePt = 15, ColorHex = "#5A5A5A" },
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 8 }
        };
        Styles["Quote"] = new DocumentStyle
        {
            Id = "Quote",
            Name = "Quote",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Italic = true, ColorHex = "#404040" },
            Paragraph = new ParagraphFormatting
            {
                SpaceBeforePt = 10,
                SpaceAfterPt = 10,
                IndentLeftPt = 36,
                IndentRightPt = 36
            }
        };
        // The built-in figure/table caption style (round-trips via styles.xml like the others).
        Styles[Captions.StyleId] = Captions.BuildCaptionStyle();
        // The built-in index heading/entry styles used by DocumentIndex (round-trip via styles.xml).
        DocumentIndex.EnsureStyles(this);
        // The built-in table-of-figures heading/entry styles used by TableOfFigures (round-trip via styles.xml).
        TableOfFigures.EnsureStyles(this);
        // The built-in Table of Authorities heading/category/entry styles (round-trip via styles.xml).
        TableOfAuthorities.EnsureStyles(this);
    }
}
