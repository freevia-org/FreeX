namespace FreeP.Core.Model;

/// <summary>
/// Default paragraph/run properties for a single indent level (0-based; level 0 = top-level).
/// Corresponds to <c>a:lvl1pPr</c> .. <c>a:lvl9pPr</c> inside <c>p:txStyles/*Style</c>
/// and <c>a:lstStyle</c> elements on layout placeholders.
/// </summary>
public sealed class TextStyleLevel
{
    /// <summary>Paragraph alignment, or null if not set at this level.</summary>
    public TextAlign? Align { get; set; }

    /// <summary>Paragraph reading direction, or null if not set at this level.</summary>
    public bool? RightToLeft { get; set; }

    /// <summary>Left margin (first-line hanging indent) in EMU, or null if not set.</summary>
    public long? MarginLeftEmu { get; set; }

    /// <summary>Indent (negative for hanging) in EMU, or null if not set.</summary>
    public long? IndentEmu { get; set; }

    // ── Default run properties (a:defRPr) ──────────────────────────────────────

    /// <summary>Default font size in points (e.g. 28.0), or null if not set.</summary>
    public double? FontSizePt { get; set; }

    /// <summary>Default bold, or null if not set.</summary>
    public bool? Bold { get; set; }

    /// <summary>Default italic, or null if not set.</summary>
    public bool? Italic { get; set; }

    /// <summary>Default text color, or null if not set.</summary>
    public ThemeAwareColor? Color { get; set; }

    /// <summary>Default Latin font family (e.g. "+mj-lt" = theme major font), or null if not set.</summary>
    public string? LatinFont { get; set; }

    /// <summary>Bullet kind for this level, or null if not specified at this level.</summary>
    public BulletKind? BulletKind { get; set; }

    /// <summary>Bullet character (when BulletKind == Char).</summary>
    public string? BulletChar { get; set; }

    // ── Wave 19A: extended style-level bullet fields ───────────────────────────

    /// <summary>Auto-number type for Auto bullets at this level.</summary>
    public AutoNumType AutoNumType { get; set; } = AutoNumType.ArabicPeriod;

    /// <summary>Bullet color at this level (null = inherit).</summary>
    public ThemeAwareColor? BulletColor { get; set; }

    /// <summary>True when this level uses <c>a:buClrTx</c> to follow run text color.</summary>
    public bool BulletColorFollowsText { get; set; }

    /// <summary>Bullet size percent at this level in 1000ths-of-a-percent (null = inherit).</summary>
    public int? BulletSizePct { get; set; }

    /// <summary>Absolute bullet size in points from <c>a:buSzPts</c> (null = inherit).</summary>
    public double? BulletSizePt { get; set; }

    /// <summary>True when this level uses <c>a:buSzTx</c> to follow run text size.</summary>
    public bool BulletSizeFollowsText { get; set; }

    /// <summary>Bullet font family override at this level (null = inherit).</summary>
    public string? BulletFontFamily { get; set; }

    /// <summary>True when this level uses <c>a:buFontTx</c> to follow run text font.</summary>
    public bool BulletFontFollowsText { get; set; }
}

/// <summary>
/// Up to 9 indent-level defaults for one text style category (title / body / other).
/// Index 0 = lvl1pPr (top-level), index 8 = lvl9pPr.
/// </summary>
public sealed class TextStyleLevels
{
    private readonly TextStyleLevel?[] _levels = new TextStyleLevel?[9];

    /// <summary>Returns the level properties, or null if that level has no explicit settings.</summary>
    public TextStyleLevel? this[int level]
    {
        get => (level >= 0 && level < 9) ? _levels[level] : null;
        set { if (level >= 0 && level < 9) _levels[level] = value; }
    }

    /// <summary>True if at least one level has been set.</summary>
    public bool HasAny => _levels.Any(l => l is not null);

    /// <summary>Returns the effective (first-set) level properties walking from the given level up to 0.</summary>
    public TextStyleLevel? Resolve(int level)
    {
        for (int l = Math.Min(level, 8); l >= 0; l--)
            if (_levels[l] is { } found) return found;
        return null;
    }
}

/// <summary>
/// Master text styles from <c>p:txStyles</c>: title, body, and "other" (footer/date/slide-number)
/// default paragraph and run properties per indent level.
/// </summary>
public sealed class MasterTextStyles
{
    /// <summary>Title placeholder defaults (<c>p:titleStyle</c>).</summary>
    public TextStyleLevels TitleStyle { get; } = new();

    /// <summary>Body placeholder defaults (<c>p:bodyStyle</c>).</summary>
    public TextStyleLevels BodyStyle { get; } = new();

    /// <summary>Other placeholder defaults (<c>p:otherStyle</c>).</summary>
    public TextStyleLevels OtherStyle { get; } = new();
}

/// <summary>
/// A slide master: the root of the layout/theme inheritance hierarchy. Holds placeholder shapes
/// (with default geometry and text styles) that slide layouts and slides inherit from.
/// Corresponds to <c>slideMaster*.xml</c> in the .pptx package.
/// </summary>
public sealed class SlideMaster
{
    /// <summary>Stable identifier (from the relationship id, e.g. "rId1").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Reference to the theme used by this master (by theme name).</summary>
    public string? ThemeId { get; set; }

    /// <summary>
    /// The theme owned by this slide master (color + font schemes).
    /// Each master in a multi-master deck owns a distinct theme; resolving slide colors/fonts
    /// must use this theme rather than the shared <see cref="Presentation.Theme"/> singleton.
    /// Null only when the master has no theme part (degenerate packages); callers fall back to
    /// <see cref="Presentation.Theme"/> in that case.
    /// </summary>
    public PresentationTheme? Theme { get; set; }

    /// <summary>
    /// Placeholder shapes on this master, in z-order. These define default geometry and
    /// text properties for all placeholders on descendant layouts and slides.
    /// </summary>
    public List<SlideShape> Placeholders { get; } = new();

    /// <summary>Optional background fill for this master (inherited by layouts/slides).</summary>
    public ShapeFill? Background { get; set; }

    /// <summary>
    /// Master text styles parsed from <c>p:txStyles</c>. Null if the element was absent
    /// (pre-Wave-6B masters/new masters). Provides per-level font-size/bold/color defaults
    /// for title, body, and other placeholders.
    /// </summary>
    public MasterTextStyles? TextStyles { get; set; }

    /// <summary>
    /// Raw color map from <c>p:clrMap</c>. Stored as a dictionary mapping scheme-color role
    /// name (e.g. "bg1") to target slot name (e.g. "lt1"). Null if absent.
    /// Used by the compositor for correct scheme-color resolution per master.
    /// </summary>
    public Dictionary<string, string>? ColorMap { get; set; }
}

/// <summary>Slide layout type identifiers from OOXML <c>p:sld type="..."</c>.</summary>
public enum SlideLayoutType
{
    Title = 0,
    TitleContent = 1,
    TitleOnly = 2,
    Blank = 3,
    TwoContent = 4,
    Comparison = 5,
    ContentCaption = 6,
    PictureCaption = 7,
    Custom = 8
}

/// <summary>
/// A slide layout: defines the default placeholder positions and styles for a class of slides.
/// Corresponds to <c>slideLayout*.xml</c> in the .pptx package.
/// </summary>
public sealed class SlideLayout
{
    /// <summary>Stable identifier (from the relationship id, e.g. "rId1").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The OPC part path within the zip archive (e.g. "ppt/slideLayouts/slideLayout1.xml").
    /// Used by the reader to match slides to their layout by path.
    /// </summary>
    public string PartPath { get; set; } = string.Empty;

    /// <summary>Human-readable layout name (from <c>p:cSld name="..."</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Layout type (see OOXML §19.3.1.16 p:sldLayout type).</summary>
    public SlideLayoutType LayoutType { get; set; } = SlideLayoutType.Custom;

    /// <summary>Reference to the parent slide master (by master Id).</summary>
    public string? MasterId { get; set; }

    /// <summary>
    /// Placeholder shapes on this layout, in z-order. These override master defaults and
    /// provide default geometry/style for slide placeholders.
    /// </summary>
    public List<SlideShape> Placeholders { get; } = new();

    /// <summary>Optional background fill for this layout (overrides master, inherited by slides).</summary>
    public ShapeFill? Background { get; set; }

    /// <summary>
    /// Raw color map override from this layout's <c>p:clrMapOvr/a:overrideClrMapping</c>.
    /// Maps scheme-color role name (e.g. "bg1") to target slot name (e.g. "lt1"). Null when the
    /// layout carries <c>a:masterClrMapping</c> (inherit the master's <see cref="SlideMaster.ColorMap"/>)
    /// or has no <c>p:clrMapOvr</c> element at all.
    /// Resolution precedence for a slide is: <c>Slide.ColorMapOverride</c> ?? this ?? the master's map.
    /// </summary>
    public Dictionary<string, string>? ColorMapOverride { get; set; }

    /// <summary>
    /// Per-placeholder list styles parsed from each layout placeholder's <c>a:lstStyle</c>.
    /// Key = Placeholder Idx (with Type encoded as Idx*100+Type for uniqueness), value = the levels.
    /// Populated by the reader; written back faithfully by the writer.
    /// </summary>
    public Dictionary<int, TextStyleLevels> PlaceholderLstStyles { get; } = new();

    /// <summary>
    /// <c>p:sldLayout/@showMasterSp</c> attribute (OOXML default: true). Controls whether this
    /// layout's slide master's placeholder shapes ("Hide Background Graphics" in PowerPoint's
    /// Slide Master view, per-layout) are shown through on slides that use this layout.
    /// Independent of <see cref="Slide.ShowMasterShapes"/> (per-slide override) and
    /// <see cref="Presentation.ShowMasterShapes"/> (deck-wide slideshow-session toggle); all three
    /// gates must be true for master decoration to render on a given slide.
    /// </summary>
    public bool ShowMasterShapes { get; set; } = true;
}
