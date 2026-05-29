using System.Windows;

namespace Freexcel.App.Host;

public static class RibbonMetadata
{
    public static readonly DependencyProperty RoleProperty =
        DependencyProperty.RegisterAttached(
            "Role",
            typeof(RibbonMetadataRole),
            typeof(RibbonMetadata),
            new FrameworkPropertyMetadata(RibbonMetadataRole.None));

    public static readonly DependencyProperty CompactFullWidthProperty =
        DependencyProperty.RegisterAttached(
            "CompactFullWidth",
            typeof(double),
            typeof(RibbonMetadata),
            new FrameworkPropertyMetadata(double.NaN));

    public static readonly DependencyProperty CompactWidthProperty =
        DependencyProperty.RegisterAttached(
            "CompactWidth",
            typeof(double),
            typeof(RibbonMetadata),
            new FrameworkPropertyMetadata(double.NaN));

    public static readonly DependencyProperty CommandContentLayoutProperty =
        DependencyProperty.RegisterAttached(
            "CommandContentLayout",
            typeof(RibbonCommandContentLayout),
            typeof(RibbonMetadata),
            new FrameworkPropertyMetadata(RibbonCommandContentLayout.None));

    public static readonly DependencyProperty GroupNameProperty =
        DependencyProperty.RegisterAttached(
            "GroupName",
            typeof(string),
            typeof(RibbonMetadata),
            new FrameworkPropertyMetadata(""));

    public static RibbonMetadataRole GetRole(DependencyObject element) =>
        (RibbonMetadataRole)element.GetValue(RoleProperty);

    public static void SetRole(DependencyObject element, RibbonMetadataRole value) =>
        element.SetValue(RoleProperty, value);

    public static double GetCompactFullWidth(DependencyObject element) =>
        (double)element.GetValue(CompactFullWidthProperty);

    public static void SetCompactFullWidth(DependencyObject element, double value) =>
        element.SetValue(CompactFullWidthProperty, value);

    public static double GetCompactWidth(DependencyObject element) =>
        (double)element.GetValue(CompactWidthProperty);

    public static void SetCompactWidth(DependencyObject element, double value) =>
        element.SetValue(CompactWidthProperty, value);

    public static RibbonCommandContentLayout GetCommandContentLayout(DependencyObject element) =>
        (RibbonCommandContentLayout)element.GetValue(CommandContentLayoutProperty);

    public static void SetCommandContentLayout(DependencyObject element, RibbonCommandContentLayout value) =>
        element.SetValue(CommandContentLayoutProperty, value);

    public static string GetGroupName(DependencyObject element) =>
        (string)element.GetValue(GroupNameProperty);

    public static void SetGroupName(DependencyObject element, string value) =>
        element.SetValue(GroupNameProperty, value);

    public static void SetCompactWidths(DependencyObject element, double fullWidth, double compactWidth)
    {
        SetCompactFullWidth(element, fullWidth);
        SetCompactWidth(element, compactWidth);
    }

    public static bool TryGetCompactWidths(DependencyObject element, out double fullWidth, out double compactWidth)
    {
        fullWidth = GetCompactFullWidth(element);
        compactWidth = GetCompactWidth(element);
        if (!double.IsNaN(fullWidth) && !double.IsNaN(compactWidth))
            return true;

        fullWidth = 0;
        compactWidth = 0;
        return false;
    }

    public static bool IsCommandLabel(DependencyObject element) =>
        GetRole(element) == RibbonMetadataRole.CommandLabel;

    public static bool IsCommandIcon(DependencyObject element) =>
        GetRole(element) is RibbonMetadataRole.CommandIcon or RibbonMetadataRole.CollapsedChevron;

    public static bool IsCollapsedChevron(DependencyObject element) =>
        GetRole(element) == RibbonMetadataRole.CollapsedChevron;

    public static bool IsCollapsedGroupButton(DependencyObject element) =>
        GetRole(element) == RibbonMetadataRole.CollapsedGroupButton;

    public static bool IsCommandSpacer(DependencyObject element) =>
        GetRole(element) == RibbonMetadataRole.CommandSpacer;

    public static bool IsRibbonGroup(DependencyObject element) =>
        GetRole(element) == RibbonMetadataRole.RibbonGroup;

    public static bool TryGetGroupName(DependencyObject element, out string groupName)
    {
        groupName = GetGroupName(element);
        if (!string.IsNullOrWhiteSpace(groupName))
        {
            groupName = groupName.Trim();
            return true;
        }

        groupName = "";
        return false;
    }

    public static bool TryGetCommandContentLayout(DependencyObject? element, out RibbonCommandContentLayout layout)
    {
        layout = RibbonCommandContentLayout.None;
        if (element is null)
            return false;

        layout = GetCommandContentLayout(element);
        if (layout != RibbonCommandContentLayout.None)
            return true;

        return false;
    }
}

public enum RibbonMetadataRole
{
    None,
    CommandLabel,
    CommandIcon,
    CollapsedGroupButton,
    CollapsedChevron,
    CommandSpacer,
    RibbonGroup
}

public enum RibbonCommandContentLayout
{
    None,
    Small,
    Medium,
    Large,
    IconOnly
}
