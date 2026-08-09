using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// WPF-specific discovery and measurement for adaptive ribbon surfaces. Product profiles still own
/// state planning; this type owns only renderer mechanics that are identical for every WPF host.
/// </summary>
public static class RibbonAdaptiveWpfSurface
{
    private const double FitTolerance = 4;

    public static StackPanel? FindLegacyAdaptivePanel(DependencyObject contentRoot)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);

        var visitedPanels = new HashSet<StackPanel>();
        StackPanel? activePanel = null;
        var activePanelRibbonGroupCount = -1;

        foreach (var descendant in EnumerateVisualDescendants(contentRoot)
                     .Concat(EnumerateLogicalDescendants(contentRoot)))
        {
            if (descendant is not StackPanel panel ||
                !visitedPanels.Add(panel) ||
                FindTreeAncestor<Button>(panel) is { } button &&
                RibbonMetadata.IsCollapsedGroupButton(button))
            {
                continue;
            }

            var ribbonGroupCount = CountRibbonGroupChildren(panel);
            if (panel.Orientation != Orientation.Horizontal ||
                ribbonGroupCount == 0 ||
                ribbonGroupCount <= activePanelRibbonGroupCount)
            {
                continue;
            }

            activePanel = panel;
            activePanelRibbonGroupCount = ribbonGroupCount;
        }

        return activePanel;
    }

    public static IReadOnlyList<FrameworkElement> GetAdaptiveGroups(StackPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        return panel.Children
            .OfType<FrameworkElement>()
            .Where(element =>
                !RibbonMetadata.IsCollapsedGroupButton(element) &&
                RibbonMetadata.IsRibbonGroup(element))
            .ToList();
    }

    public static double ResolveAvailableWidth(
        StackPanel panel,
        ScrollViewer? scrollViewer,
        double ribbonTabsWidth)
    {
        ArgumentNullException.ThrowIfNull(panel);

        double? availableWidth = scrollViewer?.ActualWidth > 0
            ? scrollViewer.ActualWidth
            : scrollViewer?.ViewportWidth;
        if (availableWidth is null or <= 0)
            availableWidth = ribbonTabsWidth > 0 ? ribbonTabsWidth : panel.ActualWidth;
        if (ribbonTabsWidth > 0)
            availableWidth = Math.Min(availableWidth.Value, Math.Max(0, ribbonTabsWidth - 12));

        return Math.Max(0, availableWidth ?? 0);
    }

    public static double ResolveMeasuredAvailableWidth(
        FrameworkElement element,
        double measuredWidth)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (!double.IsInfinity(measuredWidth))
            return measuredWidth;

        if (element.ActualWidth > 0)
            return element.ActualWidth;

        var current = VisualTreeHelper.GetParent(element);
        while (current is not null)
        {
            if (current is ScrollViewer { ViewportWidth: > 0 } scrollViewer)
                return scrollViewer.ViewportWidth;

            current = VisualTreeHelper.GetParent(current);
        }

        return double.PositiveInfinity;
    }

    public static double MeasureFixedChromeWidth(StackPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        var fixedWidth = 0.0;
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            if (child.Visibility != Visibility.Visible ||
                child is Grid ||
                RibbonMetadata.IsCollapsedGroupButton(child))
            {
                continue;
            }

            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            fixedWidth += child.DesiredSize.Width;
        }

        return fixedWidth;
    }

    public static bool MeasureOverflows(StackPanel panel, double availableWidth)
    {
        ArgumentNullException.ThrowIfNull(panel);

        panel.InvalidateMeasure();
        panel.UpdateLayout();
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return panel.DesiredSize.Width > Math.Max(0, availableWidth - FitTolerance);
    }

    public static string CreateMeasurementCacheKey(
        string tabIdentity,
        IReadOnlyList<FrameworkElement> groups,
        Func<FrameworkElement, string> groupNameResolver,
        Func<FrameworkElement, string?> groupCatalogIdResolver)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(groupNameResolver);
        ArgumentNullException.ThrowIfNull(groupCatalogIdResolver);

        return string.Join(
            "|",
            tabIdentity,
            groups.Count.ToString(CultureInfo.InvariantCulture),
            string.Join(
                ";",
                groups.Select(group =>
                    $"{groupNameResolver(group)}:{groupCatalogIdResolver(group)}:{group.GetHashCode():X}")));
    }

    public static IReadOnlyList<string> CreateGroupProfileKeys(IReadOnlyList<RibbonAdaptiveGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var keys = new string[groups.Count];
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            keys[index] = string.IsNullOrWhiteSpace(group.CatalogId)
                ? group.Name
                : group.CatalogId!;
        }

        return keys;
    }

    public static bool StatesAreMoreCollapsedThan(
        IReadOnlyList<RibbonAdaptiveGroupState> states,
        IReadOnlyList<RibbonAdaptiveGroupState> baselineStates)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(baselineStates);

        var count = Math.Min(states.Count, baselineStates.Count);
        for (var index = 0; index < count; index++)
        {
            if ((int)states[index] > (int)baselineStates[index])
                return true;
        }

        return false;
    }

    public static int RoundWidthToTenths(double width) =>
        (int)Math.Round(Math.Max(0, width) * 10, MidpointRounding.ToEven);

    public static RibbonAdaptiveWpfLayoutPlanKey CreateLayoutPlanKey(
        double availableWidth,
        double fixedChromeWidth,
        string? selectedTabHeader,
        RibbonCollapsedGroupFootprintMode footprintMode) =>
        new(
            RoundWidthToTenths(availableWidth),
            RoundWidthToTenths(fixedChromeWidth),
            selectedTabHeader ?? "",
            footprintMode);

    public static RibbonAdaptiveWpfAppliedStateKey CreateAppliedStateKey(
        RibbonCollapsedGroupFootprintMode footprintMode,
        bool wideIconOnlyLabelMode,
        IReadOnlyList<RibbonAdaptiveGroupState> states) =>
        new(footprintMode, wideIconOnlyLabelMode, CreateStateSignature(states));

    public static RibbonAdaptiveWpfCorrectionKey CreateCorrectionKey(
        string measurementCacheKey,
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroupState> states) =>
        new(
            measurementCacheKey,
            RoundWidthToTenths(availableWidth),
            CreateStateSignature(states));

    public static RibbonAdaptiveWpfMeasuredOverflowKey CreateMeasuredOverflowKey(
        string measurementCacheKey,
        double availableWidth,
        RibbonCollapsedGroupFootprintMode footprintMode,
        IReadOnlyList<RibbonAdaptiveGroupState> states) =>
        new(
            measurementCacheKey,
            RoundWidthToTenths(availableWidth),
            footprintMode,
            CreateStateSignature(states));

    public static RibbonAdaptiveWpfStateSignature CreateStateSignature(
        IReadOnlyList<RibbonAdaptiveGroupState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        ulong low = 0;
        ulong high = 0;
        var count = states.Count;
        var packedCount = Math.Min(count, 64);
        for (var index = 0; index < packedCount; index++)
        {
            var value = ((ulong)states[index]) & 0x3UL;
            if (index < 32)
                low |= value << (index * 2);
            else
                high |= value << ((index - 32) * 2);
        }

        string? overflow = null;
        if (count > 64)
        {
            var builder = new System.Text.StringBuilder(count - 64);
            for (var index = 64; index < count; index++)
                builder.Append((char)('0' + (int)states[index]));
            overflow = builder.ToString();
        }

        return new RibbonAdaptiveWpfStateSignature(count, low, high, overflow);
    }

    public static IEnumerable<DependencyObject> EnumerateSelfVisualAndLogicalDescendants(DependencyObject root) =>
        [root, .. EnumerateVisualDescendants(root), .. EnumerateLogicalDescendants(root)];

    public static T? FindVisualAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
                return match;
        }

        return null;
    }

    public static Border? FindDirectCommandIconSlot(Panel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        foreach (var child in panel.Children)
        {
            if (child is Border border && RibbonMetadata.IsCommandIcon(border))
                return border;
        }

        return null;
    }

    public static TextBlock? FindDirectCommandLabel(Panel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        foreach (var child in panel.Children)
        {
            if (child is TextBlock textBlock && RibbonMetadata.IsCommandLabel(textBlock))
                return textBlock;
        }

        return null;
    }

    public static bool IsRibbonButtonLabel(TextBlock textBlock)
    {
        ArgumentNullException.ThrowIfNull(textBlock);

        if (RibbonMetadata.IsCommandLabel(textBlock))
            return true;
        if (RibbonMetadata.IsCommandIcon(textBlock))
            return false;

        var text = textBlock.Text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length <= 1)
            return false;

        var fontFamily = textBlock.FontFamily?.Source ?? "";
        if (fontFamily.Contains("MDL2", StringComparison.OrdinalIgnoreCase) ||
            fontFamily.Contains("Symbol", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return FindVisualAncestor<ButtonBase>(textBlock) is not null;
    }

    private static int CountRibbonGroupChildren(StackPanel panel)
    {
        var count = 0;
        foreach (var child in panel.Children)
        {
            if (child is DependencyObject dependencyObject &&
                RibbonMetadata.IsRibbonGroup(dependencyObject))
            {
                count++;
            }
        }

        return count;
    }

    private static T? FindTreeAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var current = element; current is not null; current = GetTreeParent(current))
        {
            if (current is T match)
                return match;
        }

        return null;
    }

    private static DependencyObject? GetTreeParent(DependencyObject element)
    {
        if (element is Visual && VisualTreeHelper.GetParent(element) is { } visualParent)
            return visualParent;

        return LogicalTreeHelper.GetParent(element);
    }

    public static IEnumerable<DependencyObject> EnumerateVisualDescendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;

            foreach (var descendant in EnumerateVisualDescendants(child))
                yield return descendant;
        }
    }

    public static IEnumerable<DependencyObject> EnumerateLogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
                continue;

            yield return dependencyObject;
            foreach (var descendant in EnumerateLogicalDescendants(dependencyObject))
                yield return descendant;
        }
    }
}

public readonly record struct RibbonAdaptiveWpfLayoutPlanKey(
    int AvailableWidthTenths,
    int FixedChromeWidthTenths,
    string SelectedTabHeader,
    RibbonCollapsedGroupFootprintMode FootprintMode);

public readonly record struct RibbonAdaptiveWpfStateSignature(
    int Count,
    ulong Low,
    ulong High,
    string? Overflow);

public readonly record struct RibbonAdaptiveWpfAppliedStateKey(
    RibbonCollapsedGroupFootprintMode FootprintMode,
    bool WideIconOnlyLabelMode,
    RibbonAdaptiveWpfStateSignature States);

public readonly record struct RibbonAdaptiveWpfCorrectionKey(
    string MeasurementCacheKey,
    int AvailableWidthTenths,
    RibbonAdaptiveWpfStateSignature States);

public readonly record struct RibbonAdaptiveWpfMeasuredOverflowKey(
    string MeasurementCacheKey,
    int AvailableWidthTenths,
    RibbonCollapsedGroupFootprintMode FootprintMode,
    RibbonAdaptiveWpfStateSignature States);
