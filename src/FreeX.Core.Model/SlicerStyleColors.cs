namespace FreeX.Core.Model;

/// <summary>
/// The resolved fill/text colors a slicer (or timeline) control draws with, derived from its built-in
/// Excel slicer style (<c>SlicerStyleLight1</c>, <c>SlicerStyleLight2</c>, …) and the workbook theme.
/// Excel's built-in slicer styles are theme-driven: the light family tints the header band and the
/// selected-item tile from a single base color (neutral gray for Light1, an accent color for Light2–6),
/// while the body/unselected tiles stay near-white with a light border. This portable resolver lives in
/// the model tier so the WPF, Avalonia, and headless renderers all theme slicers identically.
/// </summary>
public readonly record struct SlicerStyleColors(
    CellColor Header,
    CellColor Border,
    CellColor Body,
    CellColor Tile,
    CellColor SelectedTile,
    CellColor HeaderText,
    CellColor ItemText)
{
    /// <summary>
    /// The colors FreeX used before slicer-style theming existed — kept as the explicit fallback so an
    /// unrecognized style (e.g. a custom <c>SlicerStyleOther*</c> we don't model yet) still renders the
    /// known-good default box rather than something jarring.
    /// </summary>
    public static SlicerStyleColors LegacyDefault { get; } = new(
        Header: new CellColor(91, 155, 213),
        Border: new CellColor(68, 114, 196),
        Body: new CellColor(245, 248, 252),
        Tile: new CellColor(225, 235, 247),
        SelectedTile: new CellColor(198, 224, 180),
        HeaderText: CellColor.White,
        ItemText: new CellColor(89, 89, 89));

    /// <summary>
    /// Resolves the colors for a built-in slicer style name against <paramref name="theme"/>. Recognizes
    /// the <c>SlicerStyleLight1…6</c> family (default = Light1 when the name is null/empty/unrecognized).
    /// Light1 is the neutral gray default; Light2–6 tint from theme accents 2–6 respectively, matching
    /// Excel's built-in palette ordering (Light2→Accent2, …, Light6→Accent6).
    /// </summary>
    public static SlicerStyleColors Resolve(string? styleName, WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var slot = BuiltInFilterControlStylePolicy.ResolveLightAccentSlot(
            styleName,
            "SlicerStyleLight");
        if (slot is null)
            return ResolveLight1(theme);

        // Excel Light2–6: white header background with dark bold caption and an accent-colored outer border.
        // The selected-item tile is the accent at a light tint. Unselected tiles and body stay near-white
        // so unselected items read as "available". Matches Excel's SlicerStyleLight2–6 appearance.
        var accent = theme.GetColor(slot.Value);
        return new SlicerStyleColors(
            Header: CellColor.White,
            Border: accent,
            Body: CellColor.White,
            Tile: CellColor.White,
            SelectedTile: theme.ResolveColor(slot.Value, 0.6),
            HeaderText: new CellColor(64, 64, 64),
            ItemText: new CellColor(64, 64, 64));
    }

    private static SlicerStyleColors ResolveLight1(WorkbookTheme theme)
    {
        // Light1: neutral, theme-independent grays with a faint accent1 selection so the "selected" state is
        // still visible. This is Excel's default slicer look.
        return new SlicerStyleColors(
            Header: new CellColor(245, 245, 245),
            Border: new CellColor(191, 191, 191),
            Body: CellColor.White,
            Tile: CellColor.White,
            SelectedTile: theme.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.6),
            HeaderText: new CellColor(64, 64, 64),
            ItemText: new CellColor(64, 64, 64));
    }

    private static CellColor Darken(CellColor color, double amount)
    {
        var factor = Math.Clamp(1.0 - amount, 0, 1);
        return new CellColor(
            (byte)Math.Clamp(Math.Round(color.R * factor), 0, 255),
            (byte)Math.Clamp(Math.Round(color.G * factor), 0, 255),
            (byte)Math.Clamp(Math.Round(color.B * factor), 0, 255));
    }
}
