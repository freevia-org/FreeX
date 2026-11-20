namespace Free.Shared.Ribbon;

/// <summary>
/// Office's ribbon display modes: "Always show Ribbon" or "Show tabs only" (the collapsed ribbon).
/// While collapsed, clicking a tab shows the ribbon for that one use: it hides again once a command
/// runs or the user clicks outside it, exactly as in Excel, Word and PowerPoint.
/// </summary>
/// <remarks>
/// Platform-neutral so every host (WPF and Avalonia, every sister app) follows the same rules; hosts
/// only translate <see cref="ShowsCommands"/> into visibility and forward the user's gestures.
/// </remarks>
public sealed class RibbonDisplayState
{
    /// <summary>"Show tabs only": the command area is hidden unless shown temporarily.</summary>
    public bool IsCollapsed { get; private set; }

    /// <summary>The collapsed ribbon is shown for one use after a tab click.</summary>
    public bool IsTemporarilyShown { get; private set; }

    /// <summary>Whether the command area below the tab strip should be visible now.</summary>
    public bool ShowsCommands => !IsCollapsed || IsTemporarilyShown;

    /// <summary>Raised whenever <see cref="ShowsCommands"/> or <see cref="IsCollapsed"/> may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Ctrl+F1, double-clicking a tab, or the Collapse/Pin ribbon commands.</summary>
    public void ToggleCollapsed() => SetCollapsed(!IsCollapsed);

    public void SetCollapsed(bool collapsed)
    {
        if (IsCollapsed == collapsed && !IsTemporarilyShown)
            return;

        IsCollapsed = collapsed;
        IsTemporarilyShown = false;
        RaiseChanged();
    }

    /// <summary>
    /// A single click on a content tab. Shows the collapsed ribbon temporarily; a second click on a
    /// tab while it is shown keeps it open (it only switches tabs).
    /// </summary>
    public void OnTabClicked()
    {
        if (!IsCollapsed || IsTemporarilyShown)
            return;

        IsTemporarilyShown = true;
        RaiseChanged();
    }

    /// <summary>A ribbon command ran (and did not just open a menu or gallery).</summary>
    public void OnCommandExecuted() => HideTemporaryRibbon();

    /// <summary>The user clicked outside the ribbon, or pressed Esc.</summary>
    public void OnDismissed() => HideTemporaryRibbon();

    private void HideTemporaryRibbon()
    {
        if (!IsTemporarilyShown)
            return;

        IsTemporarilyShown = false;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
