using System.Globalization;

namespace Free.Shared.AppServices;

/// <summary>
/// Caches neutral <see cref="StatusBarViewModel"/> instances so repeated status-bar refreshes can reuse
/// formatted readout strings instead of reallocating them. The cache is UI-free: WPF and Avalonia provide
/// text through <see cref="IStatusBarTextProvider"/> and render the resulting model themselves.
/// </summary>
public sealed class StatusBarViewModelCache
{
    private readonly IStatusBarTextProvider _textProvider;

    private CultureInfo? _culture;
    private CultureInfo? _uiCulture;
    private StatusBarViewMode? _lastReadyViewMode;
    private int _lastReadyZoomPercent;
    private string? _lastReadyText;
    private StatusBarViewModel? _lastReadyState;
    private StatusBarViewMode? _lastStatsViewMode;
    private int _lastStatsZoomPercent;
    private WorkbookSelectionStats? _lastStats;
    private StatusBarViewModel? _lastStatsState;

    public StatusBarViewModelCache(IStatusBarTextProvider textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);
        _textProvider = textProvider;
    }

    public StatusBarViewModel GetReady(StatusBarViewMode viewMode, int zoomPercent) =>
        GetReady(viewMode, zoomPercent, _textProvider.GetReadyText());

    public StatusBarViewModel GetReady(StatusBarViewMode viewMode, int zoomPercent, string text)
    {
        RefreshCultureState();

        if (_lastReadyState is { } state &&
            _lastReadyViewMode == viewMode &&
            _lastReadyZoomPercent == zoomPercent &&
            string.Equals(_lastReadyText, text, StringComparison.Ordinal))
        {
            return state;
        }

        state = StatusBarDisplayModelBuilder.Ready(viewMode, zoomPercent, text);
        _lastReadyViewMode = viewMode;
        _lastReadyZoomPercent = zoomPercent;
        _lastReadyText = text;
        _lastReadyState = state;
        return state;
    }

    public StatusBarViewModel GetStats(
        StatusBarViewMode viewMode,
        int zoomPercent,
        WorkbookSelectionStats stats)
    {
        RefreshCultureState();

        if (_lastStatsState is { } state &&
            _lastStatsViewMode == viewMode &&
            _lastStatsZoomPercent == zoomPercent &&
            _lastStats is { } cachedStats &&
            cachedStats == stats)
        {
            return state;
        }

        state = StatusBarDisplayModelBuilder.Stats(viewMode, zoomPercent, stats, _textProvider);
        _lastStatsViewMode = viewMode;
        _lastStatsZoomPercent = zoomPercent;
        _lastStats = stats;
        _lastStatsState = state;
        return state;
    }

    public void Clear()
    {
        _lastReadyViewMode = null;
        _lastReadyZoomPercent = 0;
        _lastReadyText = null;
        _lastReadyState = null;
        _lastStatsViewMode = null;
        _lastStatsZoomPercent = 0;
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
