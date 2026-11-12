using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Ribbon.KeyTips;
using FreeX.App.Services.Ribbon;
using FreeX.Ribbon.Definitions;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using AvaloniaShape = Avalonia.Controls.Shapes.Shape;

namespace FreeX.App.Avalonia;

/// <summary>
/// Gives an Avalonia <see cref="ContextMenu"/> the chrome of the WPF host's context menus, which
/// are the system menu template restyled by <c>MainWindowResources.xaml</c> (white surface,
/// #C8C8C8 border, 12px items) and decorated at open time by <c>RibbonMenuIconSeeder</c> (command
/// icons resolved from the item caption) and <c>MenuKeyTipAssigner</c> (a key letter in the
/// shortcut column). Fluent's defaults are 14px rows about 36px tall, inset rounded hover pills,
/// no icons and a chevron submenu arrow.
/// </summary>
internal static class AvaloniaContextMenuChrome
{
    // WPF: <Style TargetType="MenuItem"> FontSize=12.
    internal const double FontSize = 12;

    // The WPF rows measure 22-24px (12px text, 18px icon); Fluent's 16px icon slot plus this
    // padding gives the same pitch.
    internal const double ItemMinHeight = 23;
    internal const double IconSize = 16;
    internal static readonly Thickness ItemPadding = new(8, 2, 8, 2);

    // WPF separators start at the caption column, not at the menu edge:
    // item padding + icon slot + Fluent's 12px icon gap.
    private static readonly Thickness IconColumnSeparatorMargin = new(36, 2, 0, 2);
    private static readonly Thickness PlainSeparatorMargin = new(0, 2, 0, 2);

    private const string MenuClass = "freex-context-menu";
    private const string ItemClass = "freex-context-menu-item";

    // WPF: ContextMenu BorderBrush #C8C8C8 on a white surface.
    private static readonly IBrush MenuBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));

    // The system menu highlight (#3D26A0DA over white).
    private static readonly IBrush HighlightBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xCB, 0xE8, 0xF6));

    // The system submenu arrow: a small filled triangle in menu ink.
    private static readonly IBrush SubmenuArrowBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
    // Parsed per menu: a Geometry belongs to the UI thread that built it.
    private const string SubmenuArrowPathData = "M 2,4.5 L 6,8 L 2,11.5 Z";

    /// <summary>
    /// Applies the WPF menu chrome to <paramref name="menu"/> and to the items it currently holds.
    /// Safe to call again after the items are rebuilt.
    /// </summary>
    /// <param name="menu">The menu to restyle.</param>
    /// <param name="seedIcons">Resolve command icons from the item captions, as the WPF seeder does.</param>
    /// <param name="assignKeyTips">
    /// Show a unique key letter in the shortcut column and invoke the item when it is typed, as the
    /// WPF worksheet menu does for every level.
    /// </param>
    public static ContextMenu Apply(ContextMenu menu, bool seedIcons = true, bool assignKeyTips = false)
    {
        ArgumentNullException.ThrowIfNull(menu);

        if (!menu.Classes.Contains(MenuClass))
        {
            menu.Classes.Add(MenuClass);
            menu.FontSize = FontSize;
            menu.Background = Brushes.White;
            menu.BorderBrush = MenuBorderBrush;
            menu.BorderThickness = new Thickness(1);
            menu.Padding = new Thickness(0);
            menu.Resources["MenuFlyoutScrollerMargin"] = new Thickness(0, 1);
            // Submenu popups read the presenter keys, so they take the same surface and border.
            menu.Resources["MenuFlyoutPresenterBackground"] = Brushes.White;
            menu.Resources["MenuFlyoutPresenterBorderBrush"] = MenuBorderBrush;
            menu.Resources["MenuFlyoutPresenterThemePadding"] = new Thickness(0, 1);
            menu.Resources["MenuFlyoutItemBackgroundPointerOver"] = HighlightBrush;
            menu.Resources["MenuFlyoutItemBackgroundPressed"] = HighlightBrush;
            menu.Resources["MenuFlyoutSubItemChevron"] = SubmenuArrowBrush;
            menu.Resources["MenuFlyoutSubItemChevronPointerOver"] = SubmenuArrowBrush;
            menu.Resources["MenuFlyoutSubItemChevronPressed"] = SubmenuArrowBrush;
            menu.Resources["MenuFlyoutSubItemChevronSubMenuOpened"] = SubmenuArrowBrush;
            menu.Styles.Add(CreateSubmenuArrowStyle());
            if (assignKeyTips)
            {
                menu.AddHandler(
                    InputElement.KeyDownEvent,
                    (_, args) => HandleKeyTip(menu, args),
                    RoutingStrategies.Bubble);
            }
        }

        ApplyItems(ItemsOf(menu), seedIcons, assignKeyTips);
        return menu;
    }

    /// <summary>
    /// Applies the row metrics, icons and key letters to one level of menu items and, recursively,
    /// to their submenus.
    /// </summary>
    public static void ApplyItems(System.Collections.IEnumerable items, bool seedIcons = true, bool assignKeyTips = false)
    {
        ArgumentNullException.ThrowIfNull(items);

        var level = items.Cast<object?>().ToArray();
        var menuItems = level.OfType<MenuItem>().ToArray();
        foreach (var item in menuItems)
        {
            if (!item.Classes.Contains(ItemClass))
                item.Classes.Add(ItemClass);
            item.MinHeight = ItemMinHeight;
            item.Padding = ItemPadding;
            item.Margin = new Thickness(0);
            if (seedIcons)
                SeedIcon(item);
        }

        if (assignKeyTips)
            AssignKeyTips(menuItems);

        var separatorMargin = menuItems.Any(item => item.Icon is not null)
            ? IconColumnSeparatorMargin
            : PlainSeparatorMargin;
        foreach (var separator in level.OfType<Separator>())
            separator.Margin = separatorMargin;

        foreach (var item in menuItems)
            ApplyItems(ItemsOf(item), seedIcons, assignKeyTips);
    }

    // A menu filled through ItemsSource is read from that source directly.
    private static System.Collections.IEnumerable ItemsOf(ItemsControl owner) =>
        owner.ItemsSource ?? owner.Items;

    // StyleTrigger priority (the class makes the selector conditional) so the setters win over the
    // values the Fluent MenuItem template assigns to its arrow path.
    private static Style CreateSubmenuArrowStyle() =>
        new(x => x.OfType<MenuItem>().Class(ItemClass).Template().OfType<AvaloniaPath>().Name("PART_ChevronPath"))
        {
            Setters =
            {
                new Setter(AvaloniaPath.DataProperty, Geometry.Parse(SubmenuArrowPathData)),
                new Setter(AvaloniaShape.StretchProperty, Stretch.None),
                new Setter(Layoutable.WidthProperty, 8d),
                new Setter(Layoutable.HeightProperty, 16d),
                new Setter(AvaloniaShape.FillProperty, SubmenuArrowBrush),
            },
        };

    // Mirrors RibbonMenuIconSeeder.TryResolveIcon: the caption without its access-key marker and
    // ellipsis is the command name; a command with no specific glyph stays icon-less. Toggle rows
    // keep their check mark instead.
    private static void SeedIcon(MenuItem item)
    {
        if (item.Icon is not null || item.ToggleType != MenuItemToggleType.None)
            return;

        var header = CleanHeader(item.Header as string);
        if (header.Length == 0)
            return;

        var commandName = NormalizeCommandName(header);
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);
        if (icon.Kind == RibbonCommandIconKind.Generic)
            return;

        item.Icon = AvaloniaRibbonIcons.Build(icon, IconSize, commandName);
    }

    private static string CleanHeader(string? header) =>
        (header ?? string.Empty)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("…", string.Empty, StringComparison.Ordinal)
            .Replace("...", string.Empty, StringComparison.Ordinal)
            .Trim();

    // Same paste-submenu aliases as RibbonMenuIconSeeder.NormalizeCommandName.
    private static string NormalizeCommandName(string header)
    {
        if (header.Equals("Values", StringComparison.OrdinalIgnoreCase))
            return "Paste Values";
        if (header.Equals("Formulas", StringComparison.OrdinalIgnoreCase))
            return "Paste Formulas";
        if (header.Equals("Formatting", StringComparison.OrdinalIgnoreCase))
            return "Paste Formatting";
        if (header.Equals("Transpose", StringComparison.OrdinalIgnoreCase))
            return "Transpose Paste";

        return header;
    }

    // Mirrors MenuKeyTipAssigner.AssignUniqueKeyTips: one unique key per level from the shared
    // planner. A real shortcut gesture already on the item is left alone.
    private static void AssignKeyTips(IReadOnlyList<MenuItem> items)
    {
        if (items.Count == 0)
            return;

        var assignments = MenuKeyTipAssignmentPlanner.AssignUnique(
            items
                .Select(item => new MenuKeyTipAssignmentCandidate(item.Header as string))
                .ToArray());
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].InputGesture is null && TryCreateKeyTipGesture(assignments[index]) is { } gesture)
                items[index].InputGesture = gesture;
        }
    }

    private static KeyGesture? TryCreateKeyTipGesture(string? keyTip)
    {
        if (keyTip is not { Length: 1 })
            return null;

        var character = char.ToUpperInvariant(keyTip[0]);
        if (character is >= 'A' and <= 'Z')
            return new KeyGesture(Key.A + (character - 'A'));
        if (character is >= '0' and <= '9')
            return new KeyGesture(Key.D0 + (character - '0'));

        return null;
    }

    // A bare key typed while the menu is open invokes the item of the focused level that shows
    // that key, or opens its submenu.
    private static void HandleKeyTip(ContextMenu menu, KeyEventArgs args)
    {
        if (args.Handled || !menu.IsOpen || args.KeyModifiers != KeyModifiers.None)
            return;

        // Letters typed into an editor hosted in the menu (the search row) are text, not key tips.
        if (args.Source is TextBox)
            return;

        var level = args.Source is MenuItem { Parent: MenuItem parent }
            ? ItemsOf(parent).OfType<MenuItem>()
            : ItemsOf(menu).OfType<MenuItem>();
        var target = level.FirstOrDefault(candidate =>
            candidate.IsEnabled &&
            candidate.InputGesture is { KeyModifiers: KeyModifiers.None } gesture &&
            gesture.Key == args.Key);
        if (target is null)
            return;

        args.Handled = true;
        var children = ItemsOf(target).OfType<MenuItem>().ToArray();
        if (children.Length > 0)
        {
            target.IsSubMenuOpen = true;
            var firstChild = children.FirstOrDefault(child => child.IsEnabled);
            if (firstChild is not null)
            {
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        if (target.IsSubMenuOpen)
                            firstChild.Focus(NavigationMethod.Directional);
                    },
                    DispatcherPriority.Input);
            }

            return;
        }

        target.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, target));
        menu.Close();
    }
}

internal static class AvaloniaManagedContextMenu
{
    private static readonly object Marker = new();

    public static ContextMenu Attach(Control anchor, Func<IReadOnlyList<Control>> buildItems)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(buildItems);

        if (anchor.ContextMenu is { Tag: var tag } existing && ReferenceEquals(tag, Marker))
            return existing;

        var menu = new ContextMenu
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Tag = Marker,
        };

        void Populate()
        {
            menu.Items.Clear();
            foreach (var item in buildItems())
                menu.Items.Add(item);
            // These catalog menus bring their own key gestures; only the WPF row chrome is added.
            AvaloniaContextMenuChrome.Apply(menu);
        }

        Populate();
        menu.Opened += (_, _) =>
        {
            Populate();
            Dispatcher.UIThread.Post(() =>
                menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled)?.Focus());
        };
        menu.Closed += (_, _) => Dispatcher.UIThread.Post(() => anchor.Focus());
        menu.KeyDown += (_, args) =>
        {
            if (!menu.IsOpen)
                return;

            if (args.Key == Key.Escape)
            {
                menu.Close();
                args.Handled = true;
                return;
            }

            if (args.Handled || args.Source is not Control)
                return;

            var item = menu.Items
                .OfType<MenuItem>()
                .FirstOrDefault(candidate =>
                    candidate.IsEnabled &&
                    candidate.InputGesture is KeyGesture gesture &&
                    gesture.Key == args.Key &&
                    gesture.KeyModifiers == args.KeyModifiers);
            if (item is null)
                return;

            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
            menu.Close();
            args.Handled = true;
        };
        anchor.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(anchor).Properties.IsRightButtonPressed)
                return;

            menu.Open(anchor);
            args.Handled = true;
        };
        anchor.KeyDown += (_, args) =>
        {
            if (!IsKeyboardInvocation(args.Key, args.KeyModifiers))
                return;

            menu.Open(anchor);
            args.Handled = true;
        };
        anchor.ContextMenu = menu;
        return menu;
    }

    internal static bool IsKeyboardInvocation(Key key, KeyModifiers modifiers) =>
        key == Key.Apps || key == Key.F10 && modifiers == KeyModifiers.Shift;
}

internal static class AvaloniaBackstageRecentFileContextMenu
{
    public static IReadOnlyList<Control> BuildItems(
        bool isPinned,
        string fileName,
        Func<string, string> resolveHeader,
        Action<BackstageRecentFileMenuAction> dispatch)
    {
        var commands = isPinned
            ? BackstageRecentFileContextMenuPlanner.BuildPinnedFileCommands()
            : BackstageRecentFileContextMenuPlanner.BuildRecentFileCommands();

        return commands.Select(command =>
        {
            var item = new MenuItem
            {
                Header = resolveHeader(command.ResourceKey),
                Tag = command.Action,
            };
            AutomationProperties.SetAutomationId(item, command.AutomationId);
            AutomationProperties.SetName(
                item,
                UiText.Format("RecentFile_AutomationNameFormat", command.CommandName, fileName));
            AutomationProperties.SetHelpText(
                item,
                UiText.Format("RecentFile_HelpTextFormat", command.CommandName, fileName));
            item.Click += (_, _) => dispatch(command.Action);
            return (Control)item;
        }).ToArray();
    }
}

internal static class AvaloniaQuickAccessToolbarContextMenu
{
    public static IReadOnlyList<Control> BuildCustomizationItems(
        QuickAccessToolbarCustomizationMenuState state,
        Func<string, string> resolveHeader,
        Action<QuickAccessToolbarMenuCommand> dispatch) =>
        BuildItems(
            QuickAccessToolbarContextMenuPlanner.BuildCustomizationCommands(state),
            resolveHeader,
            dispatch);

    public static IReadOnlyList<Control> BuildHistoryItems(
        QuickAccessToolbarHistoryMenuState state,
        Action<QuickAccessToolbarMenuCommand> dispatch) =>
        BuildItems(
            QuickAccessToolbarContextMenuPlanner.BuildHistoryCommands(state),
            resourceKey => resourceKey,
            dispatch);

    private static IReadOnlyList<Control> BuildItems(
        IReadOnlyList<QuickAccessToolbarMenuCommand> commands,
        Func<string, string> resolveHeader,
        Action<QuickAccessToolbarMenuCommand> dispatch) =>
        commands.Select(command =>
        {
            var item = new MenuItem
            {
                Header = string.IsNullOrEmpty(command.Header)
                    ? resolveHeader(command.ResourceKey)
                    : command.Header,
                IsEnabled = command.IsEnabled,
                Tag = command.Action,
            };
            if (!string.IsNullOrEmpty(command.AutomationId))
                AutomationProperties.SetAutomationId(item, command.AutomationId);
            if (command.Action != QuickAccessToolbarMenuAction.None)
                item.Click += (_, _) => dispatch(command);
            return (Control)item;
        }).ToArray();
}
