using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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

        if (element is FrameworkElement { Tag: string tag })
            return TryParseCompactTag(tag, out fullWidth, out compactWidth);

        fullWidth = 0;
        compactWidth = 0;
        return false;
    }

    public static bool IsCommandLabel(DependencyObject element) =>
        HasRole(element, RibbonMetadataRole.CommandLabel, "RibbonLabel");

    public static bool IsCommandIcon(DependencyObject element) =>
        HasRole(element, RibbonMetadataRole.CommandIcon, "RibbonIcon") ||
        HasRole(element, RibbonMetadataRole.CollapsedChevron, "RibbonIcon");

    public static bool IsCollapsedChevron(DependencyObject element) =>
        GetRole(element) == RibbonMetadataRole.CollapsedChevron ||
        element is TextBlock { Text: "\uE70D", Tag: string tag } &&
        string.Equals(tag, "RibbonIcon", StringComparison.Ordinal);

    public static bool IsCollapsedGroupButton(DependencyObject element) =>
        HasRole(element, RibbonMetadataRole.CollapsedGroupButton, "RibbonCollapsedGroupButton");

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

        if (element is FrameworkElement { Tag: string tag })
        {
            layout = tag switch
            {
                "RibbonCommandContent:S" => RibbonCommandContentLayout.Small,
                "RibbonCommandContent:M" => RibbonCommandContentLayout.Medium,
                "RibbonCommandContent:L" => RibbonCommandContentLayout.Large,
                "RibbonCommandContent" => RibbonCommandContentLayout.IconOnly,
                _ => RibbonCommandContentLayout.None
            };
        }

        return layout != RibbonCommandContentLayout.None;
    }

    private static bool HasRole(DependencyObject element, RibbonMetadataRole role, string legacyTag)
    {
        var metadataRole = GetRole(element);
        if (metadataRole != RibbonMetadataRole.None)
            return metadataRole == role;

        return element is FrameworkElement { Tag: string tag } &&
               string.Equals(tag, legacyTag, StringComparison.Ordinal);
    }

    public static bool TryParseCompactTag(string tag, out double fullWidth, out double compactWidth)
    {
        fullWidth = 0;
        compactWidth = 0;
        const string prefix = "RibbonCompact:";
        if (!tag.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var parts = tag[prefix.Length..].Split(':');
        return parts.Length == 2 &&
               double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out fullWidth) &&
               double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out compactWidth);
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
