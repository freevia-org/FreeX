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
    Func<TTarget, AutosaveRecoveryCandidate, CancellationToken, Task> RestoreAsync,
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
                    try
                    {
                        var target = usePrimaryTarget
                            ? host.PrimaryTarget
                            : await host.CreateAdditionalTargetAsync(cancellationToken);
                        await host.RestoreAsync(target, offer.Candidate, cancellationToken);
                    }
                    catch
                    {
                        // A bad snapshot or unavailable native window must not disrupt startup.
                    }
                    finally
                    {
                        TryDelete(host.DeleteCandidate, offer.Candidate);
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
