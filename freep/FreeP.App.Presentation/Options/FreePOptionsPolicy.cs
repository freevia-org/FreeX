namespace FreeP.App.Compositor;

public enum FreePOptionKind
{
    RecentFilesCap,
    DefaultSaveFormat,
    UiLanguage,
}

public enum FreePOptionActivation
{
    Immediate,
    ApplicationRestart,
}

public sealed record FreePOptionDescriptor(
    FreePOptionKind Kind,
    string PersistencePropertyName,
    object DefaultValue,
    int ApplyOrder,
    FreePOptionActivation Activation);

public sealed record FreePOptionsSnapshot(
    int RecentFilesCap,
    string DefaultSaveFormat,
    string UiLanguage)
{
    public FreePOptions ToOptions() => new()
    {
        RecentFilesCap = RecentFilesCap,
        DefaultSaveFormat = DefaultSaveFormat,
        UiLanguage = UiLanguage,
    };
}

public sealed record FreePOptionsChangeSet(
    bool RecentFilesCapChanged,
    bool DefaultSaveFormatChanged,
    bool UiLanguageChanged)
{
    public bool Any => RecentFilesCapChanged || DefaultSaveFormatChanged || UiLanguageChanged;

    public bool IsChanged(FreePOptionKind kind) => kind switch
    {
        FreePOptionKind.RecentFilesCap => RecentFilesCapChanged,
        FreePOptionKind.DefaultSaveFormat => DefaultSaveFormatChanged,
        FreePOptionKind.UiLanguage => UiLanguageChanged,
        _ => false,
    };
}

public sealed record FreePOptionsApplyStep(
    FreePOptionKind Kind,
    int Order,
    FreePOptionActivation Activation);

public enum FreePOptionsRestartDecision
{
    NotRequired,
    Required,
}

public enum FreePOptionsPresentationReloadDecision
{
    NotRequired,
    Required,
}

public sealed record FreePOptionsSideEffectPlan(
    bool UpdateRecentFilesPolicy,
    bool UpdateDefaultSaveFormatPolicy,
    bool RefreshOptionsSummary,
    FreePOptionsRestartDecision ApplicationRestart,
    FreePOptionsPresentationReloadDecision PresentationReload);

public sealed record FreePOptionsApplyPlan(
    FreePOptionsSnapshot Before,
    FreePOptionsSnapshot After,
    FreePOptionsChangeSet Changes,
    IReadOnlyList<FreePOptionsApplyStep> Steps,
    FreePOptionsSideEffectPlan SideEffects,
    bool ShouldPersist);

public sealed record FreePOptionsCommitOutcome(
    FreePOptionsApplyPlan Plan,
    bool PersistenceAttempted,
    bool Persisted);

/// <summary>
/// Canonical descriptors, normalization, persistence projection, change detection, and activation
/// decisions for FreeP's small application-options model.
/// </summary>
public static class FreePOptionsPolicy
{
    public static IReadOnlyList<FreePOptionDescriptor> Descriptors { get; } =
    [
        new(
            FreePOptionKind.RecentFilesCap,
            nameof(FreePOptions.RecentFilesCap),
            FreePOptions.DefaultRecentFilesCap,
            ApplyOrder: 0,
            Activation: FreePOptionActivation.Immediate),
        new(
            FreePOptionKind.DefaultSaveFormat,
            nameof(FreePOptions.DefaultSaveFormat),
            FreePOptions.FxpDefaultFormat,
            ApplyOrder: 1,
            Activation: FreePOptionActivation.Immediate),
        new(
            FreePOptionKind.UiLanguage,
            nameof(FreePOptions.UiLanguage),
            FreePOptions.SystemDefaultLanguage,
            ApplyOrder: 2,
            Activation: FreePOptionActivation.ApplicationRestart),
    ];

    public static FreePOptions NormalizeLoaded(FreePOptions? loadedOptions)
    {
        var options = loadedOptions ?? new FreePOptions();
        options.Normalize();
        return options;
    }

    public static FreePOptionsSnapshot CaptureNormalized(FreePOptions? options)
    {
        var source = options ?? new FreePOptions();
        var normalized = new FreePOptions
        {
            RecentFilesCap = source.RecentFilesCap,
            DefaultSaveFormat = source.DefaultSaveFormat,
            UiLanguage = source.UiLanguage,
        };
        normalized.Normalize();

        return new FreePOptionsSnapshot(
            normalized.RecentFilesCap,
            normalized.DefaultSaveFormat,
            normalized.UiLanguage);
    }

    public static string SelectUiLanguage(FreePOptions options) =>
        CaptureNormalized(options).UiLanguage;

    public static FreePOptionsApplyPlan PlanApply(
        FreePOptions liveOptions,
        FreePOptions editedOptions)
    {
        ArgumentNullException.ThrowIfNull(liveOptions);
        ArgumentNullException.ThrowIfNull(editedOptions);

        var before = CaptureNormalized(liveOptions);
        var after = CaptureNormalized(editedOptions);
        var changes = new FreePOptionsChangeSet(
            before.RecentFilesCap != after.RecentFilesCap,
            !string.Equals(before.DefaultSaveFormat, after.DefaultSaveFormat, StringComparison.Ordinal),
            !string.Equals(before.UiLanguage, after.UiLanguage, StringComparison.Ordinal));
        var steps = Descriptors
            .Where(descriptor => changes.IsChanged(descriptor.Kind))
            .OrderBy(descriptor => descriptor.ApplyOrder)
            .Select(descriptor => new FreePOptionsApplyStep(
                descriptor.Kind,
                descriptor.ApplyOrder,
                descriptor.Activation))
            .ToArray();

        return new FreePOptionsApplyPlan(
            before,
            after,
            changes,
            steps,
            new FreePOptionsSideEffectPlan(
                UpdateRecentFilesPolicy: changes.RecentFilesCapChanged,
                UpdateDefaultSaveFormatPolicy: changes.DefaultSaveFormatChanged,
                RefreshOptionsSummary: changes.Any,
                ApplicationRestart: changes.UiLanguageChanged
                    ? FreePOptionsRestartDecision.Required
                    : FreePOptionsRestartDecision.NotRequired,
                PresentationReload: FreePOptionsPresentationReloadDecision.NotRequired),
            ShouldPersist: true);
    }

    internal static void ApplySnapshot(FreePOptions target, FreePOptionsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(snapshot);

        foreach (var descriptor in Descriptors.OrderBy(descriptor => descriptor.ApplyOrder))
        {
            switch (descriptor.Kind)
            {
                case FreePOptionKind.RecentFilesCap:
                    target.RecentFilesCap = snapshot.RecentFilesCap;
                    break;
                case FreePOptionKind.DefaultSaveFormat:
                    target.DefaultSaveFormat = snapshot.DefaultSaveFormat;
                    break;
                case FreePOptionKind.UiLanguage:
                    target.UiLanguage = snapshot.UiLanguage;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(descriptor.Kind), descriptor.Kind, null);
            }
        }

        target.Normalize();
    }
}

/// <summary>
/// Owns the normalized mutable options instance consumed by a running FreeP shell and guarantees that
/// an accepted edit is applied in descriptor order before the native persistence adapter is invoked.
/// </summary>
public sealed class FreePOptionsRuntimeSession
{
    public FreePOptionsRuntimeSession(FreePOptions liveOptions)
    {
        LiveOptions = FreePOptionsPolicy.NormalizeLoaded(
            liveOptions ?? throw new ArgumentNullException(nameof(liveOptions)));
    }

    public FreePOptions LiveOptions { get; }

    public FreePOptionsApplyPlan PlanApply(FreePOptions editedOptions) =>
        FreePOptionsPolicy.PlanApply(LiveOptions, editedOptions);

    public FreePOptionsApplyPlan Apply(FreePOptions editedOptions)
    {
        var plan = PlanApply(editedOptions);
        FreePOptionsPolicy.ApplySnapshot(LiveOptions, plan.After);
        return plan;
    }

    public FreePOptionsCommitOutcome ApplyAndPersist(
        FreePOptions editedOptions,
        Func<FreePOptions, bool> persist)
    {
        ArgumentNullException.ThrowIfNull(persist);

        var plan = Apply(editedOptions);
        var persisted = !plan.ShouldPersist || persist(LiveOptions);
        return new FreePOptionsCommitOutcome(
            plan,
            PersistenceAttempted: plan.ShouldPersist,
            Persisted: persisted);
    }
}
