namespace FreeX.App.Presentation.SheetUI;

/// <summary>
/// The three toggles in Excel's Data ▸ Outline ▸ Settings dialog. <see cref="SummaryBelow"/> places
/// summary rows below the detail rows they summarise (vs. above); <see cref="SummaryRight"/> places
/// summary columns to the right of their detail columns (vs. left); <see cref="ApplyStyles"/> turns on
/// automatic outline styling.
/// </summary>
public sealed record OutlineSettingsState(bool SummaryBelow, bool SummaryRight, bool ApplyStyles);

/// <summary>
/// Portable planner for the Outline "Settings" dialog. Reads a sheet's stored, nullable outline flags
/// into a fully-resolved <see cref="OutlineSettingsState"/> for the dialog to display (applying Excel's
/// defaults for unset values), and reports whether an accepted state actually differs from the current
/// one so the host can skip a no-op command. Pure data in, pure data out — no view-framework or
/// host types.
/// </summary>
public static class OutlineSettingsPlanner
{
    /// <summary>Excel default: summary rows appear below the detail they summarise.</summary>
    public const bool DefaultSummaryBelow = true;

    /// <summary>Excel default: summary columns appear to the right of the detail they summarise.</summary>
    public const bool DefaultSummaryRight = true;

    /// <summary>Excel default: automatic outline styles are off.</summary>
    public const bool DefaultApplyStyles = false;

    /// <summary>
    /// Resolves the dialog's initial checkbox state from a sheet's stored nullable flags, applying the
    /// Excel defaults for any flag the sheet has not set explicitly.
    /// </summary>
    public static OutlineSettingsState FromStored(bool? summaryBelow, bool? summaryRight, bool? applyStyles) =>
        new(
            summaryBelow ?? DefaultSummaryBelow,
            summaryRight ?? DefaultSummaryRight,
            applyStyles ?? DefaultApplyStyles);

    /// <summary>
    /// True when <paramref name="accepted"/> differs from the sheet's current resolved state, so the host
    /// only issues an (undoable) command when something actually changed.
    /// </summary>
    public static bool HasChanges(
        OutlineSettingsState accepted,
        bool? storedSummaryBelow,
        bool? storedSummaryRight,
        bool? storedApplyStyles)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        return accepted != FromStored(storedSummaryBelow, storedSummaryRight, storedApplyStyles);
    }
}
