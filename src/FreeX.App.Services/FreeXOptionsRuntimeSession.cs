namespace FreeX.App.Services;

public sealed record FreeXOptionsPersistenceResult(
    bool IsPersisted,
    AppOptions Options,
    string? PersistenceError)
{
    public bool Succeeded => IsPersisted;
}

/// <summary>
/// Owns the application-options snapshot used by a running FreeX shell. Both renderers use this
/// session for initialization, reload-before-mutate persistence, dialog commits, and adoption of
/// the newly persisted snapshot.
/// </summary>
public sealed class FreeXOptionsRuntimeSession
{
    private readonly Func<AppOptions> _load;
    private readonly Func<AppOptions, bool> _save;

    public FreeXOptionsRuntimeSession(
        AppOptions? initialOptions = null,
        Func<AppOptions>? load = null,
        Func<AppOptions, bool>? save = null)
    {
        _load = load ?? AppOptionsStore.Load;
        _save = save ?? AppOptionsStore.Save;
        LiveOptions = initialOptions ?? Normalize(LoadFromStore());
    }

    public AppOptions LiveOptions { get; private set; }

    public AppOptions Reload() => Adopt(LoadFromStore());

    public AppOptions Adopt(AppOptions options)
    {
        options = Normalize(options);
        if (!ReferenceEquals(LiveOptions, options))
            LiveOptions.CopyFrom(options);
        return LiveOptions;
    }

    public FreeXOptionsDialogSession BeginDialog(AppOptions? openSnapshot = null) =>
        new(this, (openSnapshot ?? Reload()).Clone());

    public FreeXOptionsPersistenceResult CommitDialog(
        AppOptions openSnapshot,
        AppOptions editedOptions)
    {
        ArgumentNullException.ThrowIfNull(openSnapshot);
        ArgumentNullException.ThrowIfNull(editedOptions);

        var merged = OptionsDialogPlanner.MergeOntoFreshLoad(
            LoadFromStore(),
            openSnapshot,
            editedOptions);
        return SaveAndAdopt(merged);
    }

    public FreeXOptionsPersistenceResult MutateFresh(Action<AppOptions> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        var options = LoadFromStore();
        mutation(options);
        options = Normalize(options);
        var persisted = _save(options);
        var adopted = Adopt(options);
        return new FreeXOptionsPersistenceResult(
            persisted,
            adopted,
            persisted ? null : adopted.LastPersistenceError);
    }

    public FreeXOptionsPersistenceResult SaveAndAdopt(AppOptions options)
    {
        options = Normalize(options);
        var persisted = _save(options);
        var resultOptions = options;
        if (persisted)
            resultOptions = Adopt(options);

        return new FreeXOptionsPersistenceResult(
            persisted,
            resultOptions,
            persisted ? null : options.LastPersistenceError);
    }

    private AppOptions LoadFromStore() =>
        _load() ?? throw new InvalidOperationException("The FreeX options store returned no options snapshot.");

    private static AppOptions Normalize(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Normalize();
        return options;
    }
}
