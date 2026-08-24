using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Free.Shared.Ribbon;

namespace FreeP.App.Avalonia;

/// <summary>Avalonia-native favorites strip and categorized More Effects flyout for Animations.</summary>
internal static class PresentationAnimationGallery
{
    private static readonly string[] FavoriteCommandIds =
    [
        "freep.anim.none", "freep.anim.entrance.appear", "freep.anim.entrance.fade",
        "freep.anim.entrance.fly-in", "freep.anim.entrance.wipe", "freep.anim.entrance.zoom",
    ];

    public static Control Build(RibbonTab tab, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        var controls = tab.FindGroup("animation-effects")?.Controls
            .Where(control => !string.IsNullOrEmpty(control.CommandId.Value))
            .ToArray() ?? [];
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 1, 2, 0),
        };
        foreach (var commandId in FavoriteCommandIds)
        {
            var control = controls.FirstOrDefault(candidate => candidate.CommandId.Value == commandId);
            if (control is not null)
                strip.Children.Add(BuildFavoriteButton(control, registry, stateStore));
        }
        strip.Children.Add(BuildMoreButton(controls, registry, stateStore));
        return strip;
    }

    private static Control BuildFavoriteButton(
        RibbonControl control, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        var content = new Grid { Width = 55, Height = 51 };
        content.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        content.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        content.Children.Add(BuildPreview(control.CommandId.Value));
        var caption = new TextBlock
        {
            Text = control.Label, FontSize = 9, TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(1, 1, 1, 0),
        };
        Grid.SetRow(caption, 1);
        content.Children.Add(caption);

        var button = new Button
        {
            Content = content, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1), Padding = new Thickness(1), Margin = new Thickness(1, 0),
        };
        ToolTip.SetTip(button, control.Label);
        AutomationProperties.SetName(button, control.Label);
        BindState(button, control.CommandId, stateStore);
        button.PointerEntered += (_, _) => SetHover(button, true);
        button.PointerExited += (_, _) => SetHover(button, false);
        button.Click += (_, _) => Execute(control.CommandId, registry);
        return button;
    }

    private static Control BuildMoreButton(
        IReadOnlyList<RibbonControl> controls, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        var label = UiText.Get("Ribbon_Command_AnimationMoreEffects_Label");
        var button = new Button
        {
            Content = new TextBlock { Text = label + " ▾", TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, FontSize = 10 },
            Width = 62, Height = 50, Margin = new Thickness(2, 0, 1, 0),
        };
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
        button.Flyout = BuildMoreFlyout(controls, registry, stateStore);
        return button;
    }

    private static MenuFlyout BuildMoreFlyout(
        IReadOnlyList<RibbonControl> controls, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        var flyout = new MenuFlyout();
        AddCategory(flyout, UiText.Get("Ribbon_Category_AnimationEntrance_Label"), controls, ".entrance.", registry, stateStore);
        AddCategory(flyout, UiText.Get("Ribbon_Category_AnimationEmphasis_Label"), controls, ".emphasis.", registry, stateStore);
        AddCategory(flyout, UiText.Get("Ribbon_Category_AnimationExit_Label"), controls, ".exit.", registry, stateStore);
        return flyout;
    }

    private static void AddCategory(
        MenuFlyout flyout, string header, IEnumerable<RibbonControl> controls, string marker,
        IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        var category = new MenuItem { Header = header };
        foreach (var control in controls.Where(control => control.CommandId.Value.Contains(marker, StringComparison.Ordinal)))
        {
            var item = new MenuItem { Header = control.Label };
            AutomationProperties.SetName(item, control.Label);
            BindState(item, control.CommandId, stateStore);
            item.Click += (_, _) => Execute(control.CommandId, registry);
            category.Items.Add(item);
        }
        if (category.Items.Count > 0)
            flyout.Items.Add(category);
    }

    private static Control BuildPreview(string commandId)
    {
        var mark = PresentationAnimationPreviewCatalog.GlyphFor(commandId);
        return new Border
        {
            Width = 50, Height = 30, BorderBrush = new SolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xA6)),
            BorderThickness = new Thickness(1), Background = Brushes.White,
            Child = new TextBlock
            {
                Text = mark, FontSize = PresentationAnimationPreviewCatalog.IsNone(commandId) ? 24 : 25, FontFamily = new FontFamily("Segoe UI Symbol"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private static void BindState(Control element, RibbonCommandId commandId, IRibbonStateStore stateStore)
    {
        void Apply(RibbonCommandState state) => element.IsEnabled = state.IsEnabled;
        Apply(stateStore.GetState(commandId));
        EventHandler<RibbonStateChangedEventArgs>? handler = (_, args) => { if (args.Id == commandId) Apply(args.State); };
        stateStore.StateChanged += handler;
        element.DetachedFromVisualTree += (_, _) => stateStore.StateChanged -= handler;
    }

    private static void Execute(RibbonCommandId commandId, IRibbonCommandRegistry registry)
    {
        if (registry.TryGet(commandId, out var command) && command is not null)
            command.Execute(RibbonCommandContext.Empty);
    }

    private static void SetHover(Button button, bool hovering)
    {
        button.Background = hovering ? new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB)) : Brushes.Transparent;
        button.BorderBrush = hovering ? new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)) : Brushes.Transparent;
    }
}
