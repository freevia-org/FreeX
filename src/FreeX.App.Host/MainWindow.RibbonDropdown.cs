using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const string RibbonDropdownMainHoverPartName = "PART_RibbonDropdownMainHover";
    private const string RibbonDropdownMenuHoverPartName = "PART_RibbonDropdownMenuHover";
    private const string RibbonDropdownContentPartName = "PART_RibbonDropdownContent";
    private const string RibbonSplitDropdownCommandSuffix = ".Dropdown";
    private const double RibbonSplitButtonIconColumnWidth = 24;
    private const double RibbonSplitButtonDropdownColumnWidth = 14;
    private const double RibbonSplitButtonLabeledDropdownColumnWidth = 20;
    private const double RibbonSplitButtonLargeDropdownZoneHeight = 20;
    private const double RibbonSplitButtonLargeChevronTopMargin = 7;
    private const double RibbonSplitButtonFallbackDropdownZoneWidth = RibbonSplitButtonLabeledDropdownColumnWidth;
    private const double RibbonSplitButtonIconOnlyContentWidth =
        RibbonSplitButtonIconColumnWidth + RibbonSplitButtonDropdownColumnWidth;

    private void NormalizeRibbonMenuButtons(RibbonStaticSurfaceSnapshot surface)
    {
        foreach (var button in surface.ButtonBases)
        {
            if (RibbonMetadata.IsCollapsedGroupButton(button) ||
                button.ContextMenu is null && !RibbonMetadata.IsDropdownMenuButton(button))
            {
                continue;
            }

            EnsureRibbonDropdownChevron(button);
            if (IsDedicatedRibbonSplitDropdownButton(button))
                continue;

            EnsureRibbonDropdownZoneHandler(button);
            EnsureRibbonDropdownZoneHighlight(button);
        }
    }

    private static bool IsDedicatedRibbonSplitDropdownButton(ButtonBase button) =>
        RibbonMetadata.TryGetCommandName(button, out var commandName) &&
        commandName.EndsWith(RibbonSplitDropdownCommandSuffix, StringComparison.Ordinal);

    internal static void EnsureRibbonDropdownChevron(ButtonBase button)
    {
        var contentRoot = button.Content as DependencyObject ??
                          WrapRibbonDropdownTextContent(button);

        if (contentRoot is null ||
            ContainsRibbonDropdownChevron(contentRoot))
            return;

        var layout = GetRibbonDropdownZoneLayout(button);
        EnsureRibbonDropdownButtonFootprint(button, layout);
        switch (contentRoot)
        {
            case Grid grid:
                AddRibbonDropdownChevronToGrid(button, grid, layout);
                break;
            case StackPanel stack:
                AddRibbonDropdownChevronToStack(button, stack, layout);
                break;
            case Panel panel:
                panel.Children.Add(CreateRibbonDropdownChevron(layout));
                break;
        }
    }

    private static void EnsureRibbonDropdownButtonFootprint(ButtonBase button, RibbonCommandContentLayout layout)
    {
        if (layout is not (RibbonCommandContentLayout.Small or RibbonCommandContentLayout.IconOnly))
            return;

        var minimumWidth = GetRibbonSplitButtonMinimumWidth();
        if (RibbonMetadata.TryGetCompactWidths(button, out var fullWidth, out var compactWidth))
        {
            SetRibbonCompactWidths(
                button,
                Math.Max(fullWidth, minimumWidth),
                Math.Max(compactWidth, minimumWidth));
        }

        if (layout == RibbonCommandContentLayout.IconOnly &&
            button is FrameworkElement element &&
            element.Width is > 0)
        {
            element.Width = Math.Max(element.Width, minimumWidth);
        }
    }

    private static double GetRibbonSplitButtonMinimumWidth() => RibbonSplitButtonIconOnlyContentWidth;

    private static DependencyObject? WrapRibbonDropdownTextContent(ButtonBase button)
    {
        if (button.Content is not string text)
            return null;

        var commandName = GetRibbonButtonCommandName(button);
        var layoutKind = GetRibbonCommandLayoutKind(button, commandName, text);
        if (ShouldUsePlannedRibbonCommandWidth(button, commandName, layoutKind))
            button.Width = 0;
        ApplyRibbonCommandSize(button, layoutKind);
        if (layoutKind is RibbonCommandLayoutKind.Small)
            button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, GetSmallRibbonCommandWidth(text));
        var fullWidth = button.Width is > 0 ? button.Width : Math.Max(button.ActualWidth, 64);
        var compactWidth = layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium ? 38 : 24;
        SetRibbonCompactWidths(button, fullWidth, compactWidth);

        var content = CreateRibbonCommandContent(commandName, text, layoutKind);
        button.Content = content;
        button.HorizontalContentAlignment = layoutKind is RibbonCommandLayoutKind.Small
            ? System.Windows.HorizontalAlignment.Left
            : System.Windows.HorizontalAlignment.Center;
        return content;
    }

    private static bool ContainsRibbonDropdownChevron(DependencyObject root) =>
        EnumerateVisualDescendants(root)
            .Concat(EnumerateLogicalDescendants(root))
            .Distinct()
            .Any(RibbonMetadata.IsDropdownChevron);

    internal static void AddRibbonDropdownChevronToGrid(ButtonBase button, Grid grid, RibbonCommandContentLayout layout)
    {
        var chevron = CreateRibbonDropdownChevron(layout);
        if (layout == RibbonCommandContentLayout.IconOnly ||
            grid.ColumnDefinitions.Count == 0)
        {
            AddRibbonIconOnlyDropdownChevronToGrid(grid, chevron);
            return;
        }

        EnsureRibbonDropdownContentUsesButtonWidth(button, grid);
        if (layout == RibbonCommandContentLayout.Small)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var column = new ColumnDefinition { Width = new GridLength(GetRibbonSplitButtonDropdownColumnWidth(layout)) };
        grid.ColumnDefinitions.Add(column);
        Grid.SetColumn(chevron, grid.ColumnDefinitions.Count - 1);
        grid.Children.Add(chevron);
    }

    private static void AddRibbonIconOnlyDropdownChevronToGrid(Grid grid, FrameworkElement chevron)
    {
        if (grid.ColumnDefinitions.Count == 0)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RibbonSplitButtonIconColumnWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RibbonSplitButtonDropdownColumnWidth) });
            foreach (UIElement child in grid.Children)
                Grid.SetColumn(child, 0);
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RibbonSplitButtonDropdownColumnWidth) });
        }

        grid.Width = Math.Max(grid.Width is > 0 ? grid.Width : 0, RibbonSplitButtonIconOnlyContentWidth);
        grid.MinWidth = Math.Max(grid.MinWidth, RibbonSplitButtonIconOnlyContentWidth);
        chevron.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        chevron.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        chevron.Margin = new Thickness(0);
        Grid.SetColumn(chevron, grid.ColumnDefinitions.Count - 1);
        grid.Children.Add(chevron);
    }

    private static void AddRibbonDropdownChevronToStack(ButtonBase button, StackPanel stack, RibbonCommandContentLayout layout)
    {
        var chevron = CreateRibbonDropdownChevron(layout);
        if (layout is RibbonCommandContentLayout.Large or RibbonCommandContentLayout.Medium)
        {
            if (layout == RibbonCommandContentLayout.Large &&
                stack.Orientation == Orientation.Vertical)
            {
                TightenLargeRibbonDropdownStackForSplitBand(stack);
                chevron.Margin = new Thickness(0, RibbonSplitButtonLargeChevronTopMargin, 0, 0);
            }
            else
            {
                chevron.Margin = stack.Orientation == Orientation.Horizontal
                    ? new Thickness(4, 0, 0, 0)
                    : new Thickness(0, 0, 0, 0);
            }

            stack.Children.Add(chevron);
            return;
        }

        if (stack.Orientation == Orientation.Horizontal &&
            button is ContentControl contentControl &&
            ReferenceEquals(contentControl.Content, stack))
        {
            var effectiveLayout = layout == RibbonCommandContentLayout.None ? RibbonCommandContentLayout.Small : layout;
            var wrapper = new Grid
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            RibbonMetadata.SetCommandContentLayout(wrapper, effectiveLayout);
            EnsureRibbonDropdownContentUsesButtonWidth(button, wrapper);
            wrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            wrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GetRibbonSplitButtonDropdownColumnWidth(effectiveLayout)) });

            contentControl.Content = null;
            Grid.SetColumn(stack, 0);
            wrapper.Children.Add(stack);
            Grid.SetColumn(chevron, 2);
            wrapper.Children.Add(chevron);
            contentControl.Content = wrapper;
            return;
        }

        stack.Children.Add(chevron);
    }

    private static void TightenLargeRibbonDropdownStackForSplitBand(StackPanel stack)
    {
        foreach (var iconSlot in stack.Children
                     .OfType<FrameworkElement>()
                     .Where(RibbonMetadata.IsCommandIcon))
        {
            iconSlot.Margin = new Thickness(iconSlot.Margin.Left, iconSlot.Margin.Top, iconSlot.Margin.Right, 0);
            return;
        }
    }

    private static double GetRibbonSplitButtonDropdownColumnWidth(RibbonCommandContentLayout layout) =>
        layout == RibbonCommandContentLayout.IconOnly
            ? RibbonSplitButtonDropdownColumnWidth
            : RibbonSplitButtonLabeledDropdownColumnWidth;

    private static void EnsureRibbonDropdownContentUsesButtonWidth(ButtonBase button, FrameworkElement content)
    {
        if (button is not FrameworkElement buttonElement)
            return;

        var buttonWidth = buttonElement.Width is > 0
            ? buttonElement.Width
            : buttonElement.ActualWidth;
        if (buttonWidth <= 0 || double.IsNaN(buttonWidth) || double.IsInfinity(buttonWidth))
            return;

        // The split lane owns the right padding so the glyph and hover strip share one right edge.
        var horizontalInset = 0.0;
        if (button is Control control)
        {
            horizontalInset =
                control.Padding.Left +
                control.BorderThickness.Left +
                control.BorderThickness.Right;
        }

        content.Width = Math.Max(content.Width is > 0 ? content.Width : 0, Math.Max(0, buttonWidth - horizontalInset));
        content.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
    }

    private static FrameworkElement CreateRibbonDropdownChevron(RibbonCommandContentLayout layout)
    {
        var isCompactChevron = layout is RibbonCommandContentLayout.Small or RibbonCommandContentLayout.IconOnly;
        var chevron = CreateRibbonChevronGlyph(
            width: isCompactChevron ? 8 : 10,
            height: isCompactChevron ? 7 : 8,
            brush: BrushFromRgb(31, 31, 31),
            pointsUp: false);
        RibbonMetadata.SetRole(chevron, RibbonMetadataRole.DropdownChevron);
        return chevron;
    }

    private static FrameworkElement CreateRibbonChevronGlyph(double width, double height, Brush brush, bool pointsUp)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(pointsUp ? "M2,6 L6,2 L10,6" : "M2,2 L6,6 L10,2"),
            Stroke = brush,
            StrokeThickness = 1.45,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            Stretch = Stretch.None,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };

        return new Viewbox
        {
            Width = width,
            Height = height,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Child = path,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
    }

    private void EnsureRibbonDropdownZoneHandler(ButtonBase button)
    {
        if (RibbonMetadata.GetDropdownZoneHandlerAttached(button))
            return;

        RibbonMetadata.SetDropdownZoneHandlerAttached(button, true);
        button.PreviewMouseLeftButtonDown += RibbonMenuButton_PreviewMouseLeftButtonDown;
    }

    private static void EnsureRibbonDropdownZoneHighlight(ButtonBase button)
    {
        if (RibbonMetadata.GetDropdownZoneHighlightAttached(button))
            return;

        RibbonMetadata.SetDropdownZoneHighlightAttached(button, true);
        if (button is Button standardButton)
            standardButton.Template = CreateRibbonDropdownButtonTemplate();
        button.Loaded += RibbonMenuButton_Loaded;
        button.MouseMove += RibbonMenuButton_InvalidateDropdownZoneHighlight;
        button.MouseLeave += RibbonMenuButton_InvalidateDropdownZoneHighlight;
        button.SizeChanged += RibbonMenuButton_InvalidateDropdownZoneHighlight;
        button.ApplyTemplate();
        UpdateRibbonDropdownZoneHighlight(button);
        EnsureRibbonDropdownZoneAdorner(button);
        button.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                EnsureRibbonDropdownZoneAdorner(button);
                UpdateRibbonDropdownZoneHighlight(button);
            }),
            DispatcherPriority.Loaded);
    }

    private static ControlTemplate CreateRibbonDropdownButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        root.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        root.AppendChild(CreateRibbonDropdownHoverPart(RibbonDropdownMainHoverPartName));
        root.AppendChild(CreateRibbonDropdownHoverPart(RibbonDropdownMenuHoverPartName));

        var chrome = new FrameworkElementFactory(typeof(Border));
        chrome.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        chrome.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        chrome.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        chrome.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        chrome.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.Name = RibbonDropdownContentPartName;
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
        content.SetValue(ContentPresenter.ContentTemplateSelectorProperty, new TemplateBindingExtension(ContentControl.ContentTemplateSelectorProperty));
        content.SetValue(ContentPresenter.ContentStringFormatProperty, new TemplateBindingExtension(ContentControl.ContentStringFormatProperty));
        content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, false);
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        content.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        chrome.AppendChild(content);
        root.AppendChild(chrome);

        var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42, RibbonDropdownContentPartName));
        template.Triggers.Add(disabledTrigger);
        template.VisualTree = root;
        return template;
    }

    private static FrameworkElementFactory CreateRibbonDropdownHoverPart(string name)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = name;
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        border.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Left);
        border.SetValue(FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Top);
        border.SetValue(UIElement.IsHitTestVisibleProperty, false);
        border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        return border;
    }

    private static void RibbonMenuButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ButtonBase button)
        {
            EnsureRibbonDropdownZoneAdorner(button);
            UpdateRibbonDropdownZoneHighlight(button);
        }
    }

    private static void RibbonMenuButton_InvalidateDropdownZoneHighlight(object sender, EventArgs e)
    {
        if (sender is ButtonBase button)
        {
            UpdateRibbonDropdownZoneHighlight(button);
            InvalidateRibbonDropdownZoneAdorner(button);
        }
    }

    private static void UpdateRibbonDropdownZoneHighlight(ButtonBase button)
    {
        if (button is not Button standardButton)
            return;

        var mainHover = standardButton.Template.FindName(RibbonDropdownMainHoverPartName, standardButton) as Border;
        var menuHover = standardButton.Template.FindName(RibbonDropdownMenuHoverPartName, standardButton) as Border;
        if (mainHover is null || menuHover is null)
            return;

        HideRibbonDropdownHoverPart(mainHover);
        HideRibbonDropdownHoverPart(menuHover);

        if (!button.IsEnabled ||
            !button.IsMouseOver ||
            !TryGetRibbonDropdownZoneBounds(button, out var dropdownBounds))
        {
            return;
        }

        var mouse = Mouse.GetPosition(button);
        var isDropdownHover = dropdownBounds.Contains(mouse);
        var activeBounds = isDropdownHover
            ? dropdownBounds
            : GetRibbonMainActionBounds(button, dropdownBounds);
        if (activeBounds is not { Width: > 0, Height: > 0 })
            return;

        ShowRibbonDropdownHoverPart(
            isDropdownHover ? menuHover : mainHover,
            activeBounds,
            GetRibbonDropdownHoverBrush(button));
    }

    private static Brush GetRibbonDropdownHoverBrush(FrameworkElement element)
    {
        if (element.TryFindResource("FreeXRibbonButtonHoverBrush") is Brush brush)
            return brush;

        return new SolidColorBrush(Color.FromRgb(0xBE, 0xE6, 0xFD));
    }

    private static void HideRibbonDropdownHoverPart(Border border)
    {
        border.Background = Brushes.Transparent;
        border.Width = 0;
        border.Height = 0;
    }

    private static void ShowRibbonDropdownHoverPart(Border border, Rect bounds, Brush brush)
    {
        border.Background = brush;
        border.Margin = new Thickness(bounds.X, bounds.Y, 0, 0);
        border.Width = bounds.Width;
        border.Height = bounds.Height;
    }

    private static void EnsureRibbonDropdownZoneAdorner(ButtonBase button)
    {
        var layer = AdornerLayer.GetAdornerLayer(button);
        if (layer is null)
            return;

        if (layer.GetAdorners(button)?.Any(adorner => adorner is RibbonDropdownZoneAdorner) == true)
            return;

        layer.Add(new RibbonDropdownZoneAdorner(button));
    }

    private static void InvalidateRibbonDropdownZoneAdorner(ButtonBase button)
    {
        var adorners = AdornerLayer.GetAdornerLayer(button)?.GetAdorners(button);
        if (adorners is null)
            return;

        foreach (var adorner in adorners.OfType<RibbonDropdownZoneAdorner>())
            adorner.InvalidateVisual();
    }

    private void RibbonMenuButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled ||
            sender is not ButtonBase button ||
            !button.IsEnabled ||
            !IsRibbonDropdownZoneClick(button, e.GetPosition(button)))
        {
            return;
        }

        e.Handled = true;
        if (button.ContextMenu is { } menu)
        {
            OpenRibbonContextMenu(button, menu);
            return;
        }

        if (RibbonMetadata.IsDropdownMenuButton(button))
        {
            var dropdownArgs = new RoutedEventArgs(RibbonMetadata.DropdownClickEvent, button);
            button.RaiseEvent(dropdownArgs);
            if (!dropdownArgs.Handled)
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        }
    }

    private static bool IsRibbonDropdownZoneClick(ButtonBase button, Point position)
    {
        return TryGetRibbonDropdownZoneBounds(button, out var bounds) &&
               bounds.Contains(position);
    }

    internal static bool TryGetRibbonDropdownZoneBounds(ButtonBase button, out Rect bounds)
    {
        bounds = Rect.Empty;
        var width = button.ActualWidth;
        var height = button.ActualHeight;
        if (width <= 0 || height <= 0)
            return false;

        if (IsDedicatedRibbonSplitDropdownButton(button))
        {
            bounds = new Rect(0, 0, width, height);
            return true;
        }

        var layout = GetRibbonDropdownZoneLayout(button);
        if (layout is not (RibbonCommandContentLayout.Large or RibbonCommandContentLayout.Medium) &&
            TryGetRibbonDropdownChevronBounds(button, out _))
        {
            bounds = ShouldUseIconAdjacentDropdownZone(button, layout)
                ? GetRibbonIconAdjacentDropdownZoneBounds(button, width, height)
                : GetRibbonTrailingDropdownZoneBounds(width, height, GetRibbonSplitButtonDropdownColumnWidth(layout));
            return bounds is { Width: > 0, Height: > 0 };
        }

        var horizontalZoneHeight = GetRibbonHorizontalDropdownZoneHeight(layout, height);
        bounds = layout switch
        {
            RibbonCommandContentLayout.Large or RibbonCommandContentLayout.Medium =>
                GetRibbonHorizontalDropdownZoneBounds(button, width, height, horizontalZoneHeight),
            _ when ShouldUseIconAdjacentDropdownZone(button, layout) =>
                GetRibbonIconAdjacentDropdownZoneBounds(button, width, height),
            _ => GetRibbonTrailingDropdownZoneBounds(width, height)
        };

        return bounds is { Width: > 0, Height: > 0 };
    }

    private static double GetRibbonHorizontalDropdownZoneHeight(
        RibbonCommandContentLayout layout,
        double buttonHeight) =>
        layout == RibbonCommandContentLayout.Large && buttonHeight > 66
            ? RibbonSplitButtonLargeDropdownZoneHeight
            : buttonHeight <= 66 ? 12 : 16;

    private static Rect GetRibbonHorizontalDropdownZoneBounds(
        ButtonBase button,
        double width,
        double height,
        double preferredZoneHeight)
    {
        var zoneTop = Math.Max(0, height - preferredZoneHeight);
        var labelBottom = GetRibbonCommandLabelBottom(button);
        if (labelBottom > 0 && labelBottom < height)
            zoneTop = Math.Max(zoneTop, Math.Min(height - 1, Math.Ceiling(labelBottom)));

        return new Rect(0, zoneTop, width, Math.Max(0, height - zoneTop));
    }

    private static bool ShouldUseIconAdjacentDropdownZone(ButtonBase button, RibbonCommandContentLayout layout) =>
        layout == RibbonCommandContentLayout.IconOnly ||
        layout == RibbonCommandContentLayout.Small && !HasVisibleRibbonCommandLabel(button);

    private static Rect GetRibbonIconAdjacentDropdownZoneBounds(ButtonBase button, double width, double height)
    {
        var zoneLeft = Math.Max(0, width - RibbonSplitButtonDropdownColumnWidth);
        if (TryGetRibbonCommandIconBounds(button, out var iconBounds))
            zoneLeft = Math.Clamp(Math.Ceiling(iconBounds.Right), 0, width);

        return new Rect(zoneLeft, 0, Math.Max(0, width - zoneLeft), height);
    }

    private static Rect GetRibbonTrailingDropdownZoneBounds(double buttonWidth, double buttonHeight) =>
        GetRibbonTrailingDropdownZoneBounds(buttonWidth, buttonHeight, RibbonSplitButtonFallbackDropdownZoneWidth);

    private static Rect GetRibbonTrailingDropdownZoneBounds(double buttonWidth, double buttonHeight, double preferredZoneWidth)
    {
        var zoneWidth = Math.Min(preferredZoneWidth, buttonWidth);
        return new Rect(Math.Max(0, buttonWidth - zoneWidth), 0, zoneWidth, buttonHeight);
    }

    private static bool HasVisibleRibbonCommandLabel(ButtonBase button)
    {
        if (button.Content is not DependencyObject contentRoot)
            return false;

        return EnumerateVisualDescendants(contentRoot)
            .Concat(EnumerateLogicalDescendants(contentRoot))
            .OfType<TextBlock>()
            .Distinct()
            .Any(label => RibbonMetadata.IsCommandLabel(label) &&
                          label.Visibility == Visibility.Visible);
    }

    private static double GetRibbonCommandLabelBottom(ButtonBase button)
    {
        if (button.Content is not DependencyObject contentRoot)
            return 0;

        var bottom = 0.0;
        foreach (var label in EnumerateVisualDescendants(contentRoot)
                     .Concat(EnumerateLogicalDescendants(contentRoot))
                     .OfType<TextBlock>()
                     .Distinct()
                     .Where(RibbonMetadata.IsCommandLabel))
        {
            if (!label.IsVisible ||
                label.ActualWidth <= 0 ||
                label.ActualHeight <= 0)
            {
                continue;
            }

            try
            {
                var bounds = label.TransformToAncestor(button)
                    .TransformBounds(new Rect(0, 0, label.ActualWidth, label.ActualHeight));
                bottom = Math.Max(bottom, bounds.Bottom);
            }
            catch (InvalidOperationException)
            {
            }
        }

        return bottom;
    }

    private static RibbonCommandContentLayout GetRibbonDropdownZoneLayout(ButtonBase button)
    {
        if (button.Content is DependencyObject content &&
            RibbonMetadata.TryGetCommandContentLayout(content, out var contentLayout) &&
            contentLayout != RibbonCommandContentLayout.None)
        {
            return contentLayout;
        }

        if (button is FrameworkElement element)
        {
            var width = element.ActualWidth > 0 ? element.ActualWidth : element.Width;
            var height = element.ActualHeight > 0 ? element.ActualHeight : element.Height;
            if (height >= 54)
                return RibbonCommandContentLayout.Large;
            if (width <= 44 && height <= 44)
                return RibbonCommandContentLayout.IconOnly;
        }

        return RibbonCommandContentLayout.None;
    }

    private static Rect GetRibbonMainActionBounds(ButtonBase button, Rect dropdownBounds)
    {
        var width = button.ActualWidth;
        var height = button.ActualHeight;
        if (width <= 0 || height <= 0)
            return Rect.Empty;

        if (dropdownBounds.Y > 0 && dropdownBounds.Width >= width - 0.5)
            return new Rect(0, 0, width, Math.Max(0, dropdownBounds.Y));

        if (dropdownBounds.X > 0 && dropdownBounds.Height >= height - 0.5)
            return new Rect(0, 0, Math.Max(0, dropdownBounds.X), height);

        return new Rect(0, 0, width, height);
    }

    private static bool TryGetRibbonDropdownChevronBounds(ButtonBase button, out Rect bounds)
    {
        bounds = Rect.Empty;
        if (button.Content is not DependencyObject contentRoot)
            return false;

        foreach (var chevron in EnumerateVisualDescendants(contentRoot)
                     .Concat(EnumerateLogicalDescendants(contentRoot))
                     .OfType<FrameworkElement>()
                     .Distinct()
                     .Where(RibbonMetadata.IsDropdownChevron))
        {
            if (!chevron.IsVisible ||
                chevron.ActualWidth <= 0 ||
                chevron.ActualHeight <= 0)
            {
                continue;
            }

            try
            {
                bounds = chevron.TransformToAncestor(button)
                    .TransformBounds(new Rect(0, 0, chevron.ActualWidth, chevron.ActualHeight));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryGetRibbonCommandIconBounds(ButtonBase button, out Rect bounds)
    {
        bounds = Rect.Empty;
        if (button.Content is not DependencyObject contentRoot)
            return false;

        foreach (var icon in EnumerateVisualDescendants(contentRoot)
                     .Concat(EnumerateLogicalDescendants(contentRoot))
                     .OfType<FrameworkElement>()
                     .Distinct()
                     .Where(element => RibbonMetadata.IsCommandIcon(element) &&
                                       !RibbonMetadata.IsDropdownChevron(element)))
        {
            if (!icon.IsVisible ||
                icon.ActualWidth <= 0 ||
                icon.ActualHeight <= 0)
            {
                continue;
            }

            try
            {
                bounds = icon.TransformToAncestor(button)
                    .TransformBounds(new Rect(0, 0, icon.ActualWidth, icon.ActualHeight));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return false;
    }

    private sealed class RibbonDropdownZoneAdorner : Adorner
    {
        private static readonly Pen HoverBorder = CreatePen(Color.FromRgb(0x3C, 0x7F, 0xB1), 1);
        private static readonly Pen SeparatorPen = CreatePen(Color.FromRgb(0x3C, 0x7F, 0xB1), 1);
        private readonly ButtonBase _button;

        public RibbonDropdownZoneAdorner(ButtonBase button)
            : base(button)
        {
            _button = button;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (!_button.IsEnabled ||
                !_button.IsMouseOver ||
                _button.ActualWidth <= 0 ||
                _button.ActualHeight <= 0 ||
                !TryGetRibbonDropdownZoneBounds(_button, out var dropdownBounds))
            {
                return;
            }

            var outerBounds = new Rect(0.5, 0.5, Math.Max(0, _button.ActualWidth - 1), Math.Max(0, _button.ActualHeight - 1));
            if (outerBounds is { Width: > 0, Height: > 0 })
                drawingContext.DrawRoundedRectangle(null, HoverBorder, outerBounds, 2, 2);
            DrawSplitLine(drawingContext, _button, dropdownBounds);
        }

        private static void DrawSplitLine(DrawingContext drawingContext, ButtonBase button, Rect dropdownBounds)
        {
            var width = button.ActualWidth;
            var height = button.ActualHeight;
            if (dropdownBounds.Y > 0 && dropdownBounds.Width >= width - 0.5)
            {
                drawingContext.DrawLine(SeparatorPen, new Point(0, dropdownBounds.Y), new Point(width, dropdownBounds.Y));
                return;
            }

            if (dropdownBounds.X > 0)
                drawingContext.DrawLine(SeparatorPen, new Point(dropdownBounds.X, 0), new Point(dropdownBounds.X, height));
        }

        private static Pen CreatePen(Color color, double thickness)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }
    }
}
