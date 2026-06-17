using System.Windows;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Attached metadata that the <see cref="RibbonWpfRenderer"/> stamps onto rendered controls
/// (command name, catalog id, role). Platform-neutral except for the WPF dependency-property
/// plumbing — ported verbatim from FreeX's app-neutral helper so a second app can reuse it.
/// </summary>
public static class RibbonMetadata
{
    public static readonly DependencyProperty RoleProperty =
        DependencyProperty.RegisterAttached(
            "Role",
            typeof(RibbonMetadataRole),
            typeof(RibbonMetadata),
            new FrameworkPropertyMetadata(RibbonMetadataRole.None));

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

    public static RibbonMetadataRole GetRole(DependencyObject element) =>
        (RibbonMetadataRole)element.GetValue(RoleProperty);

    public static void SetRole(DependencyObject element, RibbonMetadataRole value) =>
        element.SetValue(RoleProperty, value);

    public static string GetGroupName(DependencyObject element) =>
        (string)element.GetValue(GroupNameProperty);

    public static void SetGroupName(DependencyObject element, string value) =>
        element.SetValue(GroupNameProperty, value);

    public static string GetCommandName(DependencyObject element) =>
        (string)element.GetValue(CommandNameProperty);

    public static void SetCommandName(DependencyObject element, string value) =>
        element.SetValue(CommandNameProperty, value);

    public static string GetCatalogId(DependencyObject element) =>
        (string)element.GetValue(CatalogIdProperty);

    public static void SetCatalogId(DependencyObject element, string value) =>
        element.SetValue(CatalogIdProperty, value);

    public static bool IsCollapsedGroupButton(DependencyObject element) =>
        GetRole(element) == RibbonMetadataRole.CollapsedGroupButton;

    public static bool IsRibbonGroup(DependencyObject element) =>
        GetRole(element) == RibbonMetadataRole.RibbonGroup;
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
