using System.Windows;

namespace FreeX.App.Host;

internal static class DialogSizing
{
    private const double DefaultWorkAreaFillRatio = 0.92d;
    private static bool _isRegistered;

    public static void RegisterAppDialogSizing()
    {
        if (_isRegistered)
            return;

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplyAutomaticDialogSizing));
        _isRegistered = true;
    }

    public static void ApplyContentHeight(
        Window window,
        double? width = null,
        double minHeight = 0d,
        double? maxHeight = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (width is { } requestedWidth)
        {
            window.Width = requestedWidth;
            window.MinWidth = Math.Max(window.MinWidth, requestedWidth);
        }
        else if (IsFinite(window.Width))
        {
            window.MinWidth = Math.Max(window.MinWidth, window.Width);
        }

        var requestedMinHeight = Math.Max(minHeight, IsFinite(window.Height) ? window.Height : 0d);
        var resolvedMaxHeight = ResolveMaxHeight(window, maxHeight);
        if (IsFinite(resolvedMaxHeight))
        {
            window.MaxHeight = resolvedMaxHeight;
            requestedMinHeight = Math.Min(requestedMinHeight, resolvedMaxHeight);
        }

        if (requestedMinHeight > 0d)
            window.MinHeight = Math.Max(window.MinHeight, requestedMinHeight);

        window.Height = double.NaN;
        window.SizeToContent = SizeToContent.Height;
    }

    internal static bool ShouldApplyAutomaticSizing(Window window)
    {
        var type = window.GetType();
        return window.SizeToContent == SizeToContent.Manual
            && window.ResizeMode == ResizeMode.NoResize
            && string.Equals(type.Namespace, typeof(DialogSizing).Namespace, StringComparison.Ordinal)
            && type.Name.EndsWith("Dialog", StringComparison.Ordinal);
    }

    private static void ApplyAutomaticDialogSizing(object sender, RoutedEventArgs args)
    {
        if (sender is Window window && ShouldApplyAutomaticSizing(window))
            ApplyContentHeight(window);
    }

    private static double ResolveMaxHeight(Window window, double? requestedMaxHeight)
    {
        if (requestedMaxHeight is { } explicitMaxHeight && IsFinite(explicitMaxHeight) && explicitMaxHeight > 0d)
            return explicitMaxHeight;

        if (IsFinite(window.MaxHeight))
            return window.MaxHeight;

        var workAreaHeight = SystemParameters.WorkArea.Height;
        return workAreaHeight > 0d ? Math.Floor(workAreaHeight * DefaultWorkAreaFillRatio) : double.PositiveInfinity;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
