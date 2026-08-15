namespace Free.Shared.AppServices;

/// <summary>
/// Reload-before-write merge for the sister apps' shared basic option fields (recent-files cap, default
/// save format, UI language). Two windows -- possibly in two separate processes, since neither FreeW nor
/// FreeP enforces single-instance -- can each hold a snapshot of the options file loaded at their own
/// startup. If an Options dialog's OK handler simply persisted its own in-memory snapshot as the whole
/// document, the second window to save would silently discard whatever another window changed and
/// persisted meanwhile -- a last-writer-wins lost update -- even for fields its own user never touched.
///
/// <para>
/// Mirrors FreeX's <c>OptionsDialogPlanner.MergeOntoFreshLoad</c>: callers reload the freshest on-disk
/// snapshot immediately before saving and copy across only the fields that actually changed between the
/// dialog's open-time snapshot and the user's edited result, leaving every other field at whatever is
/// freshest on disk. Covers exactly the three fields <see cref="IBasicApplicationOptions"/> guarantees;
/// callers whose option model carries additional app-specific fields (e.g. FreeW's AutoCorrect settings)
/// apply the same diff-and-copy pattern to those fields after calling this.
/// </para>
/// </summary>
public static class BasicApplicationOptionsMerge
{
    public static void MergeOntoFreshLoad(
        IBasicApplicationOptions freshFromDisk,
        IBasicApplicationOptions openTimeSnapshot,
        IBasicApplicationOptions edited)
    {
        ArgumentNullException.ThrowIfNull(freshFromDisk);
        ArgumentNullException.ThrowIfNull(openTimeSnapshot);
        ArgumentNullException.ThrowIfNull(edited);

        if (edited.RecentFilesCap != openTimeSnapshot.RecentFilesCap)
            freshFromDisk.RecentFilesCap = edited.RecentFilesCap;

        if (!string.Equals(edited.DefaultSaveFormat, openTimeSnapshot.DefaultSaveFormat, StringComparison.Ordinal))
            freshFromDisk.DefaultSaveFormat = edited.DefaultSaveFormat;

        if (!string.Equals(edited.UiLanguage, openTimeSnapshot.UiLanguage, StringComparison.Ordinal))
            freshFromDisk.UiLanguage = edited.UiLanguage;
    }
}
