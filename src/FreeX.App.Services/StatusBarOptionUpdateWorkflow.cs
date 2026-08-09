using Free.Shared.AppServices;

namespace FreeX.App.Services;

public sealed record StatusBarOptionUpdateResult(
    bool IsRecognized,
    bool IsPersisted,
    StatusBarOptionVisibility Visibility,
    string? PersistenceError)
{
    public bool Succeeded => IsRecognized && IsPersisted;
}

/// <summary>
/// Owns the status-bar customization mutation and persistence ceremony shared by the WPF and
/// Avalonia shells. Renderers retain menu construction, error presentation, and live refresh.
/// </summary>
public static class StatusBarOptionUpdateWorkflow
{
    public static StatusBarOptionUpdateResult ApplyAndSave(
        AppOptions options,
        string optionTag,
        bool isVisible,
        Func<AppOptions, bool>? save = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        save ??= AppOptionsStore.Save;

        if (!StatusBarOptionVisibilityStore.TrySetOption(options, optionTag, isVisible))
        {
            return new StatusBarOptionUpdateResult(
                IsRecognized: false,
                IsPersisted: false,
                StatusBarOptionVisibilityStore.ToVisibility(options),
                PersistenceError: null);
        }

        var persisted = save(options);
        return new StatusBarOptionUpdateResult(
            IsRecognized: true,
            IsPersisted: persisted,
            StatusBarOptionVisibilityStore.ToVisibility(options),
            persisted ? null : options.LastPersistenceError);
    }

    public static StatusBarOptionUpdateResult ApplyToFreshOptionsAndSave(
        string optionTag,
        bool isVisible,
        Func<AppOptions>? load = null,
        Func<AppOptions, bool>? save = null)
    {
        var options = load is null ? AppOptionsStore.Load() : load();
        return ApplyAndSave(options, optionTag, isVisible, save);
    }
}
