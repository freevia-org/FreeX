using System.Collections.Generic;
using System.Linq;
using Free.Shared.Opc;

namespace FreeW.Core.Model;

/// <summary>
/// How an image relates to the surrounding text. <see cref="Inline"/> (the default) keeps the image in
/// the text flow, serialised as <c>wp:inline</c> exactly as before. The remaining modes make the image
/// <em>floating</em> (serialised as <c>wp:anchor</c>) with the matching OOXML wrap element:
/// <see cref="Square"/> -> <c>wp:wrapSquare</c>; <see cref="Tight"/> -> <c>wp:wrapTight</c> with a
/// rectangular <c>wp:wrapPolygon</c>; <see cref="TopAndBottom"/> -> <c>wp:wrapTopAndBottom</c>;
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
/// The side policy for square and tight floating-object wrapping. Maps to
/// <c>wp:wrapSquare/@wrapText</c> and <c>wp:wrapTight/@wrapText</c>.
/// </summary>
public enum FloatingWrapTextSide
{
    BothSides,
    Left,
    Right,
    Largest
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
/// Artistic effect applied non-destructively to a picture, matching Word's Picture Format &gt; Adjust &gt;
/// Artistic Effects gallery. <see cref="None"/> means no artistic effect. Each value is rendered at
/// display time via the pixel pipeline in <c>ImageAdjustHelper.ApplyArtistic</c> and round-trips through
/// DOCX as a <c>freew:artisticEffect</c> extension attribute on <c>a:blip</c> (the standard
/// <c>a:extLst/a14:artisticEffect</c> element is also read when present, mapping to the nearest value).
/// </summary>
public enum ImageArtisticEffect
{
    /// <summary>No artistic effect — original image.</summary>
    None,
    /// <summary>Gaussian blur, matching Word's "Blur" artistic effect.</summary>
    Blur,
    /// <summary>Soft glow diffusion (smoothing + halo), matching Word's "Glow Diffused".</summary>
    GlowDiffused,
    /// <summary>Edge-detection glow with dark fill, matching Word's "Glow Edges".</summary>
    GlowEdges,
    /// <summary>Pencil sketch in greyscale, matching Word's "Pencil Grayscale".</summary>
    PencilGrayscale,
    /// <summary>Colour pencil sketch (edge-detect + saturation boost), matching Word's "Pencil Sketch".</summary>
    PencilSketch,
    /// <summary>Black-and-white line drawing (hard edge-detection), matching Word's "Line Drawing".</summary>
    LineDrawing,
    /// <summary>Paintbrush strokes (median-filter approximation), matching Word's "Paint Brush".</summary>
    Paintbrush,
    /// <summary>Broad paint strokes with saturation boost, matching Word's "Paint Strokes".</summary>
    PaintStrokes,
    /// <summary>High-contrast photocopy threshold, matching Word's "Photocopy".</summary>
    Photocopy,
    /// <summary>Colour posterisation (level quantise), matching Word's "Posterize".</summary>
    Posterize,
    /// <summary>Pastel-chalk smoothing (soften + desaturate), matching Word's "Pastels".</summary>
    Pastels,
    /// <summary>Watercolour look (smooth + gentle saturation boost), matching Word's "Watercolor Sponge".</summary>
    Watercolor,
    /// <summary>Film grain noise overlay, matching Word's "Film Grain".</summary>
    FilmGrain,
    /// <summary>Block-average mosaic (pixelate), matching Word's "Mosaic Bubbles".</summary>
    Mosaic,
}

/// <summary>
/// Recolor mode applied non-destructively to a picture. <see cref="None"/> means the original colour is
/// used. Each mode is rendered at display time via the ImageAdjustHelper pixel pipeline and round-trips
/// through the matching DrawingML element on <c>a:blip</c>.
/// </summary>
public enum ImageRecolorMode
{
    /// <summary>No recolor — original colours.</summary>
    None,
    /// <summary>Greyscale desaturation (a:grayscl).</summary>
    Grayscale,
    /// <summary>Sepia warm-tone duotone effect (a:duotone with brown/white tones).</summary>
    Sepia,
    /// <summary>Washout: very bright, low-contrast, semi-transparent (a:lum + a:alphaModFix).</summary>
    Washout,
    /// <summary>Black and white: greyscale with maximum contrast (a:grayscl + a:lum @contrast).</summary>
    BlackWhite
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

    /// <summary>
    /// Exact external target of a linked DrawingML picture (<c>a:blip/@r:link</c>), or null for an
    /// ordinary embedded picture. Word may author a link by itself or alongside an embedded preview;
    /// <see cref="Bytes"/> carries that preview when present. The DOCX reader/writer preserves the target
    /// verbatim and never resolves or fetches it.
    /// </summary>
    public string? LinkedImageTarget { get; set; }

    /// <summary>
    /// Creates an independent image model carrying the same source bytes and every placement, crop,
    /// adjustment, effect, and accessibility property. The media bytes are immutable document content and may
    /// be shared; the mutable image object itself is not shared, so commands such as resize or crop on an
    /// inserted copy cannot alter the source document.
    /// </summary>
    public InlineImage Clone()
    {
        var clone = (InlineImage)MemberwiseClone();
        clone.ImportedEffects = ImportedEffects?.Clone();
        return clone;
    }

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

    /// <summary>
    /// The Word wrapping side policy for a floating square or tight image. Defaults to both sides.
    /// </summary>
    public FloatingWrapTextSide WrapTextSide { get; set; } = FloatingWrapTextSide.BothSides;

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

    /// <summary>
    /// Z-order index for a floating image (<c>wp:anchor/@relativeHeight</c>). Higher values place the
    /// image in front of lower-indexed images. Defaults to 0. Ignored for inline images. Maps directly
    /// to the OOXML <c>relativeHeight</c> attribute; the overlay canvas sorts children by this value.
    /// </summary>
    public int ZOrderIndex { get; set; }

    // ── Rotate / Flip ─────────────────────────────────────────────────────────────────────────────────
    // Round-trips through a:xfrm @rot (EMU angles: degrees × 60000) and @flipH/@flipV.

    /// <summary>
    /// Clockwise rotation in degrees (0–359). Stored as-authored; writer emits as the DrawingML
    /// <c>a:xfrm/@rot</c> attribute (degrees × 60 000 = EMU angle). Defaults to 0 (no rotation).
    /// </summary>
    public double RotationAngle { get; set; }

    /// <summary>Mirror the image horizontally (<c>a:xfrm/@flipH="1"</c>). Defaults to false.</summary>
    public bool FlipH { get; set; }

    /// <summary>Mirror the image vertically (<c>a:xfrm/@flipV="1"</c>). Defaults to false.</summary>
    public bool FlipV { get; set; }

    // ── Crop ──────────────────────────────────────────────────────────────────────────────────────────
    // Fractions (0–1) of the original image width/height to remove from each edge.
    // Writer emits as a:srcRect percentages (×100000 integer = percentage × 1000).

    /// <summary>Fraction of image width to crop from the left edge (0 = no crop). Round-trips as <c>a:srcRect/@l</c>.</summary>
    public double CropLeft { get; set; }
    /// <summary>Fraction of image width to crop from the right edge (0 = no crop). Round-trips as <c>a:srcRect/@r</c>.</summary>
    public double CropRight { get; set; }
    /// <summary>Fraction of image height to crop from the top edge (0 = no crop). Round-trips as <c>a:srcRect/@t</c>.</summary>
    public double CropTop { get; set; }
    /// <summary>Fraction of image height to crop from the bottom edge (0 = no crop). Round-trips as <c>a:srcRect/@b</c>.</summary>
    public double CropBottom { get; set; }

    /// <summary>True when any crop fraction is non-zero.</summary>
    public bool HasCrop => CropLeft != 0 || CropRight != 0 || CropTop != 0 || CropBottom != 0;

    // ── Picture Border ────────────────────────────────────────────────────────────────────────────────
    // Round-trips through pic:spPr/a:ln: line width (@w in EMU) + solid fill (a:solidFill/a:srgbClr)
    // + dash preset (a:prstDash/@val). Null color = no border.

    /// <summary>
    /// Border line color in 6-digit hex RGB (e.g. "FF0000" for red), or null/empty for no border.
    /// Round-trips as <c>a:solidFill/a:srgbClr/@val</c> inside <c>a:ln</c>.
    /// </summary>
    public string? BorderColorHex { get; set; }

    /// <summary>
    /// Border line width in points (e.g. 0.75 = ¾ pt = 9525 EMU). Round-trips as <c>a:ln/@w</c>.
    /// Defaults to 0.75 pt when a border color is set and this is 0.
    /// </summary>
    public double BorderWidthPt { get; set; }

    /// <summary>
    /// Border dash style token matching DrawingML <c>a:prstDash/@val</c> (e.g. "solid", "dash", "dot").
    /// Null or empty means "solid". Only meaningful when <see cref="BorderColorHex"/> is set.
    /// </summary>
    public string? BorderDash { get; set; }

    /// <summary>True when a border is active (color is non-null/non-empty).</summary>
    public bool HasBorder => !string.IsNullOrEmpty(BorderColorHex);

    // ── Original pixel dimensions ─────────────────────────────────────────────────────────────────────
    // Stored at insert time so Reset Size can restore to 100 % natural size. Not round-tripped
    // through DOCX (it's metadata for the editor only); 0 means "unknown / not set".

    /// <summary>
    /// Original pixel width read from the image header at insert time, used by Reset Size. Not
    /// persisted to DOCX (editor-only state). Defaults to 0 when unknown.
    /// </summary>
    public int OriginalPixelWidth { get; set; }

    /// <summary>
    /// Original pixel height read from the image header at insert time, used by Reset Size. Not
    /// persisted to DOCX (editor-only state). Defaults to 0 when unknown.
    /// </summary>
    public int OriginalPixelHeight { get; set; }

    // ── Adjust: Corrections / Color / Transparency ────────────────────────────────────────────────────
    // Picture Format > Adjust group. Values are non-destructive — Bytes is always the original;
    // adjustments are applied at render time and round-tripped through DOCX IO.
    //
    // IO paths (see DocxWriter/DocxReader):
    //   BrightnessPct  → a:blip child a:lum @bright (per-mille, ×1000) — DrawingML lum element.
    //   ContrastPct    → a:blip child a:lum @contrast (per-mille, ×1000) — same element.
    //   SaturationPct  → a:blip child a:satMod @val (per-mille of 100 % = 100000, so 100 % = 100000).
    //   TransparencyPct→ a:blip child a:alphaModFix @amt (per-mille inverse: 100 % opaque = 100000).
    //   All four are persisted as standard DrawingML; no custom extension needed for these.

    /// <summary>
    /// Brightness adjustment in percent offset from neutral: -100 (black) to +100 (white), 0 = neutral.
    /// Round-trips as <c>a:lum/@bright</c> (per-mille: value × 1000).
    /// </summary>
    public double BrightnessPct { get; set; }

    /// <summary>
    /// Contrast adjustment in percent offset from neutral: -100 (flat) to +100 (maximum), 0 = neutral.
    /// Round-trips as <c>a:lum/@contrast</c> (per-mille: value × 1000).
    /// </summary>
    public double ContrastPct { get; set; }

    /// <summary>
    /// Saturation in percent: 0 = greyscale, 100 = normal, 200 = double, 400 = maximum.
    /// Round-trips as <c>a:satMod/@val</c> (per-mille: value × 1000, so 100 % = 100000).
    /// Defaults to 100 (neutral).
    /// </summary>
    public double SaturationPct { get; set; } = 100;

    /// <summary>
    /// Transparency in percent: 0 = fully opaque, 100 = fully transparent.
    /// Round-trips as <c>a:alphaModFix/@amt</c> (per-mille of opacity: (100-value) × 1000).
    /// Defaults to 0 (fully opaque).
    /// </summary>
    public double TransparencyPct { get; set; }

    /// <summary>True when any pixel adjustment deviates from the neutral defaults.</summary>
    public bool HasAdjustments =>
        BrightnessPct != 0 || ContrastPct != 0 || SaturationPct != 100 || TransparencyPct != 0;

    // ── Recolor ───────────────────────────────────────────────────────────────────────────────────────
    // Picture Format > Color > Recolor presets and Color Tone (temperature).
    // IO paths:
    //   RecolorMode Grayscale  → a:blip child a:grayscl (empty element)
    //   RecolorMode Sepia      → a:blip child a:duotone (brown+white fixed tones) — FreeW extension
    //   RecolorMode Washout    → a:blip child a:alphaModFix @amt 50000 + a:lum @bright 40000
    //                           (half-transparent + bright, matches Word washout preset)
    //   RecolorMode BlackWhite → a:blip child a:grayscl + a:lum @contrast 100000
    //   ColorTemperature       → a:blip child a:clrChange (identity map with a warm/cool overlay tint)
    //                           expressed as w14:colorTemperature in w14 extension block when present,
    //                           or as a FreeW custom attribute on a:blip otherwise.

    /// <summary>
    /// Recolor mode applied to the image. <see cref="ImageRecolorMode.None"/> (default) means no recolor.
    /// Round-trips as <c>a:grayscl</c>, <c>a:duotone</c>, or a combination of existing blip children.
    /// Non-destructive: <see cref="Bytes"/> is never modified.
    /// </summary>
    public ImageRecolorMode RecolorMode { get; set; }

    /// <summary>
    /// Color temperature offset for warming/cooling the image: -100 (cool/blue) to +100 (warm/orange).
    /// 0 = neutral (no temperature shift). Serialised as a FreeW extension attribute on <c>a:blip</c>
    /// (<c>freew:colorTemp</c> in the FreeW extension namespace) to avoid conflicting with standard OOXML.
    /// Non-destructive: applied at render time via the existing pixel-pipeline in ImageAdjustHelper.
    /// </summary>
    public double ColorTemperature { get; set; }

    /// <summary>True when any recolor or temperature adjustment is active.</summary>
    public bool HasRecolor => RecolorMode != ImageRecolorMode.None || ColorTemperature != 0;

    // ── Picture Effects (a:effectLst) ─────────────────────────────────────────────────────────────────
    // Picture Format > Picture Effects group. Values are non-destructive — Bytes is always the original;
    // effects are applied at render time and round-tripped through DOCX as a:effectLst children.
    //
    // IO paths (see DocxWriter/DocxReader):
    //   Shadow      → a:effectLst/a:outerShdw (blurRad, dist, dir, color a:srgbClr)
    //   Glow        → a:effectLst/a:glow (rad, color a:srgbClr with alpha)
    //   Reflection  → a:effectLst/a:reflection (blurRad, stA/stPos/endA/endPos, dist)
    //   SoftEdge    → a:effectLst/a:softEdge (rad)
    //   Bevel       → a:effectLst/a:innerShdw (approximation; bevel requires sp3d which is complex)

    /// <summary>
    /// Shadow effect: 0 = no shadow, 1..5 = outer-shadow presets (increasing distance/blur).
    /// Round-trips as <c>a:effectLst/a:outerShdw</c>.
    /// </summary>
    public int ShadowPreset { get; set; }

    /// <summary>
    /// Glow effect size in points (0 = no glow). Color is stored in <see cref="GlowColorHex"/>.
    /// Round-trips as <c>a:effectLst/a:glow</c>.
    /// </summary>
    public double GlowSizePt { get; set; }

    /// <summary>
    /// Color for the glow effect as a 6-digit RGB hex string (e.g. "4472C4"), or null to use a default
    /// blue accent. Only meaningful when <see cref="GlowSizePt"/> &gt; 0.
    /// </summary>
    public string? GlowColorHex { get; set; }

    /// <summary>
    /// Reflection preset: 0 = no reflection, 1..5 = presets (tight half / full, 4pt/8pt/half-transparent).
    /// Round-trips as <c>a:effectLst/a:reflection</c>.
    /// </summary>
    public int ReflectionPreset { get; set; }

    /// <summary>
    /// Soft-edge radius in points (0 = no soft edge). Round-trips as <c>a:effectLst/a:softEdge</c>.
    /// </summary>
    public double SoftEdgePt { get; set; }

    /// <summary>
    /// Bevel preset: 0 = none, 1 = circle bevel, 2 = relaxed inset, 3 = cross, 4 = cool slant.
    /// Approximated in WPF via an inner-shadow highlight; round-trips as <c>a:effectLst/a:innerShdw</c>
    /// with a distinguishing @dir attribute.
    /// </summary>
    public int BevelPreset { get; set; }

    /// <summary>
    /// Exact DrawingML effect values read from the source package. UI preset fields remain available for
    /// editing, while this payload preserves Word-authored shadow, glow, and reflection semantics on save.
    /// </summary>
    public ShapeEffectLst? ImportedEffects { get; set; }

    /// <summary>True when any picture effect is active.</summary>
    public bool HasEffects =>
        ShadowPreset != 0 || GlowSizePt > 0 || ReflectionPreset != 0 || SoftEdgePt > 0 || BevelPreset != 0
        || ImportedEffects?.HasAny == true;

    // ── Artistic Effects (a14:artisticEffect / freew:artisticEffect) ─────────────────────────────────
    // Picture Format > Adjust > Artistic Effects gallery. Non-destructive: applied at render time by
    // ImageAdjustHelper.ApplyArtistic; Bytes is never modified. Round-trips via a FreeW extension
    // attribute freew:artisticEffect on a:blip (integer id = (int)ArtisticEffect enum value). Standard
    // a:extLst/a14:artisticEffect is read when present and mapped to the nearest enum member.

    /// <summary>
    /// Artistic filter to apply non-destructively. <see cref="ImageArtisticEffect.None"/> (default) means
    /// no artistic effect. Applied at render time by the pixel pipeline; original <see cref="Bytes"/> are
    /// always preserved.
    /// Round-trips as a <c>freew:artisticEffect</c> extension attribute on <c>a:blip</c>.
    /// </summary>
    public ImageArtisticEffect ArtisticEffect { get; set; } = ImageArtisticEffect.None;

    /// <summary>True when an artistic effect other than None is set.</summary>
    public bool HasArtisticEffect => ArtisticEffect != ImageArtisticEffect.None;

    // ── Picture Style ─────────────────────────────────────────────────────────────────────────────────
    // A Picture Style preset bundles border + effect settings. The integer is the preset id (0 = none).
    // Applying a style just sets the individual model fields above, so it's purely a UI convenience;
    // the IO round-trips through those individual fields without needing a separate "style" attribute.

    /// <summary>
    /// The last applied picture-style preset id (0 = none / custom). This is advisory only — the actual
    /// styling is stored in <see cref="BorderColorHex"/>, <see cref="BorderWidthPt"/>,
    /// <see cref="ShadowPreset"/>, etc. It is NOT persisted to DOCX (it's a UI state hint).
    /// </summary>
    public int PictureStylePreset { get; set; }
}

/// <summary>
/// A named picture-style preset that bundles border + effect settings, matching Word's Picture Styles
/// gallery (the 28 presets on the Picture Format contextual tab). Applying a preset calls
/// <see cref="FreeW.Core.Model.InlineImage"/> property setters — it does not add a new persistence field;
/// the individual fields carry the data through DOCX IO as before.
/// </summary>
public sealed record PictureStylePreset(
    /// <summary>Stable id (1-based). Used as the command-id suffix: freew.image-style-{Id}.</summary>
    int Id,
    /// <summary>Gallery display name (shown as tooltip/label).</summary>
    string Name,
    /// <summary>Border colour hex (6-digit RGB), or null for no border.</summary>
    string? BorderColorHex,
    /// <summary>Border width in points. Ignored when BorderColorHex is null.</summary>
    double BorderWidthPt,
    /// <summary>Border dash style token (null/"solid"/dash/dot/etc).</summary>
    string? BorderDash,
    /// <summary>Shadow preset (0=none).</summary>
    int ShadowPreset,
    /// <summary>Reflection preset (0=none).</summary>
    int ReflectionPreset,
    /// <summary>Soft-edge radius in points (0=none).</summary>
    double SoftEdgePt);

/// <summary>
/// Static catalog of the 12 Picture Style presets displayed in the Picture Styles gallery on the
/// Picture Format ribbon tab. Each preset bundles a border + optional shadow/reflection/soft-edge.
/// The gallery uses IDs 1–12 to match the <c>freew.image-style-{id}</c> command convention.
/// </summary>
public static class PictureStyleCatalog
{
    /// <summary>All built-in picture-style presets, in gallery display order.</summary>
    public static readonly IReadOnlyList<PictureStylePreset> Catalog =
    [
        // ── Row 1: Simple borders ─────────────────────────────────────────────────────────────────────
        new(1,  "Simple Frame, White",          "FFFFFF", 2.25, "solid",  0, 0, 0),
        new(2,  "Simple Frame, Black",          "000000", 2.25, "solid",  0, 0, 0),
        new(3,  "Thick Matte, Black",           "000000", 6.0,  "solid",  0, 0, 0),
        new(4,  "Double Frame, Black",          "000000", 1.5,  "solid",  0, 0, 0),
        // ── Row 2: Soft frames ───────────────────────────────────────────────────────────────────────
        new(5,  "Soft Edge Rectangle",          null,     0,    null,     0, 0, 5.0),
        new(6,  "Soft Edge Oval",               null,     0,    null,     0, 0, 10.0),
        // ── Row 3: Shadow styles ─────────────────────────────────────────────────────────────────────
        new(7,  "Drop Shadow Rectangle",        null,     0,    null,     1, 0, 0),
        new(8,  "Drop Shadow White",            "FFFFFF", 2.25, "solid",  1, 0, 0),
        new(9,  "Perspective Shadow",           null,     0,    null,     3, 0, 0),
        // ── Row 4: Reflection styles ─────────────────────────────────────────────────────────────────
        new(10, "Reflected Rounded Rectangle",  null,     0,    null,     0, 1, 0),
        new(11, "Reflected Bevel, White",       "FFFFFF", 2.25, "solid",  0, 4, 0),
        new(12, "Metal Rounded Rectangle",      "4472C4", 3.0,  "solid",  2, 0, 0),
    ];

    /// <summary>Find a preset by id, or null if not found.</summary>
    public static PictureStylePreset? FindById(int id) =>
        Catalog.FirstOrDefault(p => p.Id == id);
}

/// <summary>
/// A contiguous span of text sharing one run formatting, or — when <see cref="Image"/> is set — an
/// inline image anchored in the run flow. An image run carries no text (<see cref="Text"/> is empty).
/// </summary>
/// <summary>
/// An external Word master-document subdocument anchor. The target is retained exactly as authored in
/// the package relationship (for example <c>Chapter1.docx</c> or a <c>file:</c> URI).
/// </summary>
public sealed record SubDocumentReference(string Target);

public sealed class Run(string text, RunFormatting? formatting = null)
{
    private string _text = text;

    /// <summary>
    /// The literal text for ordinary runs, or the concatenated base text for a ruby annotation. Keeping ruby
    /// base fragments authoritative lets callers construct the annotation incrementally without leaving its
    /// visible fallback stale.
    /// </summary>
    public string Text
    {
        get => Ruby?.BaseText ?? _text;
        set => _text = value;
    }
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
    /// Optional Word ruby (phonetic guide) annotation. <see cref="Text"/> mirrors the base text so plain-text
    /// consumers and renderers that do not yet paint phonetic guides still retain the visible characters.
    /// The annotation preserves the base and guide fragments for lossless WordprocessingML round-tripping.
    /// </summary>
    public RubyAnnotation? Ruby { get; set; }

    /// <summary>Creates a run carrying a Word ruby annotation and its base text fallback.</summary>
    public static Run FromRuby(RubyAnnotation ruby) =>
        new(ruby.BaseText, ruby.BaseFragments.FirstOrDefault()?.Formatting) { Ruby = ruby };

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
    /// Optional floating drawing group (<c>wpg:wgp</c>). When non-null this run carries a group of two or
    /// more drawing objects that move and render as a unit on the floating-objects overlay canvas.
    /// Serialised as <c>w:drawing/wp:anchor/a:graphic/a:graphicData[uri=wpg]/wpg:wgp</c>.
    /// </summary>
    public DrawingGroup? DrawingGroup { get; set; }

    /// <summary>Creates a run that carries a floating drawing group instead of text.</summary>
    public static Run FromDrawingGroup(DrawingGroup group) =>
        new(string.Empty) { DrawingGroup = group };

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
    /// Optional external master-document subdocument anchor (<c>w:subDoc</c>). It occupies an ordered
    /// position in the paragraph but carries no literal text; the target is emitted through an external
    /// <c>subDocument</c> relationship.
    /// </summary>
    public SubDocumentReference? SubDocument { get; set; }

    /// <summary>Creates a textless run that anchors an external Word subdocument.</summary>
    public static Run FromSubDocument(string target) =>
        new(string.Empty) { SubDocument = new SubDocumentReference(target) };

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
    /// When non-null, this run is a <em>complex</em> Word field (Word's <c>w:fldChar</c> begin / separate /
    /// end run sequence with a <c>w:instrText</c> instruction) rather than the self-contained
    /// <c>w:fldSimple</c> carried by <see cref="FieldKind"/>. It preserves the raw field-code instruction
    /// (e.g. <c> PAGE </c>, <c> DATE \@ "M/d/yyyy" </c>, <c> FILENAME </c>, <c> AUTHOR </c>, <c> REF bm </c>)
    /// verbatim so <em>any</em> field round-trips, and the run's <see cref="Text"/> doubles as the cached
    /// result (the last-computed display value) shown when field codes are hidden. This is the generic
    /// construct behind Insert &gt; Quick Parts &gt; Field, Alt+F9 (toggle codes), and F9 (update). Modelled
    /// as an optional run mark, mirroring <see cref="CrossReference"/>, so it round-trips without a new block
    /// type.
    /// </summary>
    public ComplexField? ComplexField { get; set; }

    /// <summary>
    /// Creates a complex field run with the given raw <paramref name="instruction"/> (e.g. <c> PAGE </c>)
    /// and cached display <paramref name="result"/> (the last-computed value). The run serialises as the
    /// <c>w:fldChar</c> begin / <c>w:instrText</c> / separate / result / end sequence.
    /// </summary>
    public static Run ComplexFieldRun(string instruction, string result = "", bool showCode = false, RunFormatting? formatting = null) =>
        new(result, formatting) { ComplexField = new ComplexField(instruction, showCode) };

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
    /// When true, this run is a manual column break (<c>w:br w:type="column"</c>). It carries no text
    /// and advances following content to the next text column, or to the next page in a one-column section.
    /// </summary>
    public bool IsColumnBreak { get; set; }

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

    /// <summary>
    /// The shared Word move-revision identifier (w:id on w:moveFrom/w:moveTo), or null for an ordinary
    /// insertion/deletion. The move source still uses <see cref="RevisionKind.Deleted"/> and the destination
    /// uses <see cref="RevisionKind.Inserted"/> so existing review rendering remains unchanged.
    /// </summary>
    public int? MoveRevisionId { get; set; }

    /// <summary>
    /// A tracked <em>formatting</em> change on this run (Word's w:rPrChange), or null when the run's
    /// formatting was not changed under Track Changes. When set, <see cref="Formatting"/> is the new
    /// (current) formatting and <see cref="FormatRevision"/> carries the <em>previous</em> formatting plus
    /// the author/date who made the change. This is independent of <see cref="Revision"/>: a run can be an
    /// ordinary (un-inserted/un-deleted) run whose formatting was tracked-changed. Accepting keeps the new
    /// formatting and clears the mark; rejecting restores the previous formatting. Modelled as an optional
    /// run mark, mirroring <see cref="RevisionAuthor"/>/<see cref="RevisionDateXml"/>.
    /// </summary>
    public FormatRevision? FormatRevision { get; set; }

    /// <summary>Creates a run that carries an inline image instead of text.</summary>
    public static Run FromImage(InlineImage image) => new(string.Empty) { Image = image };

    /// <summary>Creates a manual page-break run (<c>w:br w:type="page"</c>).</summary>
    public static Run PageBreak() => new(string.Empty) { IsPageBreak = true };

    /// <summary>Creates a manual column-break run (<c>w:br w:type="column"</c>).</summary>
    public static Run ColumnBreak() => new(string.Empty) { IsColumnBreak = true };

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

    /// <summary>Creates a Title document-property field run (renders as <see cref="DocumentProperties.Title"/>).</summary>
    public static Run TitleField(string cached = "", RunFormatting? formatting = null) =>
        new(cached, formatting) { FieldKind = RunFieldKind.Title };

    /// <summary>Creates a Subject document-property field run (renders as <see cref="DocumentProperties.Subject"/>).</summary>
    public static Run SubjectField(string cached = "", RunFormatting? formatting = null) =>
        new(cached, formatting) { FieldKind = RunFieldKind.Subject };

    /// <summary>Creates a Keywords document-property field run (renders as <see cref="DocumentProperties.Keywords"/>).</summary>
    public static Run KeywordsField(string cached = "", RunFormatting? formatting = null) =>
        new(cached, formatting) { FieldKind = RunFieldKind.Keywords };

    /// <summary>Creates a Comments/Description document-property field run (renders as <see cref="DocumentProperties.Comments"/>).</summary>
    public static Run DocCommentsField(string cached = "", RunFormatting? formatting = null) =>
        new(cached, formatting) { FieldKind = RunFieldKind.DocComments };

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
    /// with the optional <paramref name="tag"/> / <paramref name="alias"/>. The nullable
    /// <paramref name="multiLine"/> preserves absent, explicitly disabled, and enabled w:multiLine
    /// states. Serialises as a w:sdt (plain-text) wrapping the run.
    /// </summary>
    public static Run PlainTextControl(
        string text,
        string? tag = null,
        string? alias = null,
        bool? multiLine = null) =>
        new(text)
        {
            Control = new ContentControl(
                ContentControlKind.PlainText,
                tag,
                alias,
                PlainTextMultiLine: multiLine)
        };

    /// <summary>
    /// Creates a checkbox content control run. The run's <see cref="Text"/> is the checked (☒) or
    /// unchecked (☐) glyph matching <paramref name="checked"/>, and the control records the state.
    /// Serialises as a w:sdt with a checkbox w:sdtPr wrapping the glyph run.
    /// </summary>
    public static Run CheckBoxControl(
        bool @checked,
        string? tag = null,
        string? alias = null,
        ContentControlCheckBoxMetadata? checkBoxMetadata = null) =>
        new(@checked ? ContentControl.CheckedGlyph : ContentControl.UncheckedGlyph)
        {
            Control = new ContentControl(
                ContentControlKind.CheckBox,
                tag,
                alias,
                @checked,
                CheckBoxMetadata: checkBoxMetadata)
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
    /// cref="ContentControl.DefaultDateFormat"/>). Optional <paramref name="dateMetadata"/> preserves
    /// Word's date value, calendar, locale, and mapped-data representation. Serialises as a w:sdt with
    /// a w:date w:sdtPr.
    /// </summary>
    public static Run DatePickerControl(
        string text,
        string? tag = null,
        string? alias = null,
        string? dateFormat = null,
        ContentControlDateMetadata? dateMetadata = null) =>
        new(text)
        {
            Control = new ContentControl(
                ContentControlKind.DatePicker, tag, alias,
                DateFormat: dateFormat ?? ContentControl.DefaultDateFormat,
                DateMetadata: dateMetadata)
        };

    /// <summary>
    /// Creates a drop-down-list content control run offering <paramref name="items"/>; the run's
    /// <see cref="Text"/> is the currently displayed item (the first item's display text when none is
    /// given). Serialises as a w:sdt with a w:dropDownList w:sdtPr carrying w:listItem entries.
    /// </summary>
    public static Run DropDownListControl(
        IReadOnlyList<ContentControlListItem> items, string? selectedText = null,
        string? tag = null, string? alias = null, string? lastValue = null) =>
        new(selectedText ?? (items.Count > 0 ? items[0].DisplayText : string.Empty))
        {
            Control = new ContentControl(
                ContentControlKind.DropDownList, tag, alias,
                ListItems: items,
                ListLastValue: lastValue)
        };

    /// <summary>
    /// Creates a combo-box content control run offering <paramref name="items"/> (and allowing free text);
    /// the run's <see cref="Text"/> is the currently displayed value. Serialises as a w:sdt with a
    /// w:comboBox w:sdtPr carrying w:listItem entries.
    /// </summary>
    public static Run ComboBoxControl(
        IReadOnlyList<ContentControlListItem> items, string? selectedText = null,
        string? tag = null, string? alias = null, string? lastValue = null) =>
        new(selectedText ?? (items.Count > 0 ? items[0].DisplayText : string.Empty))
        {
            Control = new ContentControl(
                ContentControlKind.ComboBox, tag, alias,
                ListItems: items,
                ListLastValue: lastValue)
        };

    /// <summary>
    /// Creates a picture content control whose content is <paramref name="image"/>. Serialises as a
    /// w:sdt with an empty w:picture property wrapping the ordinary DrawingML picture run.
    /// </summary>
    public static Run PictureControl(InlineImage image, string? tag = null, string? alias = null) =>
        new(string.Empty)
        {
            Image = image,
            Control = new ContentControl(ContentControlKind.Picture, tag, alias)
        };

    /// <summary>
    /// Creates an inline building-block gallery content control backed by w:sdtPr/w:docPartObj.
    /// The gallery is required; category is optional and unique maps to the presence of w:docPartUnique.
    /// </summary>
    public static Run BuildingBlockGalleryControl(
        string text,
        string gallery,
        string? category = null,
        bool unique = false,
        string? tag = null,
        string? alias = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gallery);
        return new Run(text)
        {
            Control = new ContentControl(
                ContentControlKind.BuildingBlockGallery,
                tag,
                alias,
                DocPartGallery: gallery,
                DocPartCategory: category,
                DocPartUnique: unique)
        };
    }

    /// <summary>
    /// Creates an inline document-part list content control backed by w:sdtPr/w:docPartList.
    /// The gallery is required; category is optional and unique maps to the presence of w:docPartUnique.
    /// </summary>
    public static Run DocumentPartListControl(
        string text,
        string gallery,
        string? category = null,
        bool unique = false,
        string? tag = null,
        string? alias = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gallery);
        return new Run(text)
        {
            Control = new ContentControl(
                ContentControlKind.DocumentPart,
                tag,
                alias,
                DocPartGallery: gallery,
                DocPartCategory: category,
                DocPartUnique: unique)
        };
    }

    /// <summary>
    /// Creates an inline Group content control backed by w:sdtPr/w:group. Group controls protect their
    /// contained controls as one unit while retaining ordinary run-level ownership.
    /// </summary>
    public static Run GroupControl(string text, string? tag = null, string? alias = null) =>
        new(text) { Control = new ContentControl(ContentControlKind.Group, tag, alias) };

    /// <summary>Creates an inline Citation content control backed by w:sdtPr/w:citation.</summary>
    public static Run CitationControl(string text, string? tag = null, string? alias = null) =>
        new(text) { Control = new ContentControl(ContentControlKind.Citation, tag, alias) };
}

/// <summary>A formatted fragment in the base or phonetic text of a Word ruby annotation.</summary>
public sealed record RubyTextFragment(string Text, RunFormatting Formatting);

/// <summary>
/// WordprocessingML <c>w:ruby</c> phonetic-guide payload. The base text is the normal reading text; the
/// phonetic text is typically rendered above it by Word. Size and raise use Word's half-point values.
/// </summary>
public sealed class RubyAnnotation
{
    public List<RubyTextFragment> BaseFragments { get; } = [];
    public List<RubyTextFragment> PhoneticFragments { get; } = [];
    public RubyAlignment Alignment { get; set; } = RubyAlignment.Center;
    public int? PhoneticSizeHalfPoints { get; set; }
    public int? RaiseHalfPoints { get; set; }

    /// <summary>Concatenated base text, used by <see cref="Run.Text"/> as the visible fallback.</summary>
    public string BaseText => string.Concat(BaseFragments.Select(fragment => fragment.Text));

    /// <summary>Creates an independent copy for document merge and undo snapshots.</summary>
    public RubyAnnotation Clone()
    {
        var clone = new RubyAnnotation
        {
            Alignment = Alignment,
            PhoneticSizeHalfPoints = PhoneticSizeHalfPoints,
            RaiseHalfPoints = RaiseHalfPoints
        };
        clone.BaseFragments.AddRange(BaseFragments);
        clone.PhoneticFragments.AddRange(PhoneticFragments);
        return clone;
    }
}

/// <summary>Alignment values for WordprocessingML <c>w:rubyPr/w:rubyAlign</c>.</summary>
public enum RubyAlignment
{
    Center,
    DistributeLetter,
    DistributeSpace,
    Left,
    Right
}

/// <summary>
/// The kind of content control (structured document tag, w:sdt) a <see cref="Run"/> belongs to.
/// <see cref="PlainText"/> is a plain-text control (w:sdtPr/w:text); <see cref="CheckBox"/> is a
/// checkbox control (w:sdtPr/w14:checkbox or w:checkbox) whose run carries the checked/unchecked glyph;
/// <see cref="RichText"/> is a rich-text control (w:sdtPr/w:richText) that may hold formatted content;
/// <see cref="DatePicker"/> is a date picker (w:sdtPr/w:date) whose run carries the displayed date;
/// <see cref="DropDownList"/> is a drop-down list (w:sdtPr/w:dropDownList + w:listItem entries) the user
/// can only pick from; <see cref="ComboBox"/> is a combo box (w:sdtPr/w:comboBox + w:listItem entries)
/// that additionally allows free text; <see cref="Picture"/> is a picture control (w:sdtPr/w:picture)
/// whose run carries an <see cref="InlineImage"/>; <see cref="DocumentPart"/> is a document-part list
/// (w:sdtPr/w:docPartList); <see cref="BuildingBlockGallery"/> is a building-block gallery control
/// (w:sdtPr/w:docPartObj); <see cref="Group"/> is a Group control (w:sdtPr/w:group); and
/// <see cref="Citation"/> is a citation control (w:sdtPr/w:citation).
/// </summary>
public enum ContentControlKind
{
    PlainText,
    CheckBox,
    RichText,
    DatePicker,
    DropDownList,
    ComboBox,
    Picture,
    DocumentPart,
    BuildingBlockGallery,
    Group,
    Citation
}

/// <summary>Word content-control locking from w:sdtPr/w:lock.</summary>
public enum ContentControlLockMode
{
    NotSpecified,
    Unlocked,
    ContentLocked,
    ControlLocked,
    ControlAndContentLocked
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

/// <summary>Word data binding carried by w:sdtPr/w:dataBinding.</summary>
public sealed record ContentControlDataBinding(
    string? StoreItemId,
    string? XPath,
    string? PrefixMappings);

/// <summary>
/// Word-specific structured-document-tag metadata that remains active after displayed content is edited.
/// <see cref="TabIndex"/> preserves the exact optional w:sdtPr/w:tabIndex/@w:val source token.
/// </summary>
public sealed record ContentControlWordMetadata(
    string? Id = null,
    ContentControlDataBinding? DataBinding = null,
    string? PlaceholderDocPart = null,
    bool ShowingPlaceholder = false,
    bool Temporary = false,
    string? Appearance = null,
    string? Color = null,
    string? TabIndex = null);

/// <summary>
/// Word-specific date-picker metadata from w:date. Null fields represent absent OOXML values and are
/// omitted when the control is saved.
/// </summary>
public sealed record ContentControlDateMetadata(
    string? FullDate = null,
    string? Calendar = null,
    string? LanguageId = null,
    string? StoreMappedDataAs = null);

/// <summary>
/// One optional Word checkbox-state symbol from w14:checkedState or w14:uncheckedState. Null fields
/// preserve absent attributes; non-null strings retain the authored glyph codepoint and font tokens.
/// </summary>
public sealed record ContentControlCheckBoxStateMetadata(
    string? GlyphCodePoint = null,
    string? Font = null);

/// <summary>
/// Optional Word checkbox symbol metadata. A null state represents an absent OOXML state element.
/// </summary>
public sealed record ContentControlCheckBoxMetadata(
    ContentControlCheckBoxStateMetadata? CheckedState = null,
    ContentControlCheckBoxStateMetadata? UncheckedState = null);

/// <summary>
/// An immutable content-control (structured document tag / w:sdt) mark carried by a <see cref="Run"/>.
/// Records the control <see cref="Kind"/>, an optional <see cref="Tag"/> (w:tag) and <see cref="Alias"/>
/// (w:alias), and the kind-specific extras: <see cref="Checked"/> (checkbox state), <see cref="DateFormat"/>
/// (a date picker's w:dateFormat string), <see cref="DateMetadata"/> (the remaining w:date metadata), and
/// <see cref="ListItems"/> (the w:listItem choices of a drop-down list or combo box), and
/// <see cref="ListLastValue"/> (the optional w:lastValue on the list owner),
/// <see cref="CheckBoxMetadata"/> (optional w14 checkbox-state glyph/font metadata), and
/// <see cref="PlainTextMultiLine"/> (the optional w:text/@w:multiLine state). Document-part lists
/// and building-block gallery controls additionally retain <see cref="DocPartGallery"/>,
/// <see cref="DocPartCategory"/>, and <see cref="DocPartUnique"/>.
/// Modelled as an immutable record so it mirrors how other small marks
/// (<see cref="PageBorder"/>, <see cref="TableFormatting"/>) are modelled and so consecutive runs can
/// share one instance to coalesce into a single w:sdt on save.
/// </summary>
public sealed record ContentControl(
    ContentControlKind Kind,
    string? Tag = null,
    string? Alias = null,
    bool Checked = false,
    string? DateFormat = null,
    IReadOnlyList<ContentControlListItem>? ListItems = null,
    ContentControlLockMode LockMode = ContentControlLockMode.NotSpecified,
    ContentControlWordMetadata? WordMetadata = null,
    string? DocPartGallery = null,
    string? DocPartCategory = null,
    bool DocPartUnique = false,
    ContentControlDateMetadata? DateMetadata = null,
    string? ListLastValue = null,
    bool? PlainTextMultiLine = null,
    ContentControlCheckBoxMetadata? CheckBoxMetadata = null)
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
/// Number format for footnote/endnote reference marks. Maps to w:numFmt/@w:val inside
/// w:footnotePr / w:endnotePr in word/settings.xml. The default is <see cref="Decimal"/> (1, 2, 3, …).
/// </summary>
public enum NoteNumberFormat
{
    /// <summary>Arabic numerals: 1, 2, 3, … (w:numFmt val="decimal", the Word default).</summary>
    Decimal,
    /// <summary>Lower-case Roman numerals: i, ii, iii, … (w:numFmt val="lowerRoman").</summary>
    LowerRoman,
    /// <summary>Upper-case Roman numerals: I, II, III, … (w:numFmt val="upperRoman").</summary>
    UpperRoman,
    /// <summary>Lower-case letters: a, b, c, … (w:numFmt val="lowerLetter").</summary>
    LowerLetter,
    /// <summary>Upper-case letters: A, B, C, … (w:numFmt val="upperLetter").</summary>
    UpperLetter,
    /// <summary>Symbol sequence: *, †, ‡, §, **, †† … (w:numFmt val="chicago").</summary>
    Chicago
}

/// <summary>
/// Controls when footnote/endnote numbering restarts. Maps to w:numRestart/@w:val inside
/// w:footnotePr / w:endnotePr in word/settings.xml. The default is <see cref="Continuous"/>
/// (numbering runs across the whole document without restarting).
/// </summary>
public enum NoteNumberRestart
{
    /// <summary>Continuous numbering through the whole document (the Word default).</summary>
    Continuous,
    /// <summary>Restart numbering at 1 on each new section (w:numRestart val="eachSect").</summary>
    EachSection,
    /// <summary>Restart numbering at 1 on each new page (w:numRestart val="eachPage"; footnotes only).</summary>
    EachPage
}

/// <summary>
/// Document-level footnote (or endnote) numbering options stored in word/settings.xml as
/// w:footnotePr (or w:endnotePr). All properties have Word-default values so a freshly created
/// document round-trips without emitting these elements.
/// </summary>
public sealed class NoteNumberingOptions
{
    /// <summary>
    /// Number format for the reference marks. Defaults to <see cref="NoteNumberFormat.Decimal"/>
    /// (1, 2, 3, …), matching Word's default.
    /// </summary>
    public NoteNumberFormat NumberFormat { get; set; } = NoteNumberFormat.Decimal;

    /// <summary>
    /// The starting number for the first reference mark. Defaults to 1 (Word's default).
    /// </summary>
    public int StartAt { get; set; } = 1;

    /// <summary>
    /// When numbering restarts. Defaults to <see cref="NoteNumberRestart.Continuous"/>,
    /// matching Word's default.
    /// </summary>
    public NoteNumberRestart NumberRestart { get; set; } = NoteNumberRestart.Continuous;

    /// <summary>
    /// Returns true when all properties match Word's defaults, meaning the w:footnotePr /
    /// w:endnotePr element need not be emitted (keeps a freshly authored document minimal).
    /// </summary>
    public bool IsDefault =>
        NumberFormat == NoteNumberFormat.Decimal &&
        StartAt == 1 &&
        NumberRestart == NoteNumberRestart.Continuous;
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
/// formatted (a journal article/article in a periodical cites its journal/volume/pages, an electronic source
/// or web site cites its URL, etc.). The numeric
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

    /// <summary>A report (adds the responsible institution plus report publication fields).</summary>
    Report = 3,

    /// <summary>A chapter or section within a book (adds book title, chapter number and page range).</summary>
    BookSection = 4,

    /// <summary>A paper in conference proceedings (adds the conference/proceedings name and page range).</summary>
    ConferenceProceedings = 5,

    /// <summary>A Word article-in-a-periodical source (adds periodical name, volume, issue and page range).</summary>
    ArticleInPeriodical = 6,

    /// <summary>A Word electronic source (adds URL and accessed date fields).</summary>
    ElectronicSource = 7,

    /// <summary>A Word patent source (adds inventor, patent number and filing/jurisdiction fields).</summary>
    Patent = 8,

    /// <summary>A Word interview source (adds interviewee/interviewer, medium and date fields).</summary>
    Interview = 9,

    /// <summary>A Word miscellaneous source for lightly structured material.</summary>
    Misc = 10,

    /// <summary>A Word film source (adds director, producer, writer, performer and production fields).</summary>
    Film = 11,

    /// <summary>A Word sound recording source (adds artist/composer/performer and album fields).</summary>
    SoundRecording = 12,

    /// <summary>A Word art source (adds artist, medium and holding/location fields).</summary>
    Art = 13,

    /// <summary>A Word internet site source (adds site/publisher, URL and accessed-date fields).</summary>
    InternetSite = 14,

    /// <summary>A Word performance source (adds performer/conductor, theater and date fields).</summary>
    Performance = 15,

    /// <summary>A Word bibliography case source (distinct from Table of Authorities citation marks).</summary>
    Case = 16,
}

/// <summary>
/// A Word bibliography personal-author row (<c>b:NameList/b:Person</c>).
/// </summary>
public sealed record SourceAuthorPerson(string First, string Middle, string Last)
{
    public string DisplayName => FormatDisplayName(this);

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(First)
        && string.IsNullOrWhiteSpace(Middle)
        && string.IsNullOrWhiteSpace(Last);

    public static SourceAuthorPerson Create(string? first, string? middle, string? last) =>
        new((first ?? string.Empty).Trim(), (middle ?? string.Empty).Trim(), (last ?? string.Empty).Trim());

    public static string FormatDisplayName(SourceAuthorPerson person)
    {
        ArgumentNullException.ThrowIfNull(person);

        return string.Join(
            " ",
            new[] { person.First, person.Middle, person.Last }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Trim()));
    }

    public static string FormatDisplayText(IEnumerable<SourceAuthorPerson> people) =>
        string.Join(
            "; ",
            people
                .Where(person => person is not null && !person.IsEmpty)
                .Select(FormatDisplayName)
                .Where(name => name.Length > 0));
}

/// <summary>
/// A bibliographic source the document can cite: a short <see cref="Tag"/> (a stable identifier used
/// to reference the source, e.g. <c>"Knuth1997"</c>) plus author/title/year and common Word bibliography
/// fields such as city, institution, edition, standard number, short title and comments. A <see cref="SourceType"/>
/// selects type-specific formatting and carries the extra fields that type needs
/// (journal/volume/issue/pages for an article, url/accessed date for a web site, institution for a report,
/// book title/chapter/pages for a book section, conference name/pages for proceedings). Kept deliberately
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

    /// <summary>
    /// Structured personal authors parsed from or written to Word <c>b:NameList/b:Person</c> rows.
    /// <see cref="Author"/> remains the display/compatibility string for citation formatting.
    /// </summary>
    public IReadOnlyList<SourceAuthorPerson> PersonalAuthors { get; init; } = [];

    /// <summary>
    /// Structured corporate author text parsed from or written to Word <c>b:Corporate</c>. Legacy sources
    /// without structured data continue to use <see cref="Author"/> as their corporate/ambiguous value.
    /// </summary>
    public string? CorporateAuthor { get; init; }

    /// <summary>
    /// Structured personal editors parsed from or written to Word <c>b:Editor/b:NameList/b:Person</c> rows.
    /// </summary>
    public IReadOnlyList<SourceAuthorPerson> Editors { get; init; } = [];

    /// <summary>
    /// Structured personal translators parsed from or written to Word <c>b:Translator/b:NameList/b:Person</c> rows.
    /// </summary>
    public IReadOnlyList<SourceAuthorPerson> Translators { get; init; } = [];

    /// <summary>The title of the work. Empty when unknown.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The containing book title for a <see cref="SourceType.BookSection"/>; null when unknown.</summary>
    public string? BookTitle { get; init; }

    /// <summary>The conference or proceedings name for <see cref="SourceType.ConferenceProceedings"/>; null when unknown.</summary>
    public string? ConferenceName { get; init; }

    /// <summary>The inventor role for a <see cref="SourceType.Patent"/>; null when unknown.</summary>
    public string? Inventor { get; init; }

    /// <summary>The interviewee role for a <see cref="SourceType.Interview"/>; null when unknown.</summary>
    public string? Interviewee { get; init; }

    /// <summary>The interviewer role for a <see cref="SourceType.Interview"/>; null when unknown.</summary>
    public string? Interviewer { get; init; }

    /// <summary>The artist role for <see cref="SourceType.Art"/> or <see cref="SourceType.SoundRecording"/>; null when unknown.</summary>
    public string? Artist { get; init; }

    /// <summary>The composer role for <see cref="SourceType.SoundRecording"/>; null when unknown.</summary>
    public string? Composer { get; init; }

    /// <summary>The conductor role for <see cref="SourceType.SoundRecording"/> or <see cref="SourceType.Performance"/>; null when unknown.</summary>
    public string? Conductor { get; init; }

    /// <summary>The director role for <see cref="SourceType.Film"/>; null when unknown.</summary>
    public string? Director { get; init; }

    /// <summary>The performer role for <see cref="SourceType.Film"/>, <see cref="SourceType.SoundRecording"/>, or <see cref="SourceType.Performance"/>; null when unknown.</summary>
    public string? Performer { get; init; }

    /// <summary>The producer role for <see cref="SourceType.Film"/> or <see cref="SourceType.SoundRecording"/>; null when unknown.</summary>
    public string? ProducerName { get; init; }

    /// <summary>The writer role for <see cref="SourceType.Film"/>; null when unknown.</summary>
    public string? Writer { get; init; }

    /// <summary>The year of publication. Empty when unknown.</summary>
    public string Year { get; init; } = string.Empty;

    /// <summary>The month value used by Word bibliography source types that carry a full date; null when unknown.</summary>
    public string? Month { get; init; }

    /// <summary>The day value used by Word bibliography source types that carry a full date; null when unknown.</summary>
    public string? Day { get; init; }

    /// <summary>The institution responsible for a report; null when unknown / not applicable.</summary>
    public string? Institution { get; init; }

    /// <summary>The publisher of the work, or null when unknown / not applicable.</summary>
    public string? Publisher { get; init; }

    /// <summary>The publication city/place for a book; null when unknown / not applicable.</summary>
    public string? City { get; init; }

    /// <summary>The edition statement for a book; null when unknown / not applicable.</summary>
    public string? Edition { get; init; }

    /// <summary>A Word-style standard number such as ISBN or ISSN; null when unknown / not applicable.</summary>
    public string? StandardNumber { get; init; }

    /// <summary>The chapter number for a <see cref="SourceType.BookSection"/>; null when unknown.</summary>
    public string? ChapterNumber { get; init; }

    /// <summary>The patent number for a <see cref="SourceType.Patent"/>; null when unknown.</summary>
    public string? PatentNumber { get; init; }

    /// <summary>The case number for a <see cref="SourceType.Case"/>; null when unknown.</summary>
    public string? CaseNumber { get; init; }

    /// <summary>The court for a <see cref="SourceType.Case"/>; null when unknown.</summary>
    public string? Court { get; init; }

    /// <summary>The reporter for a <see cref="SourceType.Case"/>; null when unknown.</summary>
    public string? Reporter { get; init; }

    /// <summary>The country/region jurisdiction for a <see cref="SourceType.Patent"/> or <see cref="SourceType.Case"/>; null when unknown.</summary>
    public string? CountryRegion { get; init; }

    /// <summary>The state/province jurisdiction for a <see cref="SourceType.Patent"/> or <see cref="SourceType.Case"/>; null when unknown.</summary>
    public string? StateProvince { get; init; }

    /// <summary>A source-specific medium, such as an interview medium or miscellaneous format; null when unknown.</summary>
    public string? Medium { get; init; }

    /// <summary>A source-specific type/kind string for miscellaneous Word sources; null when unknown.</summary>
    public string? SourceKind { get; init; }

    /// <summary>The album title for a <see cref="SourceType.SoundRecording"/>; null when unknown.</summary>
    public string? AlbumTitle { get; init; }

    /// <summary>The production company for a <see cref="SourceType.Film"/>; null when unknown.</summary>
    public string? ProductionCompany { get; init; }

    /// <summary>The recording number for a <see cref="SourceType.SoundRecording"/>; null when unknown.</summary>
    public string? RecordingNumber { get; init; }

    /// <summary>The theater or venue for a <see cref="SourceType.Performance"/>; null when unknown.</summary>
    public string? Theater { get; init; }

    /// <summary>A shortened citation title; null when unknown.</summary>
    public string? ShortTitle { get; init; }

    /// <summary>Free-form source comments/notes; null when unknown.</summary>
    public string? Comments { get; init; }

    /// <summary>
    /// The periodical name for a <see cref="SourceType.JournalArticle"/> or
    /// <see cref="SourceType.ArticleInPeriodical"/>; null otherwise / when unknown.
    /// </summary>
    public string? Journal { get; init; }

    /// <summary>
    /// The volume number for a <see cref="SourceType.JournalArticle"/> or
    /// <see cref="SourceType.ArticleInPeriodical"/>; null when unknown.
    /// </summary>
    public string? Volume { get; init; }

    /// <summary>
    /// The issue number for a <see cref="SourceType.JournalArticle"/> or
    /// <see cref="SourceType.ArticleInPeriodical"/>; null when unknown.
    /// </summary>
    public string? Issue { get; init; }

    /// <summary>
    /// The page (range) for a <see cref="SourceType.JournalArticle"/>, <see cref="SourceType.BookSection"/>,
    /// <see cref="SourceType.ConferenceProceedings"/>, or <see cref="SourceType.ArticleInPeriodical"/>,
    /// e.g. <c>"12-20"</c>; null when unknown.
    /// </summary>
    public string? Pages { get; init; }

    /// <summary>
    /// The URL for a <see cref="SourceType.WebSite"/> or <see cref="SourceType.ElectronicSource"/>;
    /// null otherwise / when unknown.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// The accessed date for a <see cref="SourceType.WebSite"/> or <see cref="SourceType.ElectronicSource"/>,
    /// free-text (e.g. <c>"3 May 2024"</c>);
    /// retained as a compatibility/display fallback when structured accessed-date parts are absent.
    /// </summary>
    public string? Accessed { get; init; }

    /// <summary>The Word bibliography <c>b:DayAccessed</c> value for a web site; null when unknown.</summary>
    public string? AccessedDay { get; init; }

    /// <summary>The Word bibliography <c>b:MonthAccessed</c> value for a web site; null when unknown.</summary>
    public string? AccessedMonth { get; init; }

    /// <summary>The Word bibliography <c>b:YearAccessed</c> value for a web site; null when unknown.</summary>
    public string? AccessedYear { get; init; }
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
    NumPages,
    /// <summary>dc:title document property (Insert &gt; Quick Parts &gt; Document Property &gt; Title).</summary>
    Title,
    /// <summary>dc:subject document property (Insert &gt; Quick Parts &gt; Document Property &gt; Subject).</summary>
    Subject,
    /// <summary>cp:keywords document property (Insert &gt; Quick Parts &gt; Document Property &gt; Keywords).</summary>
    Keywords,
    /// <summary>dc:description/comments document property (Insert &gt; Quick Parts &gt; Document Property &gt; Comments).</summary>
    DocComments
}

/// <summary>
/// A generic Word <em>complex</em> field — the <c>w:fldChar</c> begin / <c>w:instrText</c> / separate /
/// result / end run sequence (Insert &gt; Quick Parts &gt; Field). Unlike <see cref="RunFieldKind"/>, which
/// enumerates a fixed set of self-contained <c>w:fldSimple</c> fields, this preserves the raw field-code
/// <see cref="Instruction"/> verbatim, so any field (PAGE, NUMPAGES, DATE with a \@ picture, FILENAME,
/// AUTHOR, REF, or one FreeW does not specifically model) round-trips losslessly. The owning
/// <see cref="Run.Text"/> holds the cached result. <see cref="ShowCode"/> drives the Alt+F9 toggle: when
/// true the editor shows the field code (e.g. <c>{ PAGE }</c>) instead of the result; it is presentation
/// state only and is not serialised.
/// </summary>
/// <param name="Instruction">The raw field instruction, e.g. <c> PAGE </c> or <c> DATE \@ "M/d/yyyy" </c>.</param>
/// <param name="ShowCode">When true, the editor displays the field code rather than the result (Alt+F9).</param>
public sealed record ComplexField(string Instruction, bool ShowCode = false)
{
    /// <summary>The leading keyword of <see cref="Instruction"/> upper-cased (e.g. "PAGE"), or "" if empty.</summary>
    public string Keyword
    {
        get
        {
            var t = Instruction.Trim();
            if (t.Length == 0)
                return string.Empty;
            var end = t.IndexOfAny([' ', '\t', '\\']);
            return (end < 0 ? t : t[..end]).ToUpperInvariant();
        }
    }
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
/// A tracked formatting change on a run (Word's <c>w:rPrChange</c>). <see cref="PreviousFormatting"/> is
/// the run's formatting <em>before</em> the change (what reject restores); the run's current
/// <see cref="Run.Formatting"/> is the new formatting. <see cref="Author"/>/<see cref="DateXml"/> record
/// who made the change and when (the w:author/w:date on w:rPrChange). Immutable, mirroring how revision
/// metadata is carried as plain optional data on the run.
/// </summary>
public sealed record FormatRevision(RunFormatting PreviousFormatting, string? Author, string? DateXml);

/// <summary>
/// A tracked paragraph-formatting change on a paragraph (Word's <c>w:pPrChange</c>).
/// <see cref="PreviousParagraphFormatting"/> is the paragraph's formatting <em>before</em> the change
/// (what reject restores); the paragraph's current <see cref="Paragraph.Formatting"/> is the new
/// formatting. <see cref="Author"/>/<see cref="DateXml"/> record who made the change and when
/// (the w:author/w:date on w:pPrChange). Mirrors <see cref="FormatRevision"/> for runs.
/// </summary>
public sealed record ParagraphFormatRevision(ParagraphFormatting PreviousParagraphFormatting, string? Author, string? DateXml);

/// <summary>
/// The body-level content control (structured document tag, w:sdt) role carried by one or more
/// consecutive document blocks.
/// </summary>
public enum BlockContentControlKind
{
    RichText,
    PlainText,
    DocumentPart,
    Bibliography,
    RepeatingSection,
    RepeatingSectionItem,
    BuildingBlockGallery,
    Group,
    Citation
}

/// <summary>
/// A body-level content-control mark carried by a <see cref="Block"/>. Consecutive body blocks sharing the
/// same instance serialize as one outer w:sdt/w:sdtContent wrapper while the blocks themselves remain
/// ordinary paragraphs/tables in the model. This keeps run-level <see cref="ContentControl"/> behavior
/// unchanged and gives Word bibliography regions a place to retain their docPartObj/gallery metadata.
/// </summary>
public sealed record BlockContentControl(
    BlockContentControlKind Kind,
    string? Tag = null,
    string? Alias = null,
    string? DocPartGallery = null,
    string? DocPartCategory = null,
    bool DocPartUnique = false,
    ContentControlLockMode LockMode = ContentControlLockMode.NotSpecified,
    ContentControlWordMetadata? WordMetadata = null,
    string? RepeatingSectionTitle = null,
    bool DoNotAllowInsertDeleteSection = false,
    BlockContentControl? Parent = null)
{
    public const string BibliographyTag = "Bibliography";
    public const string BibliographyAlias = "Bibliography";
    public const string BibliographyGallery = "Bibliographies";

    public static BlockContentControl BibliographyRegion() =>
        new(
            BlockContentControlKind.Bibliography,
            Tag: BibliographyTag,
            Alias: BibliographyAlias,
            DocPartGallery: BibliographyGallery,
            DocPartUnique: true);

    /// <summary>Creates a body-level document-part list content control (w:docPartList).</summary>
    public static BlockContentControl DocumentPartListRegion(
        string gallery,
        string? category = null,
        bool unique = false,
        string? tag = null,
        string? alias = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gallery);
        return new BlockContentControl(
            BlockContentControlKind.DocumentPart,
            Tag: tag,
            Alias: alias,
            DocPartGallery: gallery,
            DocPartCategory: category,
            DocPartUnique: unique);
    }

    /// <summary>Creates a body-level building-block gallery content control (w:docPartObj).</summary>
    public static BlockContentControl BuildingBlockGalleryRegion(
        string gallery,
        string? category = null,
        bool unique = false,
        string? tag = null,
        string? alias = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gallery);
        return new BlockContentControl(
            BlockContentControlKind.BuildingBlockGallery,
            Tag: tag,
            Alias: alias,
            DocPartGallery: gallery,
            DocPartCategory: category,
            DocPartUnique: unique);
    }

    /// <summary>Creates a body-level Group content control (w:sdtPr/w:group).</summary>
    public static BlockContentControl GroupRegion(string? tag = null, string? alias = null) =>
        new(BlockContentControlKind.Group, Tag: tag, Alias: alias);

    /// <summary>Creates a body-level Citation content control (w:sdtPr/w:citation).</summary>
    public static BlockContentControl CitationRegion(string? tag = null, string? alias = null) =>
        new(BlockContentControlKind.Citation, Tag: tag, Alias: alias);

    /// <summary>Creates a Word 2013 repeating-section content control (w15:repeatingSection).</summary>
    public static BlockContentControl RepeatingSection(
        string? title = null,
        bool doNotAllowInsertDeleteSection = false,
        string? tag = null,
        string? alias = null) =>
        new(
            BlockContentControlKind.RepeatingSection,
            Tag: tag,
            Alias: alias,
            RepeatingSectionTitle: title,
            DoNotAllowInsertDeleteSection: doNotAllowInsertDeleteSection);

    /// <summary>Creates one item nested inside a Word 2013 repeating-section content control.</summary>
    public static BlockContentControl RepeatingSectionItem(
        BlockContentControl parent,
        string? tag = null,
        string? alias = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.Kind != BlockContentControlKind.RepeatingSection)
            throw new ArgumentException("A repeating-section item must have a repeating-section parent.", nameof(parent));

        return new BlockContentControl(
            BlockContentControlKind.RepeatingSectionItem,
            Tag: tag,
            Alias: alias,
            Parent: parent);
    }
}

/// <summary>
/// Metadata for a body-level w:customXml wrapper. Consecutive blocks sharing the same instance
/// serialize back into one wrapper while inline custom XML continues to flatten into ordinary runs.
/// </summary>
public sealed record BlockCustomXml(
    string? Element,
    string? Uri,
    string? PropertiesXml = null);

/// <summary>
/// A top-level document block. The document body is an ordered sequence of blocks; today that is
/// paragraphs and tables, mirroring how WordprocessingML interleaves w:p and w:tbl inside w:body.
/// </summary>
public abstract class Block
{
    /// <summary>
    /// Optional body-level content-control region metadata. The DOCX writer groups consecutive blocks
    /// sharing the same instance into one outer w:sdt; run-level controls still live on <see cref="Run"/>.
    /// </summary>
    public BlockContentControl? BlockContentControl { get; set; }

    /// <summary>
    /// Optional body-level custom XML wrapper metadata. Blocks imported from one wrapper share the
    /// same instance so the DOCX writer can restore the original grouping.
    /// </summary>
    public BlockCustomXml? BlockCustomXml { get; set; }
}

/// <summary>Whether a preserved Word bookmark boundary opens or closes its paired range.</summary>
public enum BookmarkBoundaryKind
{
    Start,
    End
}

/// <summary>
/// One invisible Word bookmark boundary positioned immediately before <see cref="RunIndex"/> in a
/// paragraph. <see cref="PairKey"/> pairs starts and ends across paragraphs; it is an internal identity,
/// not a serialized relationship id. Start boundaries retain Word's optional table-column and custom-XML
/// displacement attributes. <see cref="OwnerControl"/> retains a surrounding run-level content control.
/// Boundaries at <c>Runs.Count</c> are paragraph-end markers.
/// </summary>
public sealed record BookmarkBoundary(
    string PairKey,
    BookmarkBoundaryKind Kind,
    int RunIndex,
    string? Name = null,
    int? ColumnFirst = null,
    int? ColumnLast = null,
    string? DisplacedByCustomXml = null,
    ContentControl? OwnerControl = null);

/// <summary>
/// A body-level <c>w:altChunk</c> import that FreeW preserves without attempting to interpret its source
/// payload. Word resolves the referenced HTML, RTF, or nested Word package when it opens the document.
/// </summary>
public sealed class AltChunkBlock : Block
{
    /// <summary>The absolute OPC part name of the preserved altChunk payload.</summary>
    public string PreservedPartName { get; }

    public AltChunkBlock(string preservedPartName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preservedPartName);
        PreservedPartName = preservedPartName.StartsWith('/') ? preservedPartName : "/" + preservedPartName;
    }
}

/// <summary>A paragraph: an ordered sequence of runs plus paragraph formatting and an optional style.</summary>
public sealed class Paragraph : Block
{
    public List<Run> Runs { get; } = [];
    public ParagraphFormatting Formatting { get; set; } = ParagraphFormatting.Default;
    public string? StyleId { get; set; }

    /// <summary>
    /// Optional renderer-neutral drop-cap intent for the paragraph's leading glyph. The leading run is
    /// still represented normally in <see cref="Runs"/>; this metadata carries placement and wrapping.
    /// </summary>
    public DropCapLayoutIntent? DropCap { get; set; }

    /// <summary>
    /// Optional bookmark name marking this paragraph as a navigation target. When non-null the
    /// paragraph is bracketed by w:bookmarkStart/w:bookmarkEnd on save, and runs elsewhere can point
    /// to it via <see cref="Run.HyperlinkAnchor"/>. Bookmarks are invisible markers (no glyphs).
    /// <para>
    /// This is the primary (first) bookmark name for backward compatibility. A paragraph can carry
    /// multiple bookmarks via <see cref="BookmarkNames"/>; setting this property is equivalent to
    /// setting the first element of that list. If only one bookmark is needed, set this property
    /// directly; for multi-bookmark paragraphs, use <see cref="BookmarkNames"/> directly.
    /// </para>
    /// </summary>
    public string? BookmarkName
    {
        get => BookmarkNames.Count > 0 ? BookmarkNames[0] : null;
        set
        {
            var previousName = BookmarkNames.Count > 0 ? BookmarkNames[0] : null;
            if (string.IsNullOrEmpty(value))
            {
                // null / empty: clear ALL bookmarks on this paragraph (matches the pre-existing contract where
                // setting BookmarkName = null removed the paragraph's single bookmark; callers that do
                // paragraph.BookmarkName = null to remove bookmarks (e.g. RemoveBookmarks) still work).
                BookmarkNames.Clear();
            }
            else if (BookmarkNames.Count > 0)
                BookmarkNames[0] = value;
            else
                BookmarkNames.Add(value);

            if (previousName is not { Length: > 0 }
                || value is not { Length: > 0 }
                || string.Equals(previousName, value, StringComparison.Ordinal))
            {
                return;
            }

            for (var index = 0; index < BookmarkBoundaries.Count; index++)
            {
                var boundary = BookmarkBoundaries[index];
                if (boundary.Kind == BookmarkBoundaryKind.Start
                    && string.Equals(boundary.Name, previousName, StringComparison.Ordinal))
                {
                    BookmarkBoundaries[index] = boundary with { Name = value };
                }
            }
        }
    }

    /// <summary>
    /// All bookmarks attached to this paragraph, in document order. A paragraph may carry multiple
    /// <c>w:bookmarkStart</c>/<c>w:bookmarkEnd</c> pairs (e.g. a heading that is both a TOC target and
    /// a named user bookmark). On save, one <c>w:bookmarkStart</c>/<c>w:bookmarkEnd</c> pair is emitted
    /// per name with a unique, consistent id. <see cref="BookmarkName"/> returns the first entry (or
    /// null when empty) for backward compatibility.
    /// </summary>
    public List<string> BookmarkNames { get; } = [];

    /// <summary>
    /// Imported bookmark starts and ends in source order. Newly authored names that have no boundary
    /// metadata retain the historical whole-paragraph serialization. Public names removed from
    /// <see cref="BookmarkNames"/> are not resurrected from this retained package metadata.
    /// </summary>
    public List<BookmarkBoundary> BookmarkBoundaries { get; } = [];

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

    /// <summary>
    /// A tracked paragraph-<em>formatting</em> change on this paragraph (Word's w:pPrChange), or null
    /// when the paragraph's formatting was not changed under Track Changes. When set,
    /// <see cref="Formatting"/> is the new (current) paragraph formatting and
    /// <see cref="ParagraphFormatRevision"/> carries the <em>previous</em> formatting plus the
    /// author/date who made the change. Accepting keeps the new formatting and clears the mark;
    /// rejecting restores the previous paragraph formatting. Mirrors <see cref="Run.FormatRevision"/>
    /// at the paragraph level.
    /// </summary>
    public ParagraphFormatRevision? ParagraphFormatRevision { get; set; }

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

    /// <summary>
    /// Vertical alignment of the cell's content (<c>tc/tcPr/w:vAlign</c>): top, center or bottom.
    /// <see cref="TableCellVerticalAlignment.Top"/> is the docx default, so it is not emitted and existing
    /// cells round-trip unchanged. Set by the Table Properties dialog's Cell tab.
    /// </summary>
    public TableCellVerticalAlignment VerticalAlignment { get; set; } = TableCellVerticalAlignment.Top;

    /// <summary>
    /// Per-cell margin override (<c>tc/tcPr/w:tcMar</c>), or null to inherit the table default
    /// (<see cref="Table.DefaultCellMargins"/>). Null is the default so existing cells are unaffected.
    /// </summary>
    public TableCellMargins? Margins { get; set; }

    /// <summary>
    /// Per-edge cell borders (<c>tc/tcPr/w:tcBorders</c>), or null to inherit the table-level borders.
    /// When set each non-null edge overrides the corresponding table/style border for this cell.
    /// Null is the default so existing cells are unaffected.
    /// </summary>
    public CellBorders? Borders { get; set; }

    /// <summary>
    /// Text direction of the cell content (<c>tc/tcPr/w:textDirection/@w:val</c>).
    /// <see cref="CellTextDirection.Horizontal"/> is the docx default (no element emitted) so existing
    /// cells round-trip unchanged. Maps to the shape <see cref="ShapeTextDirection"/> pattern.
    /// </summary>
    public CellTextDirection TextDirection { get; set; } = CellTextDirection.Horizontal;

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

/// <summary>
/// Vertical alignment of a table cell's content (<c>tc/tcPr/w:vAlign</c>). <see cref="Top"/> is the docx
/// default (no element emitted); <see cref="Center"/> and <see cref="Bottom"/> map to the "center"/"bottom"
/// tokens.
/// </summary>
public enum TableCellVerticalAlignment
{
    Top,
    Center,
    Bottom
}

/// <summary>
/// Per-edge cell borders (<c>tc/tcPr/w:tcBorders</c>). Each edge is a nullable <see cref="CellBorderEdge"/>
/// so that only explicitly set edges are emitted. All-null means inherit table-level borders.
/// Immutable record so it can be compared and copied cleanly.
/// </summary>
public sealed record CellBorders
{
    /// <summary>Top edge border, or null to inherit.</summary>
    public CellBorderEdge? Top { get; init; }
    /// <summary>Bottom edge border, or null to inherit.</summary>
    public CellBorderEdge? Bottom { get; init; }
    /// <summary>Left edge border, or null to inherit.</summary>
    public CellBorderEdge? Left { get; init; }
    /// <summary>Right edge border, or null to inherit.</summary>
    public CellBorderEdge? Right { get; init; }

    /// <summary>True when all four edges are null (nothing to emit).</summary>
    public bool IsEmpty => Top is null && Bottom is null && Left is null && Right is null;
}

/// <summary>
/// A single edge of a <see cref="CellBorders"/> — style, colour and width, mirroring <see cref="ParagraphBorder"/>
/// so the same <see cref="BorderLineStyle"/> enum and <see cref="BorderLineStyles"/> token mapping are reused.
/// </summary>
public sealed record CellBorderEdge(
    BorderLineStyle Style = BorderLineStyle.Single,
    string ColorHex = "#000000",
    double WidthPt = 0.5);

/// <summary>
/// Table-level border definition (<c>w:tblBorders</c>). Unlike cell borders, tables also carry
/// independent inside-horizontal and inside-vertical edges. A non-null instance represents an explicit
/// <c>w:tblBorders</c> element; null edges retain the distinction between an absent edge and a generated
/// default. Color tokens preserve Word's <c>auto</c> value as well as explicit RGB values.
/// </summary>
public sealed record TableBorders
{
    public TableBorderEdge? Top { get; init; }
    public TableBorderEdge? Left { get; init; }
    public TableBorderEdge? Bottom { get; init; }
    public TableBorderEdge? Right { get; init; }
    public TableBorderEdge? InsideHorizontal { get; init; }
    public TableBorderEdge? InsideVertical { get; init; }

    public bool IsEmpty => Top is null && Left is null && Bottom is null && Right is null
        && InsideHorizontal is null && InsideVertical is null;
}

public sealed record TableBorderEdge(
    BorderLineStyle Style = BorderLineStyle.Single,
    string ColorToken = "auto",
    double WidthPt = 0.5);

/// <summary>
/// Text direction of a table cell's content (<c>tc/tcPr/w:textDirection/@w:val</c>).
/// Mirrors <see cref="ShapeTextDirection"/> so the same rendering pattern (LayoutTransform) is reused.
/// <see cref="Horizontal"/> is the docx default (no element emitted); existing cells are unaffected.
/// </summary>
public enum CellTextDirection
{
    /// <summary>Left-to-right, top-to-bottom — the standard docx default (<c>lrTb</c>, not emitted).</summary>
    Horizontal,
    /// <summary>Bottom-to-top, then left-to-right (<c>btLr</c> → Word rotates 90° CCW = Rotate90 up).</summary>
    Rotate90,
    /// <summary>Top-to-bottom, then right-to-left (<c>tbRl</c> → Word rotates 90° CW = Rotate270 down).</summary>
    Rotate270
}

/// <summary>
/// How a table row's height is interpreted (<c>tr/trPr/w:trHeight/@w:hRule</c>). <see cref="Auto"/> is the
/// docx default — the row grows to fit its content and no explicit height is emitted. <see cref="AtLeast"/>
/// is a minimum height the row may exceed; <see cref="Exact"/> fixes the height (content may be clipped).
/// </summary>
public enum TableRowHeightRule
{
    Auto,
    AtLeast,
    Exact
}

/// <summary>
/// The four inside margins (cell padding) of a table cell, in points, mapping onto a <c>w:tcMar</c>
/// (per-cell override) or <c>w:tblCellMar</c> (table default) element. Immutable so it round-trips cleanly.
/// Word's default cell margins are 0 top/bottom and ~5.4pt (108 dxa) left/right.
/// </summary>
public sealed record TableCellMargins(
    double TopPt = 0,
    double LeftPt = 5.4,
    double BottomPt = 0,
    double RightPt = 5.4)
{
    /// <summary>Word's default table cell margins (0 top/bottom, 5.4pt left/right).</summary>
    public static readonly TableCellMargins Default = new();
}

/// <summary>A table row: an ordered sequence of cells (w:tr).</summary>
public sealed class TableRow
{
    public List<TableCell> Cells { get; } = [];

    /// <summary>
    /// Explicit row height in points (<c>tr/trPr/w:trHeight/@w:val</c>), interpreted per <see cref="HeightRule"/>.
    /// Null (the default) means automatic height, so no <c>w:trHeight</c> is emitted and existing rows are
    /// unaffected.
    /// </summary>
    public double? HeightPt { get; set; }

    /// <summary>
    /// How <see cref="HeightPt"/> is interpreted (<c>@w:hRule</c>). <see cref="TableRowHeightRule.Auto"/> is
    /// the default; it is irrelevant unless <see cref="HeightPt"/> is set.
    /// </summary>
    public TableRowHeightRule HeightRule { get; set; } = TableRowHeightRule.Auto;

    /// <summary>
    /// Whether the row's contents may break across a page boundary (Word's "Allow row to break across pages").
    /// True (the default) lets the row split; false emits <c>tr/trPr/w:cantSplit</c> to keep the row whole.
    /// </summary>
    public bool AllowBreakAcrossPages { get; set; } = true;
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

    /// <summary>When true, the last row is styled distinctly (Word's "Last Row" toggle). Round-trips via <c>w:tblPr/w:tblLook w:lastRow="1"</c>. Default false.</summary>
    public bool LastRow { get; init; }
    /// <summary>When true, the first column is styled distinctly (bold). Round-trips via <c>w:tblPr/w:tblLook w:firstColumn="1"</c>. Default false.</summary>
    public bool FirstColumn { get; init; }
    /// <summary>When true, the last column is styled distinctly. Round-trips via <c>w:tblPr/w:tblLook w:lastColumn="1"</c>. Default false.</summary>
    public bool LastColumn { get; init; }
    /// <summary>When true, alternate columns are shaded (banded columns). Round-trips via <c>w:tblPr/w:tblLook w:noVBand="0"</c>. Default false.</summary>
    public bool BandedColumns { get; init; }

    public static readonly TableFormatting Default = new();
}

/// <summary>
/// Horizontal alignment of a table within its column / page width (<c>tbl/tblPr/w:jc</c>). <see cref="Left"/>
/// is the docx default (no element emitted); <see cref="Center"/> and <see cref="Right"/> map to the
/// "center"/"right" tokens. Set by the Table Properties dialog's Table tab.
/// </summary>
public enum TableAlignment
{
    Left,
    Center,
    Right
}

/// <summary>
/// Controls how a table fits its content and container (<c>w:tbl/w:tblPr/w:tblLayout</c>).
/// <see cref="Fixed"/> keeps column widths fixed; <see cref="Contents"/> shrinks/grows columns to
/// their content; <see cref="Window"/> stretches the table to the container width. <see cref="Fixed"/>
/// is the default and is emitted as <c>w:type="fixed"</c> so Word preserves authored grid widths.
/// </summary>
public enum AutoFitMode
{
    Fixed,
    Contents,
    Window
}

/// <summary>A table block: rows of cells, each cell holding paragraphs (w:tbl / w:tr / w:tc).</summary>
public sealed class Table : Block
{
    public List<TableRow> Rows { get; } = [];
    public TableFormatting Formatting { get; set; } = TableFormatting.Default;

    /// <summary>
    /// The OOXML table-style id (e.g. <c>"TableGrid"</c>, <c>"GridTable1Light"</c>) referencing a
    /// <see cref="DocumentTableStyle"/> from the built-in catalog. When set, the table's visual appearance
    /// (borders, header fill, banded-row shading, emphasis) is driven by the catalog entry; the style id is
    /// round-tripped via <c>w:tblPr/w:tblStyle w:val</c> in the docx. Null means no named style (the table
    /// uses its explicit <see cref="Formatting"/> flags directly, which is the historical behaviour).
    /// </summary>
    public string? TableStyleId { get; set; }

    /// <summary>Explicit table-level border payload from <c>w:tblBorders</c>, or null to inherit a style.</summary>
    public TableBorders? Borders { get; set; }

    /// <summary>
    /// Per-column widths in points, one entry per column, matching the docx table grid
    /// (<c>w:tbl/w:tblGrid/w:gridCol</c>). Empty when no explicit grid is known (the default), so
    /// existing tables are unaffected.
    /// </summary>
    public List<double> ColumnWidthsPt { get; } = [];

    /// <summary>
    /// Preferred total table width in points (<c>tbl/tblPr/w:tblW</c> with <c>type="dxa"</c>), or null for
    /// automatic width (<c>type="auto"</c>, the historical default), so existing tables are unaffected. Set
    /// by the Table Properties dialog's Table tab.
    /// </summary>
    public double? PreferredWidthPt { get; set; }

    /// <summary>
    /// Horizontal alignment of the table (<c>tbl/tblPr/w:jc</c>). <see cref="TableAlignment.Left"/> is the
    /// default, so it is not emitted and existing tables are unaffected.
    /// </summary>
    public TableAlignment Alignment { get; set; } = TableAlignment.Left;

    /// <summary>
    /// Indent of the table from the left margin in points (<c>tbl/tblPr/w:tblInd</c>), or null for none
    /// (the default). Word's "Indent from left" on the Table tab.
    /// </summary>
    public double? IndentFromLeftPt { get; set; }

    /// <summary>
    /// True when the table floats with text wrapping around it (Word's "Text wrapping: Around"). Emits a
    /// minimal <c>tbl/tblPr/w:tblpPr</c> floating-position element so Word treats the table as floating.
    /// False (the default) keeps the table inline, so existing tables are unaffected.
    /// </summary>
    public bool TextWrapping { get; set; }

    /// <summary>
    /// Default inside margins (cell padding) applied to every cell that has no <see cref="TableCell.Margins"/>
    /// override (<c>tbl/tblPr/w:tblCellMar</c>), or null to use the implicit docx default, so existing tables
    /// are unaffected. Set by the Table tab's "Options… &gt; Default cell margins".
    /// </summary>
    public TableCellMargins? DefaultCellMargins { get; set; }

    /// <summary>
    /// Spacing between cells in points (<c>tbl/tblPr/w:tblCellSpacing</c>), or null for none (the default).
    /// Word's "Allow spacing between cells" on the Table Options dialog.
    /// </summary>
    public double? CellSpacingPt { get; set; }

    /// <summary>
    /// How the table auto-fits to its content or container (<c>tbl/tblPr/w:tblLayout</c>).
    /// <see cref="AutoFitMode.Fixed"/> is the default and emits <c>w:type="fixed"</c>. <see cref="AutoFitMode.Contents"/>
    /// maps to <c>w:type="autofit"</c>; <see cref="AutoFitMode.Window"/> maps to <c>w:type="autofit"</c> with
    /// the table's preferred width set to 100% of the page. Set by Table Layout > Cell Size > AutoFit.
    /// </summary>
    public AutoFitMode AutoFit { get; set; } = AutoFitMode.Fixed;

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
/// How the document restricts editing (document protection, w:settings/w:documentProtection).
/// <see cref="None"/> is an unprotected document (the default — no settings part is emitted);
/// <see cref="ReadOnly"/> locks the whole document against edits; <see cref="CommentsOnly"/> permits
/// only the insertion of comments; <see cref="TrackChangesOnly"/> permits edits but forces them to be
/// tracked revisions; <see cref="FillingForms"/> permits only filling in form fields. Maps onto
/// w:documentProtection/@w:edit ("readOnly"/"comments"/"trackedChanges"/"forms").
/// </summary>
public enum ProtectionMode
{
    None,
    ReadOnly,
    CommentsOnly,
    TrackChangesOnly,
    FillingForms
}

/// <summary>
/// Document protection (restrict-editing) settings, mapping onto word/settings.xml's
/// w:documentProtection. Immutable so it round-trips cleanly and can be shared; the default
/// (<see cref="ProtectionMode.None"/>, see <see cref="Unprotected"/>) leaves existing documents
/// unaffected — no settings part is emitted and the reader maps a missing/absent protection to None.
/// When <see cref="Mode"/> is not None the writer emits w:documentProtection with w:enforcement="1".
/// When a password is set the writer additionally emits the OOXML legacy password hash attributes
/// (w:cryptProviderType="rsaAES", w:cryptAlgorithmClass="hash", w:cryptAlgorithmType="typeAny",
/// w:cryptAlgorithmSid="4" [SHA-1], w:cryptSpinCount, w:hash, w:salt) so the protection is
/// password-enforced in Microsoft Word and round-trips as-is.
/// </summary>
public sealed record ProtectionSettings(ProtectionMode Mode = ProtectionMode.None)
{
    /// <summary>The default, unprotected settings (<see cref="ProtectionMode.None"/>).</summary>
    public static readonly ProtectionSettings Unprotected = new(ProtectionMode.None);

    /// <summary>True when the document is protected in some mode (i.e. not <see cref="ProtectionMode.None"/>).</summary>
    public bool IsProtected => Mode != ProtectionMode.None;

    /// <summary>
    /// Base64-encoded SHA-1 password hash, or null when no password was set. Computed by the
    /// OOXML legacy algorithm (ECMA-376 §14.7.2): SHA1(salt + password-UTF16LE) iterated
    /// <see cref="SpinCount"/> times then base64-encoded. Emitted as w:documentProtection/@w:hash.
    /// </summary>
    public string? PasswordHash { get; init; }

    /// <summary>
    /// Base64-encoded 16-byte random salt used in the password hash, or null when no password was set.
    /// Emitted as w:documentProtection/@w:salt.
    /// </summary>
    public string? PasswordSalt { get; init; }

    /// <summary>
    /// Spin (iteration) count for the OOXML legacy hash algorithm. Word uses 50000. Defaults to 50000
    /// when a hash is present. Emitted as w:documentProtection/@w:cryptSpinCount.
    /// </summary>
    public int SpinCount { get; init; } = 50000;

    /// <summary>True when a password hash is stored (protection is password-enforced).</summary>
    public bool HasPassword => PasswordHash is not null && PasswordSalt is not null;
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
/// Layout orientation for a watermark: diagonal (45°) or horizontal.
/// </summary>
public enum WatermarkLayout
{
    Diagonal,
    Horizontal
}

/// <summary>
/// Options for a page watermark — the full set of choices exposed by Word's "Custom Watermark" dialog
/// (Design &gt; Page Background &gt; Watermark &gt; Custom Watermark). Null on
/// <see cref="PageSettings.WatermarkOptions"/> means no watermark. Persisted as custom document
/// properties (docProps/custom.xml) so all fields round-trip losslessly across a save/load cycle.
/// When <see cref="PageSettings.Watermark"/> carries a legacy plain-text value and
/// <see cref="PageSettings.WatermarkOptions"/> is null, the render path migrates the legacy value to a
/// default <see cref="WatermarkOptions"/> on-the-fly so the visual is identical.
/// </summary>
public sealed record WatermarkOptions(string Text)
{
    /// <summary>Font family for the watermark text. Defaults to "Calibri" (Word's own default).</summary>
    public string FontFamily { get; init; } = "Calibri";

    /// <summary>
    /// Watermark text colour as an "#RRGGBB" hex string. Defaults to "#808080" (medium grey, the same
    /// implicit colour the legacy rendering used).
    /// </summary>
    public string FontColorHex { get; init; } = "#808080";

    /// <summary>
    /// Layout orientation: <see cref="WatermarkLayout.Diagonal"/> (45° — Word's default) or
    /// <see cref="WatermarkLayout.Horizontal"/>.
    /// </summary>
    public WatermarkLayout Layout { get; init; } = WatermarkLayout.Diagonal;

    /// <summary>
    /// Opacity fraction in [0, 1]. Defaults to 0.3 (semitransparent), matching Word's "Semitransparent"
    /// checkbox (on by default). Set to 1.0 for an opaque watermark (checkbox off).
    /// </summary>
    public double Opacity { get; init; } = 0.3;

    // ── Picture watermark ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When non-null the watermark is a picture (image) rather than text. This field carries the raw
    /// image bytes (PNG, JPEG, etc.). Persisted as a custom property (<c>FreeWWatermarkImage</c>, base-64)
    /// in docProps/custom.xml so the image survives save/load cycles. When set, the text watermark fields
    /// (<see cref="Text"/>, <see cref="FontFamily"/>, <see cref="FontColorHex"/>) are ignored for
    /// rendering; <see cref="Layout"/>, <see cref="Opacity"/>, and <see cref="ScalePct"/> apply.
    /// </summary>
    public byte[]? ImageBytes { get; init; }

    /// <summary>
    /// Scale of the picture watermark as a percentage of the page size (0 = Auto, 1–500). Defaults to 0
    /// (Auto) so the image is centred at its natural size. Mirrors Word's "Picture watermark / Scale"
    /// field. Only meaningful when <see cref="ImageBytes"/> is non-null.
    /// </summary>
    public int ScalePct { get; init; }

    /// <summary>
    /// Optional width recovered from Word's native VML picture-watermark shape. Together with
    /// <see cref="NativeVmlPictureHeightPt"/>, this preserves the authored VML footprint instead
    /// of falling back to FreeW's automatic picture sizing.
    /// </summary>
    public double? NativeVmlPictureWidthPt { get; init; }

    /// <summary>
    /// Optional height recovered from Word's native VML picture-watermark shape. Meaningful only
    /// when <see cref="NativeVmlPictureWidthPt"/> is also present.
    /// </summary>
    public double? NativeVmlPictureHeightPt { get; init; }

    /// <summary>
    /// The optional <c>v:fill/@recolor</c> token recovered from a native VML picture watermark.
    /// Word's recolored VML picture paint is distinct from FreeW's editable raw image payload.
    /// </summary>
    public bool? NativeVmlPictureRecolor { get; init; }

    /// <summary>
    /// Optional width recovered from Word's native VML text-watermark shape. Together with
    /// <see cref="NativeVmlTextHeightPt"/>, this keeps an imported text-path footprint distinct
    /// from FreeW's canonical 468 by 117 point watermark shape.
    /// </summary>
    public double? NativeVmlTextWidthPt { get; init; }

    /// <summary>
    /// Optional height recovered from Word's native VML text-watermark shape. Meaningful only
    /// when <see cref="NativeVmlTextWidthPt"/> is also present.
    /// </summary>
    public double? NativeVmlTextHeightPt { get; init; }

    /// <summary>
    /// Optional <c>v:textpath/@fitshape</c> value recovered from a Word VML text watermark.
    /// A null value retains FreeW's canonical fitshape output for newly authored watermarks.
    /// </summary>
    public bool? NativeVmlTextFitShape { get; init; }

    /// <summary>
    /// Optional clockwise VML rotation recovered from a native text-watermark shape. This retains
    /// imported nonstandard angles instead of collapsing every nonzero rotation to Diagonal.
    /// </summary>
    public double? NativeVmlTextRotationDegrees { get; init; }

    /// <summary>
    /// Optional serialized <c>v:textpath</c> payload recovered from a native VML text watermark.
    /// FreeW updates its editable text and font fields on save while retaining unmodeled path
    /// controls such as <c>fitpath</c>, <c>trim</c>, and <c>xscale</c>.
    /// </summary>
    public string? NativeVmlTextPathXml { get; init; }

    /// <summary>
    /// Optional <c>v:textpath/@on</c> state recovered from a native text watermark. An explicit
    /// false value keeps the serialized watermark hidden instead of rendering it as active text.
    /// </summary>
    public bool? NativeVmlTextPathEnabled { get; init; }

    /// <summary>
    /// Optional serialized <c>v:shapetype</c> payload referenced by an imported native VML text
    /// watermark. Retaining the path, formulas, and text-path settings prevents a custom Word
    /// watermark from being rewritten as FreeW's canonical <c>_x0000_t136</c> prototype.
    /// </summary>
    public string? NativeVmlTextShapeTypeXml { get; init; }

    /// <summary>Whether this watermark is a picture watermark (<see cref="ImageBytes"/> is non-null).</summary>
    public bool IsPicture => ImageBytes is { Length: > 0 };

    /// <summary>
    /// Migrate a bare legacy watermark text string to a <see cref="WatermarkOptions"/> with sensible
    /// defaults — the same visual the legacy rendering produced.
    /// </summary>
    public static WatermarkOptions FromLegacyText(string text) => new(text);
}

/// <summary>
/// An immutable page border (w:sectPr/w:pgBorders). A uniform box drawn around the page with one
/// colour and width (points). Null on <see cref="PageSettings.PageBorder"/> means no page border, so
/// existing documents are unaffected. Mirrors how <see cref="ParagraphBorder"/> is modelled.
/// </summary>
public sealed record PageBorder(string ColorHex = "#000000", double WidthPt = 1.0)
{
    /// <summary>
    /// Controls whether <c>w:pgBorders/@w:offsetFrom</c> measures the frame from the page edge or the
    /// document text area. Page is Word's default and preserves legacy FreeW output.
    /// </summary>
    public PageBorderOffsetFrom OffsetFrom { get; init; } = PageBorderOffsetFrom.Page;

    /// <summary>
    /// The <c>w:space</c> distance in points between the selected reference edge and the page frame.
    /// Word's default is 24 points.
    /// </summary>
    public double SpacePt { get; init; } = 24.0;

    /// <summary>
    /// The line style of every page-border edge (w:val). Defaults to <see cref="BorderLineStyle.Single"/>,
    /// matching what the writer previously emitted, so existing documents round-trip byte-unchanged.
    /// When <see cref="ArtId"/> is non-zero this field is ignored because the mapped art token is written
    /// directly to <c>w:val</c>.
    /// </summary>
    public BorderLineStyle LineStyle { get; init; } = BorderLineStyle.Single;

    /// <summary>
    /// Optional Word <c>WdPageBorderArt</c> id. Zero means a plain line border. Curated non-zero ids map
    /// to the corresponding WordprocessingML <c>w:val</c> art token on every page-border edge.
    /// </summary>
    public int ArtId { get; init; }

    /// <summary>
    /// Pages in the section that display this border (<c>w:pgBorders/@w:display</c>). All pages is Word's
    /// default and is omitted from canonical output.
    /// </summary>
    public PageBorderDisplay Display { get; init; } = PageBorderDisplay.AllPages;

    /// <summary>
    /// Whether the border is composited in front of or behind document text
    /// (<c>w:pgBorders/@w:zOrder</c>). Front is Word's default and is omitted from canonical output.
    /// </summary>
    public PageBorderZOrder ZOrder { get; init; } = PageBorderZOrder.Front;
}

/// <summary>Reference edge used by <c>w:pgBorders/@w:offsetFrom</c>.</summary>
public enum PageBorderOffsetFrom
{
    Page,
    Text
}

public enum PageBorderDisplay
{
    AllPages,
    FirstPage,
    NotFirstPage
}

public enum PageBorderZOrder
{
    Front,
    Behind
}

/// <summary>
/// How (and whether) lines are numbered in the page margin (w:sectPr/w:lnNumType).
/// <see cref="None"/> emits no w:lnNumType (the default — existing documents are unaffected);
/// <see cref="Continuous"/> numbers lines continuously across pages (w:restart="continuous");
/// <see cref="RestartEachPage"/> restarts numbering at 1 on every page (w:restart="newPage");
/// <see cref="RestartEachSection"/> restarts numbering at each section boundary (w:restart="newSection").
/// </summary>
public enum LineNumberMode
{
    None,
    Continuous,
    RestartEachPage,
    RestartEachSection
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

/// <summary>
/// Number format for PAGE fields in a section. Maps to w:sectPr/w:pgNumType/@w:fmt.
/// </summary>
public enum PageNumberFormat
{
    /// <summary>Arabic numerals: 1, 2, 3 (w:fmt="decimal", the Word default).</summary>
    Decimal,
    /// <summary>Lower-case Roman numerals: i, ii, iii (w:fmt="lowerRoman").</summary>
    LowerRoman,
    /// <summary>Upper-case Roman numerals: I, II, III (w:fmt="upperRoman").</summary>
    UpperRoman,
    /// <summary>Lower-case letters: a, b, c (w:fmt="lowerLetter").</summary>
    LowerLetter,
    /// <summary>Upper-case letters: A, B, C (w:fmt="upperLetter").</summary>
    UpperLetter
}

/// <summary>
/// Separator used between a chapter prefix and a PAGE field value. Maps to
/// w:sectPr/w:pgNumType/@w:chapSep.
/// </summary>
public enum PageNumberChapterSeparator
{
    /// <summary>Hyphen separator: 1-1 (w:chapSep="hyphen").</summary>
    Hyphen,
    /// <summary>Period separator: 1.1 (w:chapSep="period").</summary>
    Period,
    /// <summary>Colon separator: 1:1 (w:chapSep="colon").</summary>
    Colon,
    /// <summary>Em dash separator: 1--1 (w:chapSep="emDash").</summary>
    EmDash,
    /// <summary>En dash separator: 1-1 (w:chapSep="enDash").</summary>
    EnDash
}

/// <summary>Page geometry for a section (points; US Letter with 1in margins by default).</summary>
public sealed class PageSettings
{
    public const double WordDefaultTabStopPt = 36;

    public double WidthPt { get; set; } = 612;
    public double HeightPt { get; set; } = 792;
    public double MarginLeftPt { get; set; } = 72;
    public double MarginRightPt { get; set; } = 72;
    public double MarginTopPt { get; set; } = 72;
    public double MarginBottomPt { get; set; } = 72;
    public bool Landscape { get; set; }

    /// <summary>
    /// The binding gutter — extra margin added on the binding edge (w:sectPr/w:pgMar/@w:gutter), in points.
    /// Defaults to 0 so existing documents round-trip unchanged — the @w:gutter attribute is emitted only when
    /// greater than 0. Word's Page Setup &gt; Margins dialog exposes this as "Gutter". Always non-negative.
    /// </summary>
    public double GutterPt { get; set; }

    /// <summary>
    /// The distance from the top of the page to the header (w:sectPr/w:pgMar/@w:header), in points. Defaults to
    /// 0, meaning "unspecified" — the @w:header attribute is then not emitted, so existing documents round-trip
    /// unchanged (Word's own default is 0.5"). When greater than 0 the writer emits @w:header and the reader maps
    /// it back here. Word's Page Setup &gt; Layout dialog exposes this as "Header from edge". Always non-negative.
    /// </summary>
    public double HeaderDistancePt { get; set; }

    /// <summary>
    /// The distance from the bottom of the page to the footer (w:sectPr/w:pgMar/@w:footer), in points. Defaults to
    /// 0, meaning "unspecified" — the @w:footer attribute is then not emitted, so existing documents round-trip
    /// unchanged (Word's own default is 0.5"). When greater than 0 the writer emits @w:footer and the reader maps
    /// it back here. Word's Page Setup &gt; Layout dialog exposes this as "Footer from edge". Always non-negative.
    /// </summary>
    public double FooterDistancePt { get; set; }

    /// <summary>
    /// Whether the document uses mirror margins for double-sided printing (the document-level
    /// w:settings/w:mirrorMargins toggle). When set, the left/right margins become inside/outside margins that
    /// swap on facing pages and the gutter is added to the inside edge. Defaults to false so existing documents
    /// are unaffected — no w:mirrorMargins is emitted and no settings part is forced. When true the writer emits
    /// the toggle in word/settings.xml and the reader maps it back. Like <see cref="DifferentOddEvenPages"/> this
    /// is a document-wide setting carried on the body-level page settings. Word's Page Setup &gt; Margins dialog
    /// exposes this via the "Multiple pages: Mirror margins" option.
    /// </summary>
    public bool MirrorMargins { get; set; }

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
    /// Legacy plain-text form: used as a fallback when <see cref="WatermarkOptions"/> is null and the
    /// loaded document carried only the old single-string custom property. New code should prefer
    /// <see cref="WatermarkOptions"/>; the render and IO paths migrate this value automatically.
    /// </summary>
    public string? Watermark { get; set; }

    /// <summary>
    /// Full watermark options (text, font, colour, layout, opacity). When non-null this takes precedence
    /// over the legacy <see cref="Watermark"/> string. Null when no watermark is set. Persisted as a
    /// set of custom document properties (docProps/custom.xml) so all fields round-trip losslessly.
    /// </summary>
    public WatermarkOptions? WatermarkOptions { get; set; }

    /// <summary>
    /// Returns the effective <see cref="WatermarkOptions"/> to render: <see cref="WatermarkOptions"/>
    /// when set, a migration of the legacy <see cref="Watermark"/> string when only that is set, or
    /// null when there is no watermark at all.
    /// </summary>
    public WatermarkOptions? EffectiveWatermark =>
        WatermarkOptions
        ?? (Watermark is { Length: > 0 } t ? FreeW.Core.Model.WatermarkOptions.FromLegacyText(t) : null);

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
    /// The starting number for the first line number shown (w:lnNumType/@w:start). Defaults to 1
    /// (Word's default). Only meaningful when <see cref="LineNumberMode"/> is not
    /// <see cref="LineNumberMode.None"/>. Always at least 1.
    /// </summary>
    public int LineNumberStartAt { get; set; } = 1;

    /// <summary>
    /// Number style used by PAGE fields in this section (w:sectPr/w:pgNumType/@w:fmt). Defaults to
    /// <see cref="PageNumberFormat.Decimal"/>, matching Word.
    /// </summary>
    public PageNumberFormat PageNumberFormat { get; set; } = PageNumberFormat.Decimal;

    /// <summary>
    /// Optional first PAGE value for this section (w:sectPr/w:pgNumType/@w:start). Null means continue
    /// numbering from the previous section; for the first section, continue starts at 1.
    /// </summary>
    public int? PageNumberStartAt { get; set; }

    /// <summary>
    /// Optional Heading level used as the chapter prefix for PAGE fields
    /// (w:sectPr/w:pgNumType/@w:chapStyle). Null means no chapter prefix.
    /// </summary>
    public int? PageNumberChapterStyleLevel { get; set; }

    /// <summary>
    /// Separator between the chapter prefix and page number
    /// (w:sectPr/w:pgNumType/@w:chapSep). Only meaningful when
    /// <see cref="PageNumberChapterStyleLevel"/> is set.
    /// </summary>
    public PageNumberChapterSeparator PageNumberChapterSeparator { get; set; } = PageNumberChapterSeparator.Hyphen;

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
    /// The default interval between implicit tab stops (word/settings.xml's w:defaultTabStop), in points.
    /// Defaults to Word's classic 0.5" spacing. Fresh documents omit the setting while this remains at the
    /// default; changed values emit w:defaultTabStop so Word and FreeW share the same baseline tab spacing.
    /// </summary>
    public double DefaultTabStopPt { get; set; } = WordDefaultTabStopPt;

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
        GutterPt = GutterPt,
        HeaderDistancePt = HeaderDistancePt,
        FooterDistancePt = FooterDistancePt,
        MirrorMargins = MirrorMargins,
        ColumnCount = ColumnCount,
        ColumnSpacingPt = ColumnSpacingPt,
        ColumnsLineBetween = ColumnsLineBetween,
        ColumnWidthsPt = ColumnWidthsPt is null ? null : new List<double>(ColumnWidthsPt),
        PageBorder = PageBorder,
        Watermark = Watermark,
        WatermarkOptions = CloneWatermarkOptions(WatermarkOptions),
        LineNumberMode = LineNumberMode,
        LineNumberCountBy = LineNumberCountBy,
        LineNumberStartAt = LineNumberStartAt,
        PageNumberFormat = PageNumberFormat,
        PageNumberStartAt = PageNumberStartAt,
        PageNumberChapterStyleLevel = PageNumberChapterStyleLevel,
        PageNumberChapterSeparator = PageNumberChapterSeparator,
        AutoHyphenation = AutoHyphenation,
        HyphenationZonePt = HyphenationZonePt,
        ConsecutiveHyphenLimit = ConsecutiveHyphenLimit,
        DoNotHyphenateCaps = DoNotHyphenateCaps,
        DefaultTabStopPt = DefaultTabStopPt,
        VerticalAlignment = VerticalAlignment,
        DifferentFirstPage = DifferentFirstPage,
        DifferentOddEvenPages = DifferentOddEvenPages,
        BackgroundColorHex = BackgroundColorHex
    };

    public static WatermarkOptions? CloneWatermarkOptions(WatermarkOptions? options) =>
        options is null
            ? null
            : options with
            {
                ImageBytes = options.ImageBytes is null ? null : (byte[])options.ImageBytes.Clone()
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
    /// Whether paragraphs with no serialized line-spacing rule should use Word's application default
    /// multiple (1.15) instead of the host text engine's natural single-line box. Set by the DOCX reader;
    /// false for documents authored directly in the model so their existing layout remains unchanged.
    /// Direct paragraph/style line rules still take precedence.
    /// </summary>
    public bool UseWordApplicationDefaultLineSpacing { get; set; }

    /// <summary>
    /// Whether the imported package omitted <c>w:docDefaults/w:rPrDefault</c> and therefore uses Word's
    /// application run defaults. False for model-authored documents and packages with explicit run defaults.
    /// </summary>
    public bool UseWordApplicationDefaultRunFormatting { get; set; }

    /// <summary>
    /// The single modelled FreeW multilevel-list definition. Its per-level number formats map to the
    /// fixed FreeW multilevel numbering definition in word/numbering.xml.
    /// </summary>
    public MultiLevelListFormat MultiLevelList { get; } = new();

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
    /// Word's "Mark as Final" flag. When true the document is advisory read-only: editors should open it
    /// non-editable and show a "Marked as Final" banner ("Edit Anyway" clears it). Persisted following the
    /// Word convention as the <c>_MarkAsFinal</c> boolean custom document property (docProps/custom.xml);
    /// the reader maps it back here. Independent of <see cref="Protection"/> (enforced restrict-editing).
    /// </summary>
    public bool MarkedAsFinal { get; set; }

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
    /// Document-level footnote numbering options (number format, start-at, restart). Read from and
    /// written to <c>w:footnotePr</c> in word/settings.xml. A fresh document uses Word's defaults so
    /// no element is emitted until the user changes something.
    /// </summary>
    public NoteNumberingOptions FootnoteNumbering { get; } = new();

    /// <summary>
    /// Document-level endnote numbering options (number format, start-at, restart). Read from and
    /// written to <c>w:endnotePr</c> in word/settings.xml. A fresh document uses Word's defaults so
    /// no element is emitted until the user changes something.
    /// </summary>
    public NoteNumberingOptions EndnoteNumbering { get; } = new();

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
            OutlineLevel = 0,
            Run = new RunFormatting { Bold = true, FontSizePt = 16, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 12, SpaceAfterPt = 4 }
        };
        Styles["Heading2"] = new DocumentStyle
        {
            Id = "Heading2",
            Name = "Heading 2",
            BasedOnStyleId = "Normal",
            OutlineLevel = 1,
            Run = new RunFormatting { Bold = true, FontSizePt = 13, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 10, SpaceAfterPt = 4 }
        };
        Styles["Heading3"] = new DocumentStyle
        {
            Id = "Heading3",
            Name = "Heading 3",
            BasedOnStyleId = "Normal",
            OutlineLevel = 2,
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
