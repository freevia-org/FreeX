using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Free.Shared.Ribbon;

namespace FreeP.App.Avalonia;

/// <summary>
/// Avalonia-native favorites strip and categorized More Effects flyout for Animations. Favourites,
/// categorisation and geometry are planned by <see cref="AnimationGalleryPlanner"/>.
/// </summary>
internal static class PresentationAnimationGallery
{
    public static Control Build(
        RibbonTab tab,
        IRibbonCommandRegistry registry,
        IRibbonStateStore stateStore,
        RibbonAdaptiveGroupState state = RibbonAdaptiveGroupState.Full)
    {
        var plan = AnimationGalleryPlanner.Plan(tab, state);
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(
                FreePRibbonGalleryMetrics.StripMarginSide,
                FreePRibbonGalleryMetrics.StripMarginTop,
                FreePRibbonGalleryMetrics.StripMarginSide,
                0),
        };
        foreach (var control in plan.Favorites)
            strip.Children.Add(BuildFavoriteButton(control, registry, stateStore));
        strip.Children.Add(BuildMoreButton(plan, registry, stateStore));
        return strip;
    }

    private static Control BuildFavoriteButton(
        RibbonControl control, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        var content = new Grid
        {
            Width = FreePRibbonGalleryMetrics.AnimationButtonWidth,
            Height = FreePRibbonGalleryMetrics.AnimationButtonHeight,
        };
        content.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        content.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        content.Children.Add(BuildPreview(control.CommandId.Value));
        var caption = new TextBlock
        {
            Text = control.Label,
            FontSize = FreePRibbonGalleryMetrics.CaptionFontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(
                FreePRibbonGalleryMetrics.CaptionMarginSide,
                FreePRibbonGalleryMetrics.CaptionMarginTop,
                FreePRibbonGalleryMetrics.CaptionMarginSide,
                0),
        };
        Grid.SetRow(caption, 1);
        content.Children.Add(caption);

        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(FreePRibbonGalleryMetrics.ButtonPadding),
            Margin = new Thickness(FreePRibbonGalleryMetrics.ButtonSideMargin, 0),
        };
        ToolTip.SetTip(button, control.Label);
        AutomationProperties.SetName(button, control.Label);
        BindState(button, control.CommandId, stateStore);
        button.PointerEntered += (_, _) => SetHover(button, true);
        button.PointerExited += (_, _) => SetHover(button, false);
        button.Click += (_, _) => FreePRibbonGalleryCommands.Execute(registry, control.CommandId);
        return button;
    }

    private static Control BuildMoreButton(
        AnimationGalleryPlan plan, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        var label = UiText.Get(AnimationGalleryPlanner.MoreEffectsLabelKey);
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = label + " ▾",
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = FreePRibbonGalleryMetrics.AnimationMoreFontSize,
            },
            Width = FreePRibbonGalleryMetrics.AnimationMoreWidth,
            Height = FreePRibbonGalleryMetrics.AnimationMoreHeight,
            Margin = new Thickness(FreePRibbonGalleryMetrics.AnimationMoreLeftMargin, 0, FreePRibbonGalleryMetrics.AnimationMoreRightMargin, 0),
        };
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
        button.Flyout = BuildMoreFlyout(plan, registry, stateStore);
        return button;
    }

    private static MenuFlyout BuildMoreFlyout(
        AnimationGalleryPlan plan, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        var flyout = new MenuFlyout();
        foreach (var categoryPlan in plan.Categories)
        {
            var category = new MenuItem { Header = UiText.Get(categoryPlan.HeaderKey) };
            foreach (var control in categoryPlan.Controls)
            {
                var item = new MenuItem { Header = control.Label };
                AutomationProperties.SetName(item, control.Label);
                BindState(item, control.CommandId, stateStore);
                item.Click += (_, _) => FreePRibbonGalleryCommands.Execute(registry, control.CommandId);
                category.Items.Add(item);
            }

            if (category.Items.Count > 0)
                flyout.Items.Add(category);
        }

        return flyout;
    }

    private static Control BuildPreview(string commandId) => new Border
    {
        Width = FreePRibbonGalleryMetrics.AnimationPreviewWidth,
        Height = FreePRibbonGalleryMetrics.AnimationPreviewHeight,
        BorderBrush = Brush(FreePRibbonGalleryMetrics.AnimationPreviewBorder),
        BorderThickness = new Thickness(1),
        Background = Brushes.White,
        Child = new TextBlock
        {
            Text = PresentationAnimationPreviewCatalog.GlyphFor(commandId),
            FontSize = FreePRibbonGalleryMetrics.AnimationGlyphSizeFor(commandId),
            FontFamily = new FontFamily(FreePRibbonGalleryMetrics.AnimationGlyphFontFamily),
            Foreground = Brush(FreePRibbonGalleryMetrics.AnimationGlyphColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static void BindState(Control element, RibbonCommandId commandId, IRibbonStateStore stateStore)
    {
        void Apply(RibbonCommandState state) => element.IsEnabled = state.IsEnabled;
        Apply(stateStore.GetState(commandId));
        EventHandler<RibbonStateChangedEventArgs>? handler = (_, args) => { if (args.Id == commandId) Apply(args.State); };
        stateStore.StateChanged += handler;
        element.DetachedFromVisualTree += (_, _) => stateStore.StateChanged -= handler;
    }

    private static void SetHover(Button button, bool hovering)
    {
        button.Background = hovering ? Brush(FreePRibbonGalleryMetrics.HoverBackground) : Brushes.Transparent;
        button.BorderBrush = hovering ? Brush(FreePRibbonGalleryMetrics.HoverBorder) : Brushes.Transparent;
    }

    private static SolidColorBrush Brush(SrgbColor color) => new(Color.FromRgb(color.R, color.G, color.B));
}
