using System.Globalization;
using Free.Shared.AppServices;

namespace FreeX.App.Host;

/// <summary>
/// Caches the neutral <see cref="StatusBarViewModel"/> produced by
/// <see cref="StatusBarDisplayModelBuilder"/> so repeated navigation refreshes reuse the
/// formatted readout strings instead of reallocating them. Replaces the former
/// WPF-coupled <c>StatusBarDisplayStateCache</c>; the resulting model carries plain strings
/// and <c>bool</c> visibility, ready for any shell to render.
/// </summary>
internal sealed class StatusBarViewModelCache
{
    private readonly IStatusBarTextProvider _textProvider;

    private CultureInfo? _culture;
    private CultureInfo? _uiCulture;
    private string? _lastReadyText;
    private StatusBarViewModel? _lastReadyState;
    private StatusBarCalculator.Stats? _lastStats;
    private StatusBarViewModel? _lastStatsState;

    public StatusBarViewModelCache(IStatusBarTextProvider textProvider)
    {
        _textProvider = textProvider;
    }

    public StatusBarViewModel GetReady(string text)
    {
        RefreshCultureState();

        if (_lastReadyState is { } state &&
            string.Equals(_lastReadyText, text, StringComparison.Ordinal))
        {
            return state;
        }

        state = StatusBarDisplayModelBuilder.Ready(StatusBarViewMode.Normal, zoomPercent: 0, text);
        _lastReadyText = text;
        _lastReadyState = state;
        return state;
    }

    public StatusBarViewModel GetStats(StatusBarCalculator.Stats stats)
    {
        RefreshCultureState();

        if (_lastStatsState is { } state &&
            _lastStats is { } cachedStats &&
            cachedStats == stats)
        {
            return state;
        }

        state = StatusBarDisplayModelBuilder.Stats(
            StatusBarViewMode.Normal,
            zoomPercent: 0,
            StatusBarCalculator.ToShared(stats),
            _textProvider);
        _lastStats = stats;
        _lastStatsState = state;
        return state;
    }

    public void Clear()
    {
        _lastReadyText = null;
        _lastReadyState = null;
        _lastStats = null;
        _lastStatsState = null;
    }

    private void RefreshCultureState()
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        if (ReferenceEquals(_culture, culture) && ReferenceEquals(_uiCulture, uiCulture))
            return;

        _culture = culture;
        _uiCulture = uiCulture;
        Clear();
    }
}
