using System.Windows;
using SharedRibbonCommandContentLayout = Free.Shared.Ribbon.Wpf.RibbonCommandContentLayout;
using SharedRibbonMetadata = Free.Shared.Ribbon.Wpf.RibbonMetadata;
using SharedRibbonMetadataRole = Free.Shared.Ribbon.Wpf.RibbonMetadataRole;

namespace FreeX.App.Host;

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

    public static readonly DependencyProperty CommandNameProperty =
        DependencyProperty.RegisterAttached(
            "CommandName",
            typeof(string),
            typeof(RibbonMetadata),
            new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty CatalogIdProperty =
        DependencyProperty.RegisterAttached(
            "CatalogId",
            typeof(string),
            typeof(RibbonMetadata),
            new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty DropdownMenuButtonProperty =
        DependencyProperty.RegisterAttached(
            "DropdownMenuButton",
            typeof(bool),
            typeof(RibbonMetadata),
            new FrameworkPropertyMetadata(false));

    public static readonly RoutedEvent DropdownClickEvent =
        EventManager.RegisterRoutedEvent(
            "DropdownClick",
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(RibbonMetadata));

    public static readonly DependencyProperty DropdownZoneHandlerAttachedProperty =
        DependencyProperty.RegisterAttached(
            "DropdownZoneHandlerAttached",
            typeof(bool),
            typeof(RibbonMetadata),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty DropdownZoneHighlightAttachedProperty =
        DependencyProperty.RegisterAttached(
            "DropdownZoneHighlightAttached",
            typeof(bool),
            typeof(RibbonMetadata),
            new FrameworkPropertyMetadata(false));

    public static RibbonMetadataRole GetRole(DependencyObject element)
    {
        var role = (RibbonMetadataRole)element.GetValue(RoleProperty);
        return role == RibbonMetadataRole.None ? MapRole(SharedRibbonMetadata.GetRole(element)) : role;
    }

    public static void SetRole(DependencyObject element, RibbonMetadataRole value) =>
        element.SetValue(RoleProperty, value);

    public static double GetCompactFullWidth(DependencyObject element)
    {
        var width = (double)element.GetValue(CompactFullWidthProperty);
        return double.IsNaN(width) ? SharedRibbonMetadata.GetCompactFullWidth(element) : width;
    }

    public static void SetCompactFullWidth(DependencyObject element, double value) =>
        element.SetValue(CompactFullWidthProperty, value);

    public static double GetCompactWidth(DependencyObject element)
    {
        var width = (double)element.GetValue(CompactWidthProperty);
        return double.IsNaN(width) ? SharedRibbonMetadata.GetCompactWidth(element) : width;
    }

    public static void SetCompactWidth(DependencyObject element, double value) =>
        element.SetValue(CompactWidthProperty, value);

    public static RibbonCommandContentLayout GetCommandContentLayout(DependencyObject element)
    {
        var layout = (RibbonCommandContentLayout)element.GetValue(CommandContentLayoutProperty);
        return layout == RibbonCommandContentLayout.None
            ? MapCommandContentLayout(SharedRibbonMetadata.GetCommandContentLayout(element))
            : layout;
    }

    public static void SetCommandContentLayout(DependencyObject element, RibbonCommandContentLayout value) =>
        element.SetValue(CommandContentLayoutProperty, value);

    public static string GetGroupName(DependencyObject element)
    {
        var value = (string)element.GetValue(GroupNameProperty);
        return string.IsNullOrWhiteSpace(value) ? SharedRibbonMetadata.GetGroupName(element) : value;
    }

    public static void SetGroupName(DependencyObject element, string value) =>
        element.SetValue(GroupNameProperty, value);

    public static string GetCommandName(DependencyObject element)
    {
        var value = (string)element.GetValue(CommandNameProperty);
        return string.IsNullOrWhiteSpace(value) ? SharedRibbonMetadata.GetCommandName(element) : value;
    }

    public static void SetCommandName(DependencyObject element, string value) =>
        element.SetValue(CommandNameProperty, value);

    public static string GetCatalogId(DependencyObject element)
    {
        var value = (string)element.GetValue(CatalogIdProperty);
        return string.IsNullOrWhiteSpace(value) ? SharedRibbonMetadata.GetCatalogId(element) : value;
    }

    public static void SetCatalogId(DependencyObject element, string value) =>
        element.SetValue(CatalogIdProperty, value);

    public static bool GetDropdownMenuButton(DependencyObject element) =>
        (bool)element.GetValue(DropdownMenuButtonProperty) ||
        SharedRibbonMetadata.GetDropdownMenuButton(element);

    public static void SetDropdownMenuButton(DependencyObject element, bool value) =>
        element.SetValue(DropdownMenuButtonProperty, value);

    public static void AddDropdownClickHandler(DependencyObject element, RoutedEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.AddHandler(DropdownClickEvent, handler);
    }

    public static void RemoveDropdownClickHandler(DependencyObject element, RoutedEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.RemoveHandler(DropdownClickEvent, handler);
    }

    public static bool GetDropdownZoneHandlerAttached(DependencyObject element) =>
        (bool)element.GetValue(DropdownZoneHandlerAttachedProperty) ||
        SharedRibbonMetadata.GetDropdownZoneHandlerAttached(element);

    public static void SetDropdownZoneHandlerAttached(DependencyObject element, bool value) =>
        element.SetValue(DropdownZoneHandlerAttachedProperty, value);

    public static bool GetDropdownZoneHighlightAttached(DependencyObject element) =>
        (bool)element.GetValue(DropdownZoneHighlightAttachedProperty) ||
        SharedRibbonMetadata.GetDropdownZoneHighlightAttached(element);

    public static void SetDropdownZoneHighlightAttached(DependencyObject element, bool value) =>
        element.SetValue(DropdownZoneHighlightAttachedProperty, value);

    public static void SetCompactWidths(DependencyObject element, double fullWidth, double compactWidth)
    {
        SetCompactFullWidth(element, fullWidth);
        SetCompactWidth(element, compactWidth);
    }

    public static bool TryGetCompactWidths(DependencyObject element, out double fullWidth, out double compactWidth)
    {
        fullWidth = GetCompactFullWidth(element);
        compactWidth = GetCompactWidth(element);
        if (double.IsFinite(fullWidth) &&
            double.IsFinite(compactWidth) &&
            fullWidth > 0 &&
            compactWidth > 0 &&
            compactWidth <= fullWidth)
        {
            return true;
        }

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

    public static bool IsDropdownChevron(DependencyObject element) =>
        GetRole(element) == RibbonMetadataRole.DropdownChevron;

    public static bool IsDropdownMenuButton(DependencyObject element) =>
        GetDropdownMenuButton(element);

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

    public static bool TryGetCommandName(DependencyObject element, out string commandName)
    {
        commandName = GetCommandName(element);
        if (!string.IsNullOrWhiteSpace(commandName))
        {
            commandName = commandName.Trim();
            return true;
        }

        commandName = "";
        return false;
    }

    public static bool TryGetCatalogId(DependencyObject element, out string catalogId)
    {
        catalogId = GetCatalogId(element);
        if (!string.IsNullOrWhiteSpace(catalogId))
        {
            catalogId = catalogId.Trim();
            return true;
        }

        catalogId = "";
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

    private static RibbonMetadataRole MapRole(SharedRibbonMetadataRole role) =>
        role switch
        {
            SharedRibbonMetadataRole.CommandLabel => RibbonMetadataRole.CommandLabel,
            SharedRibbonMetadataRole.CommandIcon => RibbonMetadataRole.CommandIcon,
            SharedRibbonMetadataRole.CollapsedGroupButton => RibbonMetadataRole.CollapsedGroupButton,
            SharedRibbonMetadataRole.CollapsedChevron => RibbonMetadataRole.CollapsedChevron,
            SharedRibbonMetadataRole.CommandSpacer => RibbonMetadataRole.CommandSpacer,
            SharedRibbonMetadataRole.RibbonGroup => RibbonMetadataRole.RibbonGroup,
            SharedRibbonMetadataRole.DropdownChevron => RibbonMetadataRole.DropdownChevron,
            _ => RibbonMetadataRole.None
        };

    private static RibbonCommandContentLayout MapCommandContentLayout(SharedRibbonCommandContentLayout layout) =>
        layout switch
        {
            SharedRibbonCommandContentLayout.Small => RibbonCommandContentLayout.Small,
            SharedRibbonCommandContentLayout.Medium => RibbonCommandContentLayout.Medium,
            SharedRibbonCommandContentLayout.Large => RibbonCommandContentLayout.Large,
            SharedRibbonCommandContentLayout.IconOnly => RibbonCommandContentLayout.IconOnly,
            _ => RibbonCommandContentLayout.None
        };
}

public enum RibbonMetadataRole
{
    None,
    CommandLabel,
    CommandIcon,
    CollapsedGroupButton,
    CollapsedChevron,
    CommandSpacer,
    RibbonGroup,
    DropdownChevron
}

public enum RibbonCommandContentLayout
{
    None,
    Small,
    Medium,
    Large,
    IconOnly
}
