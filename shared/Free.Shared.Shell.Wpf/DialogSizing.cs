using System.Windows;

namespace Free.Shared.Shell;

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
        // Target the application's own custom "*Dialog" windows. These helpers were extracted into
        // Free.Shared.Shell, but the concrete dialogs still live in the host application's namespace,
        // so we can no longer compare against this helper's own namespace. Instead, exclude framework
        // (System.*) windows — a bare System.Windows.Window's type name is "Window", which already
        // fails the "Dialog" suffix check.
        var typeNamespace = type.Namespace;
        return window.SizeToContent == SizeToContent.Manual
            && window.ResizeMode == ResizeMode.NoResize
            && type.Name.EndsWith("Dialog", StringComparison.Ordinal)
            && typeNamespace is not null
            && !typeNamespace.StartsWith("System", StringComparison.Ordinal);
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
