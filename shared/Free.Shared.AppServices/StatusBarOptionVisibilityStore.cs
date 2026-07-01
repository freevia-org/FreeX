namespace Free.Shared.AppServices;

public interface IStatusBarOptionVisibilityStore
{
    bool StatusBarShowCellMode { get; set; }
    bool StatusBarShowEndMode { get; set; }
    bool StatusBarShowSelectionMode { get; set; }
    bool StatusBarShowPageNumber { get; set; }
    bool StatusBarShowAverage { get; set; }
    bool StatusBarShowCount { get; set; }
    bool StatusBarShowNumericalCount { get; set; }
    bool StatusBarShowMinimum { get; set; }
    bool StatusBarShowMaximum { get; set; }
    bool StatusBarShowSum { get; set; }
    bool StatusBarShowViewShortcuts { get; set; }
    bool StatusBarShowZoom { get; set; }
    bool StatusBarShowZoomSlider { get; set; }
}

public static class StatusBarOptionVisibilityStore
{
    public static StatusBarOptionVisibility ToVisibility(IStatusBarOptionVisibilityStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        return new StatusBarOptionVisibility(
            CellMode: store.StatusBarShowCellMode,
            EndMode: store.StatusBarShowEndMode,
            SelectionMode: store.StatusBarShowSelectionMode,
            PageNumber: store.StatusBarShowPageNumber,
            Average: store.StatusBarShowAverage,
            Count: store.StatusBarShowCount,
            NumericalCount: store.StatusBarShowNumericalCount,
            Minimum: store.StatusBarShowMinimum,
            Maximum: store.StatusBarShowMaximum,
            Sum: store.StatusBarShowSum,
            ViewShortcuts: store.StatusBarShowViewShortcuts,
            Zoom: store.StatusBarShowZoom,
            ZoomSlider: store.StatusBarShowZoomSlider);
    }

    public static void ApplyVisibility(
        IStatusBarOptionVisibilityStore store,
        StatusBarOptionVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(store);

        store.StatusBarShowCellMode = visibility.CellMode;
        store.StatusBarShowEndMode = visibility.EndMode;
        store.StatusBarShowSelectionMode = visibility.SelectionMode;
        store.StatusBarShowPageNumber = visibility.PageNumber;
        store.StatusBarShowAverage = visibility.Average;
        store.StatusBarShowCount = visibility.Count;
        store.StatusBarShowNumericalCount = visibility.NumericalCount;
        store.StatusBarShowMinimum = visibility.Minimum;
        store.StatusBarShowMaximum = visibility.Maximum;
        store.StatusBarShowSum = visibility.Sum;
        store.StatusBarShowViewShortcuts = visibility.ViewShortcuts;
        store.StatusBarShowZoom = visibility.Zoom;
        store.StatusBarShowZoomSlider = visibility.ZoomSlider;
    }

    public static bool TrySetOption(
        IStatusBarOptionVisibilityStore store,
        string optionTag,
        bool isVisible)
    {
        ArgumentNullException.ThrowIfNull(store);

        switch (optionTag)
        {
            case StatusBarOptionTags.CellMode:
                store.StatusBarShowCellMode = isVisible;
                return true;
            case StatusBarOptionTags.EndMode:
                store.StatusBarShowEndMode = isVisible;
                return true;
            case StatusBarOptionTags.SelectionMode:
                store.StatusBarShowSelectionMode = isVisible;
                return true;
            case StatusBarOptionTags.PageNumber:
                store.StatusBarShowPageNumber = isVisible;
                return true;
            case StatusBarOptionTags.Average:
                store.StatusBarShowAverage = isVisible;
                return true;
            case StatusBarOptionTags.Count:
                store.StatusBarShowCount = isVisible;
                return true;
            case StatusBarOptionTags.NumericalCount:
                store.StatusBarShowNumericalCount = isVisible;
                return true;
            case StatusBarOptionTags.Minimum:
                store.StatusBarShowMinimum = isVisible;
                return true;
            case StatusBarOptionTags.Maximum:
                store.StatusBarShowMaximum = isVisible;
                return true;
            case StatusBarOptionTags.Sum:
                store.StatusBarShowSum = isVisible;
                return true;
            case StatusBarOptionTags.ViewShortcuts:
                store.StatusBarShowViewShortcuts = isVisible;
                return true;
            case StatusBarOptionTags.Zoom:
                store.StatusBarShowZoom = isVisible;
                return true;
            case StatusBarOptionTags.ZoomSlider:
                store.StatusBarShowZoomSlider = isVisible;
                return true;
            default:
                return false;
        }
    }
}
