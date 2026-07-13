namespace FreeW.Core.Model;

/// <summary>
/// A curated SmartArt LAYOUT preset: a named diagram geometry that determines how nodes are arranged
/// (boxes + connectors). The <see cref="Id"/> maps to the Word layout-part uniqueId suffix and to the
/// FreeW renderer's geometry switch. The <see cref="Kind"/> drives which <see cref="SmartArtKind"/>
/// the diagram must be set to when this layout is applied.
/// </summary>
public sealed record SmartArtLayoutPreset(string Name, string Id, SmartArtKind Kind, string Description)
{
    /// <summary>
    /// Curated layout catalog — at least one entry per SmartArt category (List, Process, Cycle, Hierarchy,
    /// Relationship/Radial, Matrix). Display order matches Word's Layouts gallery row order.
    /// </summary>
    public static readonly IReadOnlyList<SmartArtLayoutPreset> Catalog =
    [
        // ── List ────────────────────────────────────────────────────────────────────────────────────
        new("Basic List",              "list1",          SmartArtKind.List,      "Vertical stack of labelled boxes"),
        new("Vertical Bullet List",    "vertbullet1",    SmartArtKind.List,      "Bulleted vertical list"),
        new("Horizontal Bullet List",  "horizbullet1",   SmartArtKind.List,      "Bulleted horizontal list"),

        // ── Process ─────────────────────────────────────────────────────────────────────────────────
        new("Basic Process",           "process1",       SmartArtKind.Process,   "Left-to-right flow with chevron arrows"),
        new("Continuous Block Process", "continuousBlockProcess", SmartArtKind.Process, "Left-to-right connected process blocks"),
        new("Step Up Process",         "stepup1",        SmartArtKind.Process,   "Ascending staircase flow"),
        new("Step Down Process",       "stepdown1",      SmartArtKind.Process,   "Descending staircase flow"),

        // ── Cycle ───────────────────────────────────────────────────────────────────────────────────
        new("Basic Cycle",             "cycle1",         SmartArtKind.List,      "Circular arrangement of nodes"),
        new("Basic Pyramid",           "pyramid1",       SmartArtKind.List,      "Top-to-bottom stack of widening bands"),

        // ── Hierarchy ───────────────────────────────────────────────────────────────────────────────
        new("Basic Hierarchy",         "hierarchy1",     SmartArtKind.Hierarchy, "Classic org-chart tree"),
        new("Org Chart",               "orgchart1",      SmartArtKind.Hierarchy, "Organisation chart layout"),

        // ── Relationship / Radial ───────────────────────────────────────────────────────────────────
        new("Basic Radial",            "radial1",        SmartArtKind.List,      "Central node with satellite spokes"),

        // ── Matrix ──────────────────────────────────────────────────────────────────────────────────
        new("Basic Matrix",            "matrix1",        SmartArtKind.List,      "Two-by-two quadrant grid"),
    ];

    /// <summary>Find a preset by id (case-insensitive), or null when not found.</summary>
    public static SmartArtLayoutPreset? FindById(string id) =>
        Catalog.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Default layout preset (Basic List).</summary>
    public static SmartArtLayoutPreset Default => Catalog[0];
}

/// <summary>
/// A curated SmartArt COLOR-SCHEME preset. The <see cref="Id"/> maps to the colors-part uniqueId suffix
/// and to the FreeW renderer's node-color selector. Each scheme carries up to four accent colors used
/// to paint nodes in sequence (cycling when there are more nodes than accent slots).
/// </summary>
public sealed record SmartArtColorScheme(
    string Name,
    string Id,
    string Color1Hex,
    string Color2Hex,
    string Color3Hex,
    string Color4Hex,
    string TextHex = "#FFFFFF")
{
    /// <summary>Curated color-scheme catalog matching Word's Change Colors gallery sections.</summary>
    public static readonly IReadOnlyList<SmartArtColorScheme> Catalog =
    [
        // ── Colorful ────────────────────────────────────────────────────────────────────────────────
        new("Colorful Range",  "colorful1", "#4E81BD", "#C0504D", "#9BBB59", "#8064A2"),
        new("Colorful Accent", "colorful2", "#4BACC6", "#F79646", "#9BBB59", "#4E81BD"),

        // ── Accent 1 (monochromatic blue) ──────────────────────────────────────────────────────────
        new("Dark 1",          "accent1",   "#1F3864", "#2F5496", "#4E81BD", "#9DC3E6"),
        new("Gradient Loop",   "accent1g",  "#1F3864", "#4E81BD", "#9DC3E6", "#DEEBF7"),

        // ── Accent 2 (monochromatic red) ───────────────────────────────────────────────────────────
        new("Dark 2",          "accent2",   "#7F0000", "#C00000", "#FF0000", "#FF9999"),

        // ── Accent 3 (monochromatic green) ─────────────────────────────────────────────────────────
        new("Dark 3",          "accent3",   "#375623", "#70AD47", "#A9D18E", "#D5E8CF"),

        // ── Monochrome ──────────────────────────────────────────────────────────────────────────────
        new("Monochrome",      "mono1",     "#595959", "#7F7F7F", "#A6A6A6", "#CCCCCC"),
        new("Dark Outline",    "mono2",     "#262626", "#404040", "#595959", "#7F7F7F"),
    ];

    /// <summary>Find a color scheme by id (case-insensitive), or null when not found.</summary>
    public static SmartArtColorScheme? FindById(string id) =>
        Catalog.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Default color scheme (Colorful Range).</summary>
    public static SmartArtColorScheme Default => Catalog[0];

    /// <summary>Get the fill color for node at <paramref name="index"/> (cycles through the four slots).</summary>
    public string FillHexAt(int index) => (index % 4) switch
    {
        0 => Color1Hex,
        1 => Color2Hex,
        2 => Color3Hex,
        _ => Color4Hex
    };
}

/// <summary>
/// A curated SmartArt STYLE preset controlling node fill/outline/effect treatment (flat → intense →
/// 3D-ish shading). The <see cref="Id"/> maps to the quickStyle-part uniqueId suffix and to the FreeW
/// renderer's style switch. <see cref="ShadowOpacity"/> > 0 adds a drop shadow; <see cref="CornerRadius"/>
/// rounds corners; <see cref="BorderThickness"/> sets the node outline weight.
/// </summary>
public sealed record SmartArtStyle(
    string Name,
    string Id,
    double CornerRadius,
    double BorderThickness,
    double ShadowOpacity,
    double BrightnessAdjust = 0.0)
{
    /// <summary>Curated style catalog matching Word's SmartArt Styles gallery (Best Match for Document + 3D).</summary>
    public static readonly IReadOnlyList<SmartArtStyle> Catalog =
    [
        // ── Flat / Best Match ───────────────────────────────────────────────────────────────────────
        new("Flat",            "flat1",    0,   1.0, 0.00),
        new("Simple Fill",     "subtle1",  3,   0.5, 0.00),
        new("Outline",         "outline1", 0,   1.5, 0.00, -0.1),

        // ── Subtle ──────────────────────────────────────────────────────────────────────────────────
        new("Soft Edge",       "subtle2",  4,   0.5, 0.15),
        new("Moderate Effect", "moderate1",2,   1.0, 0.20),

        // ── Intense ─────────────────────────────────────────────────────────────────────────────────
        new("Intense Effect",  "intense1", 0,   1.5, 0.30, 0.10),
        new("Insert",          "intense2", 5,   0,   0.35, 0.15),

        // ── 3D ──────────────────────────────────────────────────────────────────────────────────────
        new("Cartoon",         "3d1",      8,   1.0, 0.40, 0.20),
        new("Powder",          "3d2",      12,  0,   0.45, 0.25),
    ];

    /// <summary>Find a style preset by id (case-insensitive), or null when not found.</summary>
    public static SmartArtStyle? FindById(string id) =>
        Catalog.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Default style preset (Flat).</summary>
    public static SmartArtStyle Default => Catalog[0];
}
