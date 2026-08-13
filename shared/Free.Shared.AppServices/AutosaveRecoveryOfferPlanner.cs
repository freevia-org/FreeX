using System.Globalization;

namespace Free.Shared.AppServices;

/// <summary>
/// Describes one localized startup-recovery offer without creating or showing native UI.
/// </summary>
public sealed record AutosaveRecoveryOfferPlan(
    AutosaveRecoveryCandidate Candidate,
    string PromptKey,
    object?[] PromptArguments,
    string TitleKey,
    string TimestampText);

/// <summary>
/// Prepares autosave candidates and plans the localized prompt contract shared by application hosts.
/// </summary>
public static class AutosaveRecoveryOfferPlanner
{
    public const string TitleKey = "Startup_RecoveryTitle";
    public const string PromptKey = "Startup_RecoveryPrompt";
    public const string NamedPromptKey = "Startup_RecoveryPromptNamed";
    public const string MultiplePromptKey = "Startup_RecoveryPromptMultiple";
    public const string NamedMultiplePromptKey = "Startup_RecoveryPromptNamedMultiple";

    public static IReadOnlyList<AutosaveRecoveryOfferPlan> PrepareOffers(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var prepared = AutosaveRecoveryCandidateProcessor.PrepareForRecovery(candidates);
        if (prepared.Count == 0)
            return [];

        culture ??= CultureInfo.CurrentCulture;
        var offers = new AutosaveRecoveryOfferPlan[prepared.Count];
        for (var index = 0; index < prepared.Count; index++)
        {
            offers[index] = CreateOffer(
                prepared[index],
                remainingCount: prepared.Count - index,
                culture);
        }

        return offers;
    }

    internal static AutosaveRecoveryOfferPlan CreateOffer(
        AutosaveRecoveryCandidate candidate,
        int remainingCount,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentOutOfRangeException.ThrowIfLessThan(remainingCount, 1);

        var timestampText = FormatTimestamp(candidate, culture);
        var hasName = !string.IsNullOrWhiteSpace(candidate.Sidecar.DisplayName);

        if (remainingCount > 1)
        {
            return hasName
                ? new AutosaveRecoveryOfferPlan(
                    candidate,
                    NamedMultiplePromptKey,
                    [candidate.Sidecar.DisplayName, remainingCount, timestampText],
                    TitleKey,
                    timestampText)
                : new AutosaveRecoveryOfferPlan(
                    candidate,
                    MultiplePromptKey,
                    [remainingCount, timestampText],
                    TitleKey,
                    timestampText);
        }

        return hasName
            ? new AutosaveRecoveryOfferPlan(
                candidate,
                NamedPromptKey,
                [candidate.Sidecar.DisplayName, timestampText],
                TitleKey,
                timestampText)
            : new AutosaveRecoveryOfferPlan(
                candidate,
                PromptKey,
                [timestampText],
                TitleKey,
                timestampText);
    }

    public static string FormatTimestamp(
        AutosaveRecoveryCandidate candidate,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        culture ??= CultureInfo.CurrentCulture;

        return AutosaveRecoveryCandidateProcessor.ResolveTimestamp(candidate)
            .ToLocalTime()
            .ToString("g", culture);
    }
}
