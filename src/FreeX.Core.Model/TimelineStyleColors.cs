namespace FreeX.Core.Model;

/// <summary>
/// The resolved fill/text colors a timeline filter control draws with, derived from its built-in
/// Excel timeline style (<c>TimeSlicerStyleLight1</c>, <c>TimeSlicerStyleLight2</c>, …) and the
/// workbook theme. Excel's built-in timeline styles are theme-driven: the header uses a white
/// background with a dark bold caption for accent styles, with the accent color driving the outer
/// border, the selection band, and the summary date label. The track (unselected range bar) is a
/// neutral light grey regardless of accent. This portable resolver lives in the model tier so the
/// WPF, Avalonia, and headless renderers all theme timelines identically.
/// </summary>
public readonly record struct TimelineStyleColors(
    CellColor Header,
    CellColor Border,
    CellColor Body,
    CellColor Track,
    CellColor SelectionBand,
    CellColor HeaderText,
    CellColor SummaryLabel)
{
    /// <summary>
    /// The colors FreeX used before timeline-style theming existed — kept as the explicit fallback so an
    /// unrecognized style still renders the known-good default box rather than something jarring.
    /// </summary>
    public static TimelineStyleColors LegacyDefault { get; } = new(
        Header: new CellColor(91, 155, 213),
        Border: new CellColor(68, 114, 196),
        Body: new CellColor(245, 248, 252),
        Track: new CellColor(225, 235, 247),
        SelectionBand: new CellColor(198, 224, 180),
        HeaderText: CellColor.White,
        SummaryLabel: new CellColor(89, 89, 89));

    /// <summary>
    /// Resolves the colors for a built-in timeline style name against <paramref name="theme"/>.
    /// Recognizes the <c>TimeSlicerStyleLight1…6</c> family (default = Light1 when the name is
    /// null/empty/unrecognized). Light1 is the neutral gray default; Light2–6 tint from theme
    /// accents 2–6 respectively, matching Excel's built-in palette ordering
    /// (Light2→Accent2, …, Light6→Accent6).
    /// </summary>
    public static TimelineStyleColors Resolve(string? styleName, WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var slot = BuiltInFilterControlStylePolicy.ResolveLightAccentSlot(
            styleName,
            "TimeSlicerStyleLight");
        if (slot is null)
            return ResolveLight1(theme);

        // Excel Light2–6: white header background with dark bold caption and an accent-colored outer
        // border. The selection band is the accent color; the track is a neutral light grey
        // (~RGB 217,217,217) so the selected range stands out clearly.
        var accent = theme.GetColor(slot.Value);
        return new TimelineStyleColors(
            Header: CellColor.White,
            Border: accent,
            Body: CellColor.White,
            Track: new CellColor(217, 217, 217),
            SelectionBand: accent,
            HeaderText: new CellColor(64, 64, 64),
            SummaryLabel: accent);
    }

    private static TimelineStyleColors ResolveLight1(WorkbookTheme theme)
    {
        // Light1: neutral, theme-independent grays. The selection band uses a light accent1 tint
        // so the "selected" state is still visible without dominating.
        var accent = theme.GetColor(WorkbookThemeColorSlot.Accent1);
        return new TimelineStyleColors(
            Header: new CellColor(245, 245, 245),
            Border: new CellColor(191, 191, 191),
            Body: CellColor.White,
            Track: new CellColor(217, 217, 217),
            SelectionBand: theme.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.6),
            HeaderText: new CellColor(64, 64, 64),
            SummaryLabel: accent);
    }

}
