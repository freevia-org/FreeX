using System.Globalization;

namespace FreeX.App.Host;

internal sealed class StatusBarDisplayStateCache
{
    private CultureInfo? _culture;
    private CultureInfo? _uiCulture;
    private readonly Dictionary<int, string> _countTexts = [];
    private readonly Dictionary<int, string> _numericalCountTexts = [];
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

        state = StatusBarDisplayState.Stats(
            stats,
            GetOrCreateCountText(stats.Count),
            GetOrCreateNumericalCountText(stats.NumericalCount));
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
        _countTexts.Clear();
        _numericalCountTexts.Clear();
    }

    private string GetOrCreateCountText(int count)
    {
        if (_countTexts.TryGetValue(count, out var text))
            return text;

        text = string.Format(CultureInfo.CurrentCulture, UiText.Get("StatusBar_CountFormat"), count);
        _countTexts[count] = text;
        return text;
    }

    private string GetOrCreateNumericalCountText(int count)
    {
        if (_numericalCountTexts.TryGetValue(count, out var text))
            return text;

        text = string.Format(CultureInfo.CurrentCulture, UiText.Get("StatusBar_NumericalCountFormat"), count);
        _numericalCountTexts[count] = text;
        return text;
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
