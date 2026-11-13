using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Free.Shared.Ribbon;

namespace FreeP.App.Host;

/// <summary>
/// Native WPF presentation for PowerPoint's common animation strip. The complete catalog is read
/// from the shared ribbon definition and remains available from the categorized More Effects menu.
/// Favourites, categorisation and geometry are planned by <see cref="AnimationGalleryPlanner"/>.
/// </summary>
internal static class PresentationAnimationGallery
{
    public static FrameworkElement Build(
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

    private static FrameworkElement BuildFavoriteButton(
        RibbonControl control,
        IRibbonCommandRegistry registry,
        IRibbonStateStore stateStore)
    {
        var content = new Grid
        {
            Width = FreePRibbonGalleryMetrics.AnimationButtonWidth,
            Height = FreePRibbonGalleryMetrics.AnimationButtonHeight,
        };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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
            Margin = new Thickness(FreePRibbonGalleryMetrics.ButtonSideMargin, 0, FreePRibbonGalleryMetrics.ButtonSideMargin, 0),
            ToolTip = control.Label,
        };
        AutomationProperties.SetName(button, control.Label);
        BindState(button, control.CommandId, stateStore);
        button.MouseEnter += (_, _) =>
        {
            button.Background = Brush(FreePRibbonGalleryMetrics.HoverBackground);
            button.BorderBrush = Brush(FreePRibbonGalleryMetrics.HoverBorder);
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        };
        button.Click += (_, _) => FreePRibbonGalleryCommands.Execute(registry, control.CommandId);
        return button;
    }

    private static FrameworkElement BuildMoreButton(
        AnimationGalleryPlan plan,
        IRibbonCommandRegistry registry,
        IRibbonStateStore stateStore)
    {
        var moreEffectsLabel = UiText.Get(AnimationGalleryPlanner.MoreEffectsLabelKey);
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = moreEffectsLabel + " ▾",
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = FreePRibbonGalleryMetrics.AnimationMoreFontSize,
            },
            Width = FreePRibbonGalleryMetrics.AnimationMoreWidth,
            Height = FreePRibbonGalleryMetrics.AnimationMoreHeight,
            Margin = new Thickness(FreePRibbonGalleryMetrics.AnimationMoreLeftMargin, 0, FreePRibbonGalleryMetrics.AnimationMoreRightMargin, 0),
            ToolTip = moreEffectsLabel,
        };
        AutomationProperties.SetName(button, moreEffectsLabel);
        var menu = BuildMoreMenu(plan, registry, stateStore);
        button.Click += (_, _) =>
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        return button;
    }

    private static ContextMenu BuildMoreMenu(
        AnimationGalleryPlan plan,
        IRibbonCommandRegistry registry,
        IRibbonStateStore stateStore)
    {
        var menu = new ContextMenu();
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
                menu.Items.Add(category);
        }

        return menu;
    }

    private static FrameworkElement BuildPreview(string commandId)
    {
        var border = new Border
        {
            Width = FreePRibbonGalleryMetrics.AnimationPreviewWidth,
            Height = FreePRibbonGalleryMetrics.AnimationPreviewHeight,
            BorderBrush = Brush(FreePRibbonGalleryMetrics.AnimationPreviewBorder),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
        };
        border.Child = new TextBlock
        {
            Text = PresentationAnimationPreviewCatalog.GlyphFor(commandId),
            FontSize = FreePRibbonGalleryMetrics.AnimationGlyphSizeFor(commandId),
            FontFamily = new FontFamily(FreePRibbonGalleryMetrics.AnimationGlyphFontFamily),
            Foreground = Brush(FreePRibbonGalleryMetrics.AnimationGlyphColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return border;
    }

    private static void BindState(FrameworkElement element, RibbonCommandId commandId, IRibbonStateStore stateStore)
    {
        void Apply(RibbonCommandState state) => element.IsEnabled = state.IsEnabled;
        Apply(stateStore.GetState(commandId));
        EventHandler<RibbonStateChangedEventArgs>? handler = (_, args) =>
        {
            if (args.Id == commandId)
                Apply(args.State);
        };
        stateStore.StateChanged += handler;
        element.Unloaded += (_, _) => stateStore.StateChanged -= handler;
    }

    private static SolidColorBrush Brush(SrgbColor color) => new(Color.FromRgb(color.R, color.G, color.B));
}
