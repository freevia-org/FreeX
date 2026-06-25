namespace FreeP.Core.Model;

/// <summary>
/// A small catalogue of built-in presentation themes, each with a distinct name,
/// 12-slot color scheme, and major/minor font pair.  These mirror the themes that
/// ship with PowerPoint 2016+.
///
/// Consumer code should call <see cref="GetAll"/> to enumerate options or
/// <see cref="GetById"/> to look up by <see cref="BuiltInThemeId"/>.
/// </summary>
public static class BuiltInThemes
{
    // ── IDs ───────────────────────────────────────────────────────────────────────

    public static class Id
    {
        public const string Office  = "Office";
        public const string Berlin  = "Berlin";
        public const string Facet   = "Facet";
        public const string Ion     = "Ion";
        public const string Slice   = "Slice";
    }

    // ── Registry ──────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<BuiltInThemeEntry> _all = new[]
    {
        new BuiltInThemeEntry(Id.Office,  "Office Theme",  BuildOffice()),
        new BuiltInThemeEntry(Id.Berlin,  "Berlin",        BuildBerlin()),
        new BuiltInThemeEntry(Id.Facet,   "Facet",         BuildFacet()),
        new BuiltInThemeEntry(Id.Ion,     "Ion",           BuildIon()),
        new BuiltInThemeEntry(Id.Slice,   "Slice",         BuildSlice()),
    };

    /// <summary>All built-in themes, in display order.</summary>
    public static IReadOnlyList<BuiltInThemeEntry> GetAll() => _all;

    /// <summary>
    /// Returns the theme for <paramref name="id"/>, or null if the id is not recognised.
    /// </summary>
    public static PresentationTheme? GetById(string id)
    {
        foreach (var e in _all)
            if (e.Id == id) return e.Theme;
        return null;
    }

    // ── Factory helpers ──────────────────────────────────────────────────────────

    private static PresentationTheme Build(
        string name,
        int dk1, int lt1, int dk2, int lt2,
        int a1, int a2, int a3, int a4, int a5, int a6,
        int hl, int fhl,
        string majorFont, string minorFont)
    {
        var cs = new PresentationColorScheme();
        cs[ThemeColorSlot.Dk1]      = SrgbColor.FromRgb(dk1);
        cs[ThemeColorSlot.Lt1]      = SrgbColor.FromRgb(lt1);
        cs[ThemeColorSlot.Dk2]      = SrgbColor.FromRgb(dk2);
        cs[ThemeColorSlot.Lt2]      = SrgbColor.FromRgb(lt2);
        cs[ThemeColorSlot.Accent1]  = SrgbColor.FromRgb(a1);
        cs[ThemeColorSlot.Accent2]  = SrgbColor.FromRgb(a2);
        cs[ThemeColorSlot.Accent3]  = SrgbColor.FromRgb(a3);
        cs[ThemeColorSlot.Accent4]  = SrgbColor.FromRgb(a4);
        cs[ThemeColorSlot.Accent5]  = SrgbColor.FromRgb(a5);
        cs[ThemeColorSlot.Accent6]  = SrgbColor.FromRgb(a6);
        cs[ThemeColorSlot.HLink]    = SrgbColor.FromRgb(hl);
        cs[ThemeColorSlot.FolHLink] = SrgbColor.FromRgb(fhl);

        return new PresentationTheme
        {
            Name = name,
            ColorScheme = cs,
            FontScheme  = new PresentationFontScheme
            {
                MajorLatinFont = majorFont,
                MinorLatinFont = minorFont,
            }
        };
    }

    // ── Individual theme builders ─────────────────────────────────────────────────

    /// <summary>Office 2013+ default theme (blue accents, Calibri fonts).</summary>
    private static PresentationTheme BuildOffice() => Build(
        "Office Theme",
        dk1:  0x000000, lt1:  0xFFFFFF,
        dk2:  0x44546A, lt2:  0xE7E6E6,
        a1:   0x4472C4, a2:   0xED7D31,
        a3:   0xA9D18E, a4:   0xFFC000,
        a5:   0x5B9BD5, a6:   0x70AD47,
        hl:   0x0563C1, fhl:  0x954F72,
        majorFont: "Calibri Light", minorFont: "Calibri");

    /// <summary>Berlin theme (dark navy/gold palette, Trebuchet fonts).</summary>
    private static PresentationTheme BuildBerlin() => Build(
        "Berlin",
        dk1:  0x000000, lt1:  0xFFFFFF,
        dk2:  0x323E4F, lt2:  0xF0F0F0,
        a1:   0xD7AC1F, a2:   0xD06B1B,
        a3:   0x4DA79A, a4:   0x716FB2,
        a5:   0xA62B2B, a6:   0x457B9D,
        hl:   0x0077B6, fhl:  0x6A0572,
        majorFont: "Trebuchet MS", minorFont: "Trebuchet MS");

    /// <summary>Facet theme (muted greens/greys, Trebuchet + Georgia).</summary>
    private static PresentationTheme BuildFacet() => Build(
        "Facet",
        dk1:  0x000000, lt1:  0xFFFFFF,
        dk2:  0x3B3838, lt2:  0xF5F5F5,
        a1:   0x90C226, a2:   0x54A021,
        a3:   0xE6B219, a4:   0xD17B0F,
        a5:   0x457B9D, a6:   0x2A6496,
        hl:   0x006621, fhl:  0x800000,
        majorFont: "Trebuchet MS", minorFont: "Georgia");

    /// <summary>Ion theme (vibrant orange/teal, Century Gothic fonts).</summary>
    private static PresentationTheme BuildIon() => Build(
        "Ion",
        dk1:  0x000000, lt1:  0xFFFFFF,
        dk2:  0x1D3A5F, lt2:  0xF4F4F4,
        a1:   0xE7700D, a2:   0x009AC7,
        a3:   0xC4DE00, a4:   0x9E2063,
        a5:   0x00B588, a6:   0xDB3A34,
        hl:   0xE7700D, fhl:  0x6B6B6B,
        majorFont: "Century Gothic", minorFont: "Century Gothic");

    /// <summary>Slice theme (crimson/dark-grey, Gill Sans / Constantia).</summary>
    private static PresentationTheme BuildSlice() => Build(
        "Slice",
        dk1:  0x000000, lt1:  0xFFFFFF,
        dk2:  0x3E3E3E, lt2:  0xF9F9F9,
        a1:   0xCC2529, a2:   0xDB6C00,
        a3:   0x007E8A, a4:   0x006B3E,
        a5:   0x5154A0, a6:   0x813530,
        hl:   0xCC2529, fhl:  0x813530,
        majorFont: "Gill Sans MT", minorFont: "Constantia");
}

/// <summary>One entry in the built-in theme catalogue.</summary>
public sealed class BuiltInThemeEntry
{
    internal BuiltInThemeEntry(string id, string displayName, PresentationTheme theme)
    {
        Id          = id;
        DisplayName = displayName;
        Theme       = theme;
    }

    /// <summary>Stable string id (use the constants in <see cref="BuiltInThemes.Id"/>).</summary>
    public string Id { get; }

    /// <summary>Human-readable display name shown in the ribbon gallery.</summary>
    public string DisplayName { get; }

    /// <summary>The theme model instance.</summary>
    public PresentationTheme Theme { get; }
}
