namespace Free.Shared.AppServices;

/// <summary>
/// The shared "apply, reload the freshest on-disk snapshot, merge only the changed fields, persist"
/// commit flow for a running sister-app shell's live options. Product runtime sessions supply how to
/// snapshot, apply, merge and copy back their own option fields; the ordering and the
/// persisted-only copy-back live here once.
/// </summary>
public static class OptionsReloadMergeCommit
{
    /// <summary>
    /// Runs the commit. <paramref name="captureOpenTimeSnapshot"/> is invoked before
    /// <paramref name="applyToLive"/> (only when <paramref name="reloadFresh"/> is supplied). Without a
    /// reload the live instance is persisted as a whole document. With one, the fresh snapshot is
    /// normalized, <paramref name="mergeOntoFresh"/> copies across the changed fields, the merged result
    /// is persisted, and on success <paramref name="copyFreshIntoLive"/> aligns the live instance.
    /// </summary>
    public static bool Commit<TOptions>(
        TOptions liveOptions,
        Func<TOptions> captureOpenTimeSnapshot,
        Action applyToLive,
        Func<TOptions> captureEdited,
        Func<TOptions, bool> persist,
        Func<TOptions>? reloadFresh,
        Action<TOptions, TOptions, TOptions> mergeOntoFresh,
        Action<TOptions, TOptions> copyFreshIntoLive)
        where TOptions : class, INormalizableApplicationOptions
    {
        ArgumentNullException.ThrowIfNull(liveOptions);
        ArgumentNullException.ThrowIfNull(captureOpenTimeSnapshot);
        ArgumentNullException.ThrowIfNull(applyToLive);
        ArgumentNullException.ThrowIfNull(captureEdited);
        ArgumentNullException.ThrowIfNull(persist);
        ArgumentNullException.ThrowIfNull(mergeOntoFresh);
        ArgumentNullException.ThrowIfNull(copyFreshIntoLive);

        var openTimeSnapshot = reloadFresh is null ? null : captureOpenTimeSnapshot();
        applyToLive();

        if (reloadFresh is null || openTimeSnapshot is null)
            return persist(liveOptions);

        var fresh = reloadFresh();
        fresh.Normalize();
        mergeOntoFresh(fresh, openTimeSnapshot, captureEdited());

        var persisted = persist(fresh);
        if (persisted)
            copyFreshIntoLive(liveOptions, fresh);

        return persisted;
    }
}
