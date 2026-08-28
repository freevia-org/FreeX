using Free.Shared.AppServices;

namespace FreeX.App.Services;

/// <summary>
/// Native-host callbacks for startup recovery. Hosts retain UI and window ownership while this
/// contract keeps the recovery decision and lifetime sequence platform-neutral.
/// </summary>
public sealed record StartupRecoveryWorkflowHost<TTarget>(
    TTarget PrimaryTarget,
    Func<AutosaveRecoveryOfferPlan, CancellationToken, ValueTask<bool>> OfferAsync,
    Func<CancellationToken, ValueTask<TTarget>> CreateAdditionalTargetAsync,
    // Returns true only when the candidate's content actually loaded into the target. The
    // candidate is retired (deleted) ONLY on true -- a false/failed/thrown restore leaves the
    // snapshot on disk, since it may be the only surviving copy of the crash-time edits
    // (R165-shared-autosave-recovery-F1).
    Func<TTarget, AutosaveRecoveryCandidate, CancellationToken, Task<bool>> RestoreAsync,
    Func<Func<Task>, CancellationToken, ValueTask> ExecuteRestoreAsync,
    Action<AutosaveRecoveryCandidate> DeleteCandidate)
    where TTarget : class;

/// <summary>
/// Offers recovery candidates in order, restores the first accepted candidate into the primary
/// target and later accepted candidates into new targets, and retires every handled candidate.
/// Recovery is best-effort and never prevents normal application startup.
/// </summary>
public static class StartupRecoveryWorkflow
{
    /// <summary>
    /// Enumerates recoverable snapshots from <paramref name="snapshotStore"/> while excluding
    /// snapshots still owned by a live window or process before any candidate can be offered or
    /// retired. Native hosts should use this overload so liveness filtering cannot drift.
    /// </summary>
    public static async Task<bool> RunAsync<TTarget>(
        AutosaveSnapshotStore snapshotStore,
        StartupRecoveryWorkflowHost<TTarget> host,
        CancellationToken cancellationToken = default)
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(snapshotStore);

        try
        {
            var candidates = snapshotStore.ExcludeLiveOwned(snapshotStore.EnumerateCandidates());
            return await RunAsync(candidates, host, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> RunAsync<TTarget>(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates,
        StartupRecoveryWorkflowHost<TTarget> host,
        CancellationToken cancellationToken = default)
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(host.PrimaryTarget);
        ArgumentNullException.ThrowIfNull(host.OfferAsync);
        ArgumentNullException.ThrowIfNull(host.CreateAdditionalTargetAsync);
        ArgumentNullException.ThrowIfNull(host.RestoreAsync);
        ArgumentNullException.ThrowIfNull(host.ExecuteRestoreAsync);
        ArgumentNullException.ThrowIfNull(host.DeleteCandidate);

        try
        {
            var offers = AutosaveRecoveryOfferPlanner.PrepareOffers(candidates);
            var anyAccepted = false;

            foreach (var offer in offers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!await host.OfferAsync(offer, cancellationToken))
                {
                    TryDelete(host.DeleteCandidate, offer.Candidate);
                    continue;
                }

                var usePrimaryTarget = !anyAccepted;
                anyAccepted = true;

                async Task RestoreAndRetireCandidateAsync()
                {
                    var restored = false;
                    try
                    {
                        var target = usePrimaryTarget
                            ? host.PrimaryTarget
                            : await host.CreateAdditionalTargetAsync(cancellationToken);
                        restored = await host.RestoreAsync(target, offer.Candidate, cancellationToken);
                    }
                    catch
                    {
                        // A bad snapshot or unavailable native window must not disrupt startup.
                    }
                    finally
                    {
                        // Only retire (delete) the candidate once the restore is CONFIRMED to have
                        // loaded the crash-time content. A failed or thrown restore must leave the
                        // snapshot on disk -- it is the only surviving copy of the user's unsaved
                        // work, and deleting it here regardless of outcome previously destroyed that
                        // copy the moment recovery merely LOOKED like it ran (R165-shared-autosave-
                        // recovery-F1).
                        if (restored)
                        {
                            TryDelete(host.DeleteCandidate, offer.Candidate);
                        }
                    }
                }

                await host.ExecuteRestoreAsync(RestoreAndRetireCandidateAsync, cancellationToken);
            }

            return anyAccepted;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(
        Action<AutosaveRecoveryCandidate> deleteCandidate,
        AutosaveRecoveryCandidate candidate)
    {
        try
        {
            deleteCandidate(candidate);
        }
        catch
        {
            // Cleanup is best-effort; recovery state must not make application startup fail.
        }
    }
}
