namespace Free.Shared.AppServices;

/// <summary>
/// Platform-neutral worksheet view mode for the status-bar view-shortcut toggles.
/// Mirrors the host's <c>WorksheetViewMode</c> without depending on the domain model,
/// so non-WPF shells (Avalonia, FreeW) can consume the neutral status-bar model.
/// </summary>
public enum StatusBarViewMode
{
    Normal,
    PageLayout,
    PageBreak
}
