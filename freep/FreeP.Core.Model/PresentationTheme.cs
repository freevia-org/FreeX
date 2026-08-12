using System.Xml.Linq;

namespace FreeP.Core.Model;

/// <summary>
/// An sRGB color (0–255 per channel). Immutable value type used throughout the model as a
/// resolved color (after theme color + lumMod/lumOff are applied).
/// </summary>
public readonly record struct SrgbColor(byte R, byte G, byte B)
{
    /// <summary>Creates from a packed 0xRRGGBB integer.</summary>
    public static SrgbColor FromRgb(int rgb) =>
        new((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    /// <summary>Black (#000000).</summary>
    public static readonly SrgbColor Black = new(0, 0, 0);

    /// <summary>White (#FFFFFF).</summary>
    public static readonly SrgbColor White = new(0xFF, 0xFF, 0xFF);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// Named color slots in a DrawingML theme color scheme. These 12 slots are the canonical set from
/// ECMA-376 §20.1.6.2 (dk1/lt1/dk2/lt2/accent1–6/hlink/folHlink).
/// </summary>
public enum ThemeColorSlot
{
    Dk1 = 0,
    Lt1 = 1,
    Dk2 = 2,
    Lt2 = 3,
    Accent1 = 4,
    Accent2 = 5,
    Accent3 = 6,
    Accent4 = 7,
    Accent5 = 8,
    Accent6 = 9,
    HLink = 10,
    FolHLink = 11
}

/// <summary>
/// A reference to a theme color slot plus optional luminance modifiers, so that theme changes
/// cause all referencing objects to update without re-saving. The resolved sRGB is computed as:
/// base = theme[Slot]; if LumMod/LumOff applied, convert to HLS, clamp, convert back.
/// </summary>
public sealed class SchemeColorRef
{
    /// <summary>
    /// The raw OOXML role name as it appears in the XML val= attribute (e.g. "tx1", "bg1", "dk1",
    /// "accent1"). Stored verbatim so the effective clrMap can be re-applied at render time.
    /// Null/empty when the SchemeColorRef was constructed without an XML role name (e.g. in tests
    /// or programmatic usage) — in that case <see cref="Slot"/> is used directly for resolution.
    /// </summary>
    public string? RoleName { get; init; }

    /// <summary>
    /// The theme slot resolved using the DEFAULT clrMap (tx1→Dk1, bg1→Lt1, …).
    /// Used as a fast-path fallback when no master clrMap is available at render time.
    /// When an effective clrMap IS provided to ThemeColorResolver.Resolve, RoleName is
    /// re-applied through that map instead, so this field is bypassed for role names that
    /// can be remapped (tx1/bg1/tx2/bg2 and the canonical dk1/lt1/dk2/lt2/accent*/hlink/folhlink).
    /// </summary>
    public ThemeColorSlot Slot { get; init; }

    /// <summary>Luminance multiplier (0–100000 in OOXML; stored normalized 0.0–1.0 here; 1.0 = no change).</summary>
    public double LumMod { get; init; } = 1.0;

    /// <summary>Luminance offset (0–100000 in OOXML; stored normalized 0.0–1.0 here; 0.0 = no change).</summary>
    public double LumOff { get; init; } = 0.0;

    /// <summary>
    /// DrawingML tint (blend toward white). 0–100000 in OOXML; stored normalized here.
    /// val=100000 (1.0) = original color; val=0 (0.0) = fully white. Default 1.0 = no tint.
    /// </summary>
    public double Tint { get; init; } = 1.0;

    /// <summary>
    /// DrawingML shade (blend toward black). 0–100000 in OOXML; stored normalized here.
    /// val=100000 (1.0) = original color; val=0 (0.0) = fully black. Default 1.0 = no shade.
    /// </summary>
    public double Shade { get; init; } = 1.0;
}

/// <summary>
/// A color with both the resolved sRGB value (used by the renderer) and an optional scheme color
/// reference (so re-theming works without re-parsing). When <see cref="SchemeColor"/> is non-null,
/// the renderer should re-resolve from the live theme; the <see cref="Resolved"/> value is a fallback.
/// </summary>
public sealed class ThemeAwareColor
{
    /// <summary>The resolved sRGB color. Always set; may be re-resolved from <see cref="SchemeColor"/>.</summary>
    public SrgbColor Resolved { get; init; }

    /// <summary>Opacity carried from DrawingML color alpha, where 255 is fully opaque.</summary>
    public byte Alpha { get; init; } = 255;

    /// <summary>If this color derives from a theme slot, the slot reference; otherwise null.</summary>
    public SchemeColorRef? SchemeColor { get; init; }

    public ThemeAwareColor(SrgbColor resolved, byte alpha = 255)
    {
        Resolved = resolved;
        Alpha = alpha;
    }

    public ThemeAwareColor(SrgbColor resolved, SchemeColorRef schemeColor, byte alpha = 255)
    {
        Resolved = resolved;
        SchemeColor = schemeColor;
        Alpha = alpha;
    }

    public static readonly ThemeAwareColor Black = new(SrgbColor.Black);
    public static readonly ThemeAwareColor White = new(SrgbColor.White);
}

/// <summary>
/// The 12-color scheme from a DrawingML theme (<c>a:clrScheme</c>). Maps each
/// <see cref="ThemeColorSlot"/> to an sRGB value.
/// </summary>
public sealed class PresentationColorScheme
{
    private readonly SrgbColor[] _slots = new SrgbColor[12];

    public SrgbColor this[ThemeColorSlot slot]
    {
        get => _slots[(int)slot];
        set => _slots[(int)slot] = value;
    }

    /// <summary>Returns the default Office theme color scheme (Office 2013+).</summary>
    public static PresentationColorScheme CreateDefault()
    {
        var s = new PresentationColorScheme();
        s[ThemeColorSlot.Dk1] = SrgbColor.FromRgb(0x000000);
        s[ThemeColorSlot.Lt1] = SrgbColor.FromRgb(0xFFFFFF);
        s[ThemeColorSlot.Dk2] = SrgbColor.FromRgb(0x44546A);
        s[ThemeColorSlot.Lt2] = SrgbColor.FromRgb(0xE7E6E6);
        s[ThemeColorSlot.Accent1] = SrgbColor.FromRgb(0x4472C4);
        s[ThemeColorSlot.Accent2] = SrgbColor.FromRgb(0xED7D31);
        s[ThemeColorSlot.Accent3] = SrgbColor.FromRgb(0xA9D18E);
        s[ThemeColorSlot.Accent4] = SrgbColor.FromRgb(0xFFC000);
        s[ThemeColorSlot.Accent5] = SrgbColor.FromRgb(0x5B9BD5);
        s[ThemeColorSlot.Accent6] = SrgbColor.FromRgb(0x70AD47);
        s[ThemeColorSlot.HLink] = SrgbColor.FromRgb(0x0563C1);
        s[ThemeColorSlot.FolHLink] = SrgbColor.FromRgb(0x954F72);
        return s;
    }
}

/// <summary>
/// The font scheme from a DrawingML theme (<c>a:fontScheme</c>): major (heading) and minor (body) fonts.
/// </summary>
public sealed class PresentationFontScheme
{
    /// <summary>Major (heading) Latin font name (e.g. "Calibri Light").</summary>
    public string MajorLatinFont { get; set; } = "Calibri Light";

    /// <summary>Minor (body) Latin font name (e.g. "Calibri").</summary>
    public string MinorLatinFont { get; set; } = "Calibri";
}

/// <summary>
/// The theme for a presentation: a color scheme and a font scheme. One theme is shared across
/// all masters in a typical presentation.
/// </summary>
public sealed class PresentationTheme
{
    /// <summary>Theme name (from <c>a:theme name="..."</c>).</summary>
    public string Name { get; set; } = "Office Theme";

    public PresentationColorScheme ColorScheme { get; set; } = PresentationColorScheme.CreateDefault();

    public PresentationFontScheme FontScheme { get; set; } = new();

    /// <summary>
    /// The original <c>&lt;a:fontScheme&gt;</c> XML captured when this theme was read from a .pptx.
    /// Carries East-Asian/complex-script <c>&lt;a:ea&gt;</c>/<c>&lt;a:cs&gt;</c> typefaces that
    /// <see cref="FontScheme"/> does not model. Null for themes created programmatically (e.g. brand
    /// new presentations). The writer patches only the major/minor <c>&lt;a:latin&gt;</c> typeface into
    /// this XML and preserves everything else verbatim — mirrors FreeX's WorkbookTheme.WithFonts.
    /// </summary>
    public string? NativeFontSchemeXml { get; set; }

    /// <summary>
    /// Raw <c>a:fillStyleLst</c> entries from the theme's format scheme (<c>a:fmtScheme</c>), in
    /// document order (index 0 = <c>idx="1"</c>, ...). PowerPoint's built-in Shape Styles gallery
    /// encodes a shape's fill purely as a <c>p:style/a:fillRef</c> index into this list (with a
    /// <c>phClr</c> placeholder color substituted at resolve time) — shapes styled from the
    /// gallery carry no explicit <c>spPr</c> fill at all.
    /// </summary>
    public IReadOnlyList<XElement> FillStyles { get; set; } = Array.Empty<XElement>();

    /// <summary>
    /// Raw <c>a:ln</c> entries from <c>a:lnStyleLst</c>, referenced by <c>p:style/a:lnRef</c> the
    /// same way <see cref="FillStyles"/> is referenced by fillRef.
    /// </summary>
    public IReadOnlyList<XElement> LineStyles { get; set; } = Array.Empty<XElement>();

    /// <summary>
    /// Raw <c>a:bgFillStyleLst</c> entries. Per ECMA-376, a <c>fillRef idx</c> of 1000 or greater
    /// refers here instead of <see cref="FillStyles"/>, using (idx - 1000) as the 1-based index.
    /// </summary>
    public IReadOnlyList<XElement> BackgroundFillStyles { get; set; } = Array.Empty<XElement>();

    /// <summary>
    /// Raw <c>a:effectStyle</c> entries from <c>a:effectStyleLst</c>, referenced by
    /// <c>p:style/a:effectRef</c> the same way <see cref="FillStyles"/> is referenced by fillRef.
    /// </summary>
    public IReadOnlyList<XElement> EffectStyles { get; set; } = Array.Empty<XElement>();

    public static PresentationTheme CreateDefault() => new();
}
