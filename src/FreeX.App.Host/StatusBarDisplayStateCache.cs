using System.Globalization;

namespace FreeX.App.Host;

internal sealed class StatusBarDisplayStateCache
{
    private CultureInfo? _culture;
    private CultureInfo? _uiCulture;
    private string? _lastReadyText;
    private StatusBarDisplayState? _lastReadyState;
    private StatusBarCalculator.Stats? _lastStats;
    private StatusBarDisplayState? _lastStatsState;

    public StatusBarDisplayState GetReady(string text)
    {
        RefreshCultureState();

        if (_lastReadyState is { } state &&
            string.Equals(_lastReadyText, text, StringComparison.Ordinal))
        {
            return state;
        }

        state = StatusBarDisplayState.Ready(text);
        _lastReadyText = text;
        _lastReadyState = state;
        return state;
    }

    public StatusBarDisplayState GetStats(StatusBarCalculator.Stats stats)
    {
        RefreshCultureState();

        if (_lastStatsState is { } state &&
            _lastStats is { } cachedStats &&
            cachedStats == stats)
        {
            return state;
        }

        state = StatusBarDisplayState.Stats(stats);
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
