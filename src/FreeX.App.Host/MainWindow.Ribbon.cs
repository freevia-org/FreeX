using System;
using System.Collections.Generic;
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

    private void NormalizeRibbonCommandButtons(RibbonStaticSurfaceSnapshot surface)
    {
        foreach (var button in surface.ButtonBases)
        {
            if (button is CheckBox or RadioButton)
                continue;

            if (button.Content is not string label || string.IsNullOrWhiteSpace(label))
                continue;

            var commandName = GetRibbonButtonCommandName(button);
            var layoutKind = RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label);
            ApplyRibbonCommandSize(button, layoutKind);
            if (layoutKind is RibbonCommandLayoutKind.Small)
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, GetSmallRibbonCommandWidth(label));
            var fullWidth = button.Width is > 0 ? button.Width : Math.Max(button.ActualWidth, 64);
            var compactWidth = layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium ? 38 : 24;
            SetRibbonCompactWidths(button, fullWidth, compactWidth);

            button.Content = CreateRibbonCommandContent(commandName, label, layoutKind);
            button.HorizontalContentAlignment = layoutKind is RibbonCommandLayoutKind.Small
                ? System.Windows.HorizontalAlignment.Left
                : System.Windows.HorizontalAlignment.Center;
        }
    }

    private void NormalizeRibbonSurface(bool forceCompact = false)
    {
        if (_normalizingRibbonSurface)
            return;

        _normalizingRibbonSurface = true;
        try
        {
            NormalizeStaticRibbonSurfaceForSelectedTabOnce();
            UpdateRibbonCompactMode(force: forceCompact);
        }
        finally
        {
            _normalizingRibbonSurface = false;
        }
    }

    private void NormalizeStaticRibbonSurfaceForSelectedTabOnce()
    {
        if (RibbonTabs?.SelectedItem is not TabItem tabItem)
            return;

        if (!_normalizedRibbonStaticTabs.Add(tabItem))
            return;

        PrepareRibbonTabForImmediateCompaction(tabItem, forceLayout: true);
        var root = GetRibbonTabContentRoot(tabItem);
        var surface = CaptureRibbonStaticSurface(root);
        NormalizeRibbonGroupMetadata(surface);
        NormalizeRibbonCommandButtons(surface);
        NormalizeExistingRibbonIconText(surface);
        ConfigureInsertRibbonSurface(surface);
        NormalizeRibbonCommandGroups(surface);
        NormalizeRibbonMenuButtons(surface);
        AlignRibbonIconColumns(surface);
        HideRibbonScrollBars(root, surface);
        ApplyToolbarDropdownWhiteBackgrounds(surface);
        InvalidateRibbonAdaptiveMeasurementCaches();
    }

    private void NormalizeRibbonGroupMetadata(RibbonStaticSurfaceSnapshot surface)
    {
        foreach (var group in surface.Grids)
        {
            if (!RibbonMetadata.IsRibbonGroup(group) ||
                RibbonMetadata.TryGetGroupName(group, out _))
            {
                continue;
            }

            if (TryFindStaticRibbonGroupLabel(group, out var groupName))
                RibbonMetadata.SetGroupName(group, groupName);
        }
    }

    private static bool TryFindStaticRibbonGroupLabel(Grid group, out string groupName)
    {
        foreach (var border in group.Children.OfType<Border>())
        {
            if (Grid.GetRow(border) == 1 &&
                border.Child is TextBlock groupLabel &&
                !string.IsNullOrWhiteSpace(groupLabel.Text))
            {
                groupName = groupLabel.Text.Trim();
                return true;
            }
        }

        groupName = "";
        return false;
    }

    private void HideRibbonScrollBars(DependencyObject root, RibbonStaticSurfaceSnapshot surface)
    {
        if (root is FrameworkElement element &&
            FindVisualAncestor<ScrollViewer>(element) is { } owningScrollViewer)
        {
            owningScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        }

        foreach (var scrollViewer in surface.ScrollViewers)
            scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
    }

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
            EnsureRibbonDropdownZoneHandler(button);
            EnsureRibbonDropdownZoneHighlight(button);
        }
    }

    private static void EnsureRibbonDropdownChevron(ButtonBase button)
    {
        var contentRoot = button.Content as DependencyObject ??
                          WrapRibbonDropdownTextContent(button);

        if (contentRoot is null ||
            ContainsRibbonDropdownChevron(contentRoot))
            return;

        var layout = GetRibbonDropdownZoneLayout(button);
        switch (contentRoot)
        {
            case Grid grid:
                AddRibbonDropdownChevronToGrid(grid, layout);
                break;
            case StackPanel stack:
                AddRibbonDropdownChevronToStack(stack, layout);
                break;
            case Panel panel:
                panel.Children.Add(CreateRibbonDropdownChevron(layout));
                break;
        }
    }

    private static DependencyObject? WrapRibbonDropdownTextContent(ButtonBase button)
    {
        if (button.Content is not string text)
            return null;

        var commandName = GetRibbonButtonCommandName(button);
        var layoutKind = RibbonCommandPresentationPlanner.GetLayoutKind(commandName, text);
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

    private static void AddRibbonDropdownChevronToGrid(Grid grid, RibbonCommandContentLayout layout)
    {
        var chevron = CreateRibbonDropdownChevron(layout);
        if (layout == RibbonCommandContentLayout.IconOnly ||
            grid.ColumnDefinitions.Count == 0)
        {
            chevron.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            chevron.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
            chevron.Margin = new Thickness(0, 0, -1, -1);
            grid.Children.Add(chevron);
            return;
        }

        var column = new ColumnDefinition { Width = new GridLength(12) };
        grid.ColumnDefinitions.Add(column);
        Grid.SetColumn(chevron, grid.ColumnDefinitions.Count - 1);
        grid.Children.Add(chevron);
    }

    private static void AddRibbonDropdownChevronToStack(StackPanel stack, RibbonCommandContentLayout layout)
    {
        var chevron = CreateRibbonDropdownChevron(layout);
        if (layout is RibbonCommandContentLayout.Large or RibbonCommandContentLayout.Medium)
        {
            chevron.Margin = stack.Orientation == Orientation.Horizontal
                ? new Thickness(4, 0, 0, 0)
                : new Thickness(0, 0, 0, 0);
        }

        stack.Children.Add(chevron);
    }

    private static FrameworkElement CreateRibbonDropdownChevron(RibbonCommandContentLayout layout)
    {
        var chevron = CreateRibbonChevronGlyph(
            width: 10,
            height: 8,
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
            button.ContextMenu is not { } menu ||
            !button.IsEnabled ||
            !IsRibbonDropdownZoneClick(button, e.GetPosition(button)))
        {
            return;
        }

        e.Handled = true;
        OpenRibbonContextMenu(button, menu);
    }

    private static bool IsRibbonDropdownZoneClick(ButtonBase button, Point position)
    {
        return TryGetRibbonDropdownZoneBounds(button, out var bounds) &&
               bounds.Contains(position);
    }

    private static bool TryGetRibbonDropdownZoneBounds(ButtonBase button, out Rect bounds)
    {
        bounds = Rect.Empty;
        var width = button.ActualWidth;
        var height = button.ActualHeight;
        if (width <= 0 || height <= 0)
            return false;

        var layout = GetRibbonDropdownZoneLayout(button);
        bounds = layout switch
        {
            RibbonCommandContentLayout.Large or RibbonCommandContentLayout.Medium =>
                new Rect(0, Math.Max(0, height - 20), width, Math.Min(20, height)),
            RibbonCommandContentLayout.IconOnly =>
                new Rect(Math.Max(0, width - 16), Math.Max(0, height - 16), Math.Min(16, width), Math.Min(16, height)),
            _ => new Rect(Math.Max(0, width - 18), 0, Math.Min(18, width), height)
        };

        if (TryGetRibbonDropdownChevronBounds(button, out var chevronBounds))
            bounds.Union(chevronBounds);

        return bounds is { Width: > 0, Height: > 0 };
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
                bounds.Inflate(7, 7);
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

    private static IEnumerable<DependencyObject> EnumerateRibbonStaticDescendants(DependencyObject root) =>
        EnumerateVisualDescendants(root)
            .Concat(EnumerateLogicalDescendants(root))
            .Distinct();

    private static RibbonStaticSurfaceSnapshot CaptureRibbonStaticSurface(DependencyObject root)
    {
        var descendants = EnumerateRibbonStaticDescendants(root).ToList();
        return new RibbonStaticSurfaceSnapshot(descendants);
    }

    private sealed class RibbonStaticSurfaceSnapshot
    {
        public RibbonStaticSurfaceSnapshot(IReadOnlyList<DependencyObject> descendants)
        {
            Descendants = descendants;
            ButtonBases = descendants.OfType<ButtonBase>().ToList();
            Buttons = descendants.OfType<Button>().ToList();
            Grids = descendants.OfType<Grid>().ToList();
            StackPanels = descendants.OfType<StackPanel>().ToList();
            ComboBoxes = descendants.OfType<ComboBox>().ToList();
            ScrollViewers = descendants.OfType<ScrollViewer>().ToList();
        }

        public IReadOnlyList<DependencyObject> Descendants { get; }
        public IReadOnlyList<ButtonBase> ButtonBases { get; }
        public IReadOnlyList<Button> Buttons { get; }
        public IReadOnlyList<Grid> Grids { get; }
        public IReadOnlyList<StackPanel> StackPanels { get; }
        public IReadOnlyList<ComboBox> ComboBoxes { get; }
        public IReadOnlyList<ScrollViewer> ScrollViewers { get; }
    }

    private void NormalizeRibbonSurfaceAfterTabSelection()
    {
        _ribbonResizeNormalizationRequired = true;
        NormalizeRibbonSurfaceAfterLayoutChange(prepareSelectedTab: true, scheduleFallback: true);
    }

    private void ChangeRibbonSelectionWithoutTabNormalization(Action changeSelection)
    {
        var previous = _suppressRibbonSelectionChangedNormalization;
        _suppressRibbonSelectionChangedNormalization = true;
        try
        {
            changeSelection();
        }
        finally
        {
            _suppressRibbonSelectionChangedNormalization = previous;
        }
    }

    private void NormalizeRibbonSurfaceAfterResize()
    {
        if (!ShouldNormalizeRibbonSurfaceForResize())
            return;

        CompactRibbonSurfaceAfterResize(scheduleFallback: !_isInWindowResizeMoveLoop);
    }

    private void NormalizeRibbonSurfaceAfterLayoutChange()
        => NormalizeRibbonSurfaceAfterLayoutChange(prepareSelectedTab: false, scheduleFallback: true);

    private void NormalizeRibbonSurfaceAfterLayoutChange(bool prepareSelectedTab, bool scheduleFallback)
    {
        if (prepareSelectedTab)
            PrepareSelectedRibbonTabForImmediateCompaction();

        NormalizeRibbonSurface(forceCompact: true);
        UpdateActiveRibbonLayoutBeforeFirstFrame();
        if (scheduleFallback)
            QueueRibbonFallback(RibbonFallbackWork.NormalizeSurface);
    }

    private void CompactRibbonSurfaceAfterResize(bool scheduleFallback)
    {
        if (!scheduleFallback)
        {
            _ribbonResizeCompactionPendingOnExit = true;
            return;
        }

        UpdateRibbonCompactMode(force: true);
        UpdateActiveRibbonLayoutBeforeFirstFrame();
        QueueRibbonFallback(RibbonFallbackWork.CompactOnly);
    }

    private void QueueRibbonFallback(RibbonFallbackWork work)
    {
        if (work == RibbonFallbackWork.None)
            return;

        _ribbonFallbackRequestCount++;
        _lastRibbonFallbackRequestedWork = work;
        _ribbonFallbackWork = MergeRibbonFallbackWork(_ribbonFallbackWork, work);
        _lastRibbonFallbackMergedWork = _ribbonFallbackWork;
        if (_ribbonFallbackPending)
            return;

        _ribbonFallbackPending = true;
        _ribbonFallbackPostedCount++;
        Dispatcher.BeginInvoke(
            (Action)(() =>
            {
                var pendingWork = _ribbonFallbackWork;
                _ribbonFallbackWork = RibbonFallbackWork.None;
                _ribbonFallbackPending = false;
                _ribbonFallbackExecutedCount++;
                _lastRibbonFallbackExecutedWork = pendingWork;

                if (pendingWork == RibbonFallbackWork.NormalizeSurface)
                {
                    _ribbonFallbackForcedNormalizeCount++;
                    NormalizeRibbonSurface(forceCompact: true);
                    UpdateActiveRibbonLayoutBeforeFirstFrame();
                }
                else if (pendingWork == RibbonFallbackWork.CompactOnly)
                {
                    _ribbonFallbackForcedCompactCount++;
                    UpdateRibbonCompactMode(force: false);
                    UpdateActiveRibbonLayoutBeforeFirstFrame();
                }
            }),
            DispatcherPriority.Render);
    }

    internal RibbonFallbackDiagnosticsSnapshot GetRibbonFallbackDiagnosticsForTests() =>
        new(
            _ribbonFallbackRequestCount,
            _ribbonFallbackPostedCount,
            _ribbonFallbackExecutedCount,
            _ribbonFallbackForcedNormalizeCount,
            _ribbonFallbackForcedCompactCount,
            _ribbonFirstFrameLayoutUpdateCount,
            _lastRibbonFallbackRequestedWork.ToString(),
            _lastRibbonFallbackMergedWork.ToString(),
            _lastRibbonFallbackExecutedWork.ToString(),
            _ribbonFallbackPending,
            _ribbonResizeCompactionPendingOnExit);

    internal void ResetRibbonFallbackDiagnosticsForTests()
    {
        _ribbonFallbackRequestCount = 0;
        _ribbonFallbackPostedCount = 0;
        _ribbonFallbackExecutedCount = 0;
        _ribbonFallbackForcedNormalizeCount = 0;
        _ribbonFallbackForcedCompactCount = 0;
        _ribbonFirstFrameLayoutUpdateCount = 0;
        _lastRibbonFallbackRequestedWork = RibbonFallbackWork.None;
        _lastRibbonFallbackMergedWork = RibbonFallbackWork.None;
        _lastRibbonFallbackExecutedWork = RibbonFallbackWork.None;
    }

    private static RibbonFallbackWork MergeRibbonFallbackWork(RibbonFallbackWork current, RibbonFallbackWork requested) =>
        current == RibbonFallbackWork.NormalizeSurface || requested == RibbonFallbackWork.NormalizeSurface
            ? RibbonFallbackWork.NormalizeSurface
            : current == RibbonFallbackWork.CompactOnly || requested == RibbonFallbackWork.CompactOnly
                ? RibbonFallbackWork.CompactOnly
                : RibbonFallbackWork.None;

    private void CompleteRibbonResizeCompaction()
    {
        if (_ribbonResizeCompactionPendingOnExit)
        {
            _ribbonResizeCompactionPendingOnExit = false;
            UpdateRibbonCompactMode(force: true);
            UpdateActiveRibbonLayoutBeforeFirstFrame();
            QueueRibbonFallback(RibbonFallbackWork.CompactOnly);
        }

        var width = GetCurrentRibbonResizeWidth();
        if (width > 0 && !double.IsNaN(width))
            _lastRibbonResizeWidth = width;
    }

    private bool ShouldNormalizeRibbonSurfaceForResize()
    {
        var width = GetCurrentRibbonResizeWidth();
        if (width <= 0 || double.IsNaN(width))
            return true;

        if (double.IsNaN(_lastRibbonResizeWidth))
        {
            _lastRibbonResizeWidth = width;
            return true;
        }

        if (_ribbonResizeNormalizationRequired)
        {
            _ribbonResizeNormalizationRequired = false;
            _lastRibbonResizeWidth = width;
            return true;
        }

        var previousWidth = _lastRibbonResizeWidth;
        _lastRibbonResizeWidth = width;
        if (_ribbonResizeThresholds.Count == 0)
            return true;

        return RibbonResizeThresholdGate.CrossedAnyThreshold(previousWidth, width, _ribbonResizeThresholds);
    }

    private double GetCurrentRibbonResizeWidth()
    {
        if (RibbonTabs is null)
            return 0;

        if (TryGetCachedRibbonResizeWidth(out var cachedWidth))
            return cachedWidth;

        if (GetActiveRibbonPanel() is { } activePanel &&
            FindVisualAncestor<ScrollViewer>(activePanel) is { } scrollViewer)
        {
            var width = scrollViewer.ActualWidth > 0 ? scrollViewer.ActualWidth : scrollViewer.ViewportWidth;
            if (width > 0)
                return RibbonTabs.ActualWidth > 0
                    ? Math.Min(width, Math.Max(0, RibbonTabs.ActualWidth - 12))
                    : width;
        }

        return RibbonTabs.ActualWidth;
    }

    private bool TryGetCachedRibbonResizeWidth(out double width)
    {
        width = 0;
        if (_ribbonAdaptiveControlCachePanel is not { IsVisible: true } ||
            _ribbonAdaptiveScrollViewerCache is not { } scrollViewer ||
            !IsCachedRibbonSurfaceSelected())
        {
            return false;
        }

        width = scrollViewer.ActualWidth > 0 ? scrollViewer.ActualWidth : scrollViewer.ViewportWidth;
        if (width <= 0)
            return false;

        if (RibbonTabs.ActualWidth > 0)
            width = Math.Min(width, Math.Max(0, RibbonTabs.ActualWidth - 12));

        return width > 0;
    }

    private bool IsCachedRibbonSurfaceSelected()
    {
        if (RibbonTabs?.SelectedItem is not TabItem selectedTab ||
            _ribbonAdaptiveControlCachePanel is not { } cachedPanel)
        {
            return false;
        }

        return ReferenceEquals(FindVisualAncestor<TabItem>(cachedPanel), selectedTab);
    }

    private void PrepareSelectedRibbonTabForImmediateCompaction()
    {
        if (RibbonTabs?.SelectedItem is not TabItem tabItem)
            return;

        PrepareRibbonTabForImmediateCompaction(tabItem);
    }

    private static void PrepareRibbonTabForImmediateCompaction(TabItem tabItem, bool forceLayout = false)
    {
        tabItem.ApplyTemplate();
        if (tabItem.Content is FrameworkElement content)
        {
            content.ApplyTemplate();
            UpdateRibbonLayoutIfNeeded(content, force: forceLayout);
        }
    }

    private static void UpdateRibbonLayoutIfNeeded(FrameworkElement element, bool force = false)
    {
        if (force ||
            !element.IsMeasureValid ||
            !element.IsArrangeValid ||
            (element.IsVisible && (element.ActualWidth <= 0 || element.ActualHeight <= 0)))
        {
            element.UpdateLayout();
        }
    }

    private void UpdateActiveRibbonLayoutBeforeFirstFrame()
    {
        if (RibbonTabs?.SelectedItem is not TabItem tabItem)
            return;

        if (GetRibbonTabContentRoot(tabItem) is FrameworkElement content)
        {
            content.ApplyTemplate();
            content.UpdateLayout();
            _ribbonFirstFrameLayoutUpdateCount++;
            return;
        }

        if (GetActiveRibbonPanel() is { } activePanel)
        {
            activePanel.UpdateLayout();
            _ribbonFirstFrameLayoutUpdateCount++;
        }
    }

    private void ConfigureInsertRibbonSurface(RibbonStaticSurfaceSnapshot surface)
    {
        if (RibbonTabs?.SelectedItem is not TabItem selectedTab ||
            !string.Equals(selectedTab.Header?.ToString(), "Insert", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var button in surface.Buttons)
        {
            var title = GetRibbonButtonCommandName(button);
            var groupName = FindRibbonOwningGroupName(button);
            if ((string.Equals(groupName, "Charts", StringComparison.Ordinal) &&
                 !RibbonCommandPresentationPlanner.IsInsertRibbonChartCommand(title)) ||
                RibbonCommandPresentationPlanner.ShouldHideFromInsertRibbon(title))
            {
                button.Visibility = Visibility.Collapsed;
            }
        }
    }

    private static string FindRibbonOwningGroupName(DependencyObject element)
    {
        var current = element;
        while (current is not null)
        {
            if (RibbonMetadata.TryGetGroupName(current, out var groupName))
            {
                return groupName;
            }

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return "";
    }

    private static string GetRibbonButtonCommandName(ButtonBase button)
    {
        if (RibbonMetadata.TryGetCommandName(button, out var commandName))
            return commandName;

        return GetRibbonButtonTitleOrLabel(button);
    }

    private static string GetRibbonButtonTitleOrLabel(ButtonBase button)
    {
        var title = RibbonTooltip.GetTitle(button);
        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        if (button.Content is string text && !string.IsNullOrWhiteSpace(text))
            return text.Trim();

        var label = FindRibbonContentLabel(button.Content);

        return label ?? "";
    }

    private static string GetRibbonButtonDisplayLabel(ButtonBase button)
    {
        if (button.Content is string text && !string.IsNullOrWhiteSpace(text))
            return text.Trim();

        if (FindRibbonContentLabel(button.Content) is { } label)
            return label;

        var title = RibbonTooltip.GetTitle(button);
        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        return RibbonMetadata.TryGetCommandName(button, out var commandName) ? commandName : "";
    }

    private static string? FindRibbonContentLabel(object? content)
    {
        if (content is TextBlock textBlock &&
            RibbonMetadata.IsCommandLabel(textBlock) &&
            !string.IsNullOrWhiteSpace(textBlock.Text))
        {
            return textBlock.Text.Trim();
        }

        if (content is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (FindRibbonContentLabel(child) is { } label)
                    return label;
            }
        }

        if (content is ContentControl contentControl &&
            !ReferenceEquals(contentControl.Content, content))
        {
            return FindRibbonContentLabel(contentControl.Content);
        }

        return null;
    }

    private void AlignRibbonIconColumns(RibbonStaticSurfaceSnapshot surface)
    {
        foreach (var stack in surface.StackPanels)
        {
            if (RibbonMetadata.TryGetCommandContentLayout(stack, out _))
                continue;

            if (stack.Orientation != Orientation.Horizontal || stack.Children.Count < 2)
                continue;

            var label = stack.Children
                .OfType<TextBlock>()
                .FirstOrDefault(RibbonMetadata.IsCommandLabel);
            if (label is null)
                continue;

            var labelIndex = stack.Children.IndexOf(label);
            var icon = stack.Children
                .OfType<FrameworkElement>()
                .Take(labelIndex >= 0 ? labelIndex : stack.Children.Count)
                .LastOrDefault(element => !ReferenceEquals(element, label));
            if (icon is null)
                continue;

            if (FindVisualAncestor<ButtonBase>(stack) is null)
                continue;

            if (icon is not Image)
                icon.Width = Math.Max(icon.Width is > 0 ? icon.Width : 0, 18);
            icon.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            icon.Margin = new Thickness(0, icon.Margin.Top, 4, icon.Margin.Bottom);
            label.MinWidth = Math.Max(label.MinWidth, 84);
            label.FontSize = Math.Max(label.FontSize, 12);
            label.TextTrimming = TextTrimming.None;
            label.TextWrapping = TextWrapping.NoWrap;
            stack.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        }
    }

    private void NormalizeExistingRibbonIconText(RibbonStaticSurfaceSnapshot surface)
    {
        foreach (var button in surface.ButtonBases)
        {
            if (TryNormalizeHomeCompactIconButton(button))
                continue;

            if (TryNormalizeHomeSmallCommandButton(button))
                continue;

            if (TryNormalizeStaticRibbonCommandButton(button))
                continue;

            var tall = button is FrameworkElement element && element.Height >= 46;
            ReplaceRibbonGlyphIcons(button.Content, button, tall);
            NormalizeRibbonButtonSizeForCommandIcons(button, tall);
            foreach (var textBlock in EnumerateRibbonTextContent(button.Content))
            {
                if (RibbonMetadata.IsCommandLabel(textBlock))
                {
                    textBlock.FontSize = 12;
                    textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
                    textBlock.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                    if (tall)
                    {
                        textBlock.TextAlignment = TextAlignment.Center;
                        textBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                    }

                    continue;
                }

                var isIcon = RibbonMetadata.IsCommandIcon(textBlock);
                if (!isIcon)
                    continue;

                RibbonMetadata.SetRole(textBlock, RibbonMetadataRole.CommandIcon);
                textBlock.FontSize = tall ? 22 : Math.Max(12, textBlock.FontSize);
                textBlock.Width = tall ? Math.Max(24, textBlock.Width) : Math.Max(16, textBlock.Width);
                textBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                textBlock.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                textBlock.TextAlignment = TextAlignment.Center;
            }
        }
    }

    private bool TryNormalizeHomeCompactIconButton(ButtonBase button)
    {
        if (HomeRibbonPanel is null ||
            !ReferenceEquals(FindHomeRibbonAncestor(button), HomeRibbonPanel) ||
            button is not FrameworkElement element ||
            element.Width > 46 ||
            element.Height > 32)
        {
            return false;
        }

        var commandName = GetRibbonButtonCommandName(button);
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        element.Width = 26;
        element.Height = 26;
        SetRibbonCompactWidths(button, 26, 24);
        element.Margin = new Thickness(1, 0, 1, 0);
        if (button is Control control)
            control.Padding = new Thickness(1);

        button.Content = CreateRibbonIconOnlyContent(commandName, 22);
        button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
        button.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
        return true;
    }

    private bool TryNormalizeHomeSmallCommandButton(ButtonBase button)
    {
        if (HomeRibbonPanel is null ||
            !ReferenceEquals(FindHomeRibbonAncestor(button), HomeRibbonPanel) ||
            button.Content is string ||
            ContainsUnreplacedRibbonIcon(button.Content) ||
            button is not FrameworkElement element ||
            element.Height > 34)
        {
            return false;
        }

        var commandName = GetRibbonButtonCommandName(button);
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        var label = GetRibbonButtonDisplayLabel(button);
        element.Width = GetSmallRibbonCommandWidth(label);
        element.Height = 24;
        SetRibbonCompactWidths(button, element.Width, 24);
        if (button is Control control)
            control.Padding = new Thickness(4, 2, 4, 2);
        button.Content = CreateRibbonCommandContent(commandName, label, RibbonCommandLayoutKind.Small);
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
        button.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
        return true;
    }

    private static StackPanel? FindHomeRibbonAncestor(DependencyObject element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is StackPanel { Name: "HomeRibbonPanel" } stack)
                return stack;

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private bool TryNormalizeStaticRibbonCommandButton(ButtonBase button)
    {
        if (RibbonTabs is null ||
            button is not Button commandButton ||
            IsRibbonCollapsedGroupButton(commandButton) ||
            IsRibbonCommandContent(commandButton.Content) ||
            (!ContainsUnreplacedRibbonIcon(commandButton.Content) &&
             !ContainsRibbonCommandLabel(commandButton.Content)))
        {
            return false;
        }

        var hadUnreplacedIcon = ContainsUnreplacedRibbonIcon(commandButton.Content);
        var hadRibbonCommandLabel = ContainsRibbonCommandLabel(commandButton.Content);
        var commandName = GetRibbonButtonCommandName(commandButton);
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        var label = GetRibbonButtonDisplayLabel(commandButton);
        var layoutKind = IsFixedHeightIconOnlyRibbonButton(commandButton, hadUnreplacedIcon, hadRibbonCommandLabel) ||
                         (!hadUnreplacedIcon &&
                          hadRibbonCommandLabel &&
                          commandButton.Height is > 0 and <= 34)
            ? RibbonCommandLayoutKind.Small
            : RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label);
        ApplyRibbonCommandSize(commandButton, layoutKind);
        if (layoutKind is RibbonCommandLayoutKind.Small)
        {
            commandButton.Width = Math.Max(commandButton.Width is > 0 ? commandButton.Width : 0, GetSmallRibbonCommandWidth(label));
            if (!hadUnreplacedIcon && hadRibbonCommandLabel)
                commandButton.Width = Math.Max(commandButton.Width, GetIconLabelRowRibbonCommandWidth(label));
        }
        SetRibbonCompactWidths(
            commandButton,
            commandButton.Width is > 0 ? commandButton.Width : Math.Max(commandButton.ActualWidth, 64),
            layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium ? 38 : 24);

        commandButton.Content = CreateRibbonCommandContent(commandName, label, layoutKind);
        if (!hadUnreplacedIcon && hadRibbonCommandLabel && commandButton.Content is DependencyObject contentRoot)
        {
            foreach (var textBlock in EnumerateVisualDescendants(contentRoot)
                         .Concat(EnumerateLogicalDescendants(contentRoot))
                         .OfType<TextBlock>()
                         .Distinct()
                         .Where(RibbonMetadata.IsCommandLabel))
            {
                textBlock.Uid = "RibbonCompactRowLabel";
                textBlock.FontSize = 12;
            }
        }

        if (layoutKind is RibbonCommandLayoutKind.Small)
            commandButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        commandButton.HorizontalContentAlignment = layoutKind is RibbonCommandLayoutKind.Small
            ? System.Windows.HorizontalAlignment.Left
            : System.Windows.HorizontalAlignment.Center;
        return true;
    }

    private static bool IsRibbonCommandContent(object? content)
    {
        return content is DependencyObject element &&
               RibbonMetadata.TryGetCommandContentLayout(element, out _);
    }

    private static bool IsFixedHeightIconOnlyRibbonButton(
        ButtonBase button,
        bool hadUnreplacedIcon,
        bool hadRibbonCommandLabel)
    {
        return hadUnreplacedIcon &&
               !hadRibbonCommandLabel &&
               button is FrameworkElement { Height: > 0 and <= 34 };
    }

    private static bool ContainsRibbonCommandLabel(object? content)
    {
        switch (content)
        {
            case TextBlock textBlock:
                return RibbonMetadata.IsCommandLabel(textBlock) &&
                       !string.IsNullOrWhiteSpace(textBlock.Text);
            case Panel panel:
                return panel.Children.Cast<object>().Any(ContainsRibbonCommandLabel);
            case Decorator decorator:
                return ContainsRibbonCommandLabel(decorator.Child);
            case ContentControl contentControl when !ReferenceEquals(contentControl.Content, content):
                return ContainsRibbonCommandLabel(contentControl.Content);
            default:
                return false;
        }
    }

    private static void NormalizeRibbonButtonSizeForCommandIcons(ButtonBase button, bool tall)
    {
        if (button is not FrameworkElement element || !ContainsRibbonCommandIcon(button.Content))
            return;

        if (tall)
        {
            var tallLabel = GetRibbonButtonDisplayLabel(button);
            element.Width = Math.Max(element.Width is > 0 ? element.Width : 0, GetLargeRibbonCommandWidth(tallLabel));
            element.Height = Math.Max(element.Height is > 0 ? element.Height : 0, 76);
            SetRibbonCompactWidths(button, element.Width, 38);
            return;
        }

        var label = FindRibbonContentLabel(button.Content);
        if (string.IsNullOrWhiteSpace(label))
            return;

        var minWidth = label.Length switch
        {
            <= 3 => 58,
            <= 6 => 66,
            <= 10 => 92,
            <= 14 => 126,
            _ => Math.Min(150, 44 + label.Length * 6)
        };

        element.Width = Math.Max(element.Width is > 0 ? element.Width : 0, minWidth);
        element.Height = Math.Max(element.Height is > 0 ? element.Height : 0, 24);
        SetRibbonCompactWidths(button, element.Width, 24);
    }

    private static void SetRibbonCompactWidths(ButtonBase button, double fullWidth, double compactWidth)
    {
        RibbonMetadata.SetCompactWidths(button, fullWidth, compactWidth);
    }

    private static bool ContainsRibbonCommandIcon(object? content)
    {
        switch (content)
        {
            case FrameworkElement element when RibbonMetadata.IsCommandIcon(element):
                return true;
            case Panel panel:
                return panel.Children.Cast<object>().Any(ContainsRibbonCommandIcon);
            case Decorator decorator:
                return ContainsRibbonCommandIcon(decorator.Child);
            case ContentControl contentControl when !ReferenceEquals(contentControl.Content, content):
                return ContainsRibbonCommandIcon(contentControl.Content);
            default:
                return false;
        }
    }

    private static bool ContainsUnreplacedRibbonIcon(object? content)
    {
        switch (content)
        {
            case RibbonIcon:
                return true;
            case Panel panel:
                return panel.Children.Cast<object>().Any(ContainsUnreplacedRibbonIcon);
            case Decorator decorator:
                return ContainsUnreplacedRibbonIcon(decorator.Child);
            case ContentControl contentControl when !ReferenceEquals(contentControl.Content, content):
                return ContainsUnreplacedRibbonIcon(contentControl.Content);
            default:
                return false;
        }
    }

    private static void ReplaceRibbonGlyphIcons(object? content, ButtonBase owner, bool tall)
    {
        switch (content)
        {
            case null:
                return;
            case RibbonIcon ribbonIcon:
                owner.Content = CreateStaticRibbonCommandIcon(owner, ribbonIcon, tall);
                return;
            case TextBlock textBlock when IsRibbonIconTextBlock(textBlock):
                owner.Content = CreateStaticRibbonVectorIcon(owner, textBlock, tall);
                return;
            case Panel panel:
                for (var i = 0; i < panel.Children.Count; i++)
                {
                    if (panel.Children[i] is RibbonIcon childRibbonIcon)
                    {
                        var replacement = CreateStaticRibbonCommandIcon(owner, childRibbonIcon, tall);
                        panel.Children.RemoveAt(i);
                        panel.Children.Insert(i, replacement);
                        continue;
                    }

                    if (panel.Children[i] is TextBlock childText && IsRibbonIconTextBlock(childText))
                    {
                        var replacement = CreateStaticRibbonVectorIcon(owner, childText, tall);
                        panel.Children.RemoveAt(i);
                        panel.Children.Insert(i, replacement);
                        continue;
                    }

                    ReplaceRibbonGlyphIcons(panel.Children[i], owner, tall);
                }

                return;
            case Decorator decorator:
                if (decorator.Child is RibbonIcon decoratorRibbonIcon)
                    decorator.Child = CreateStaticRibbonCommandIcon(owner, decoratorRibbonIcon, tall);
                else if (decorator.Child is TextBlock decoratorText && IsRibbonIconTextBlock(decoratorText))
                    decorator.Child = CreateStaticRibbonVectorIcon(owner, decoratorText, tall);
                else
                    ReplaceRibbonGlyphIcons(decorator.Child, owner, tall);
                return;
            case ContentControl contentControl when !ReferenceEquals(contentControl, owner):
                if (contentControl.Content is RibbonIcon contentRibbonIcon)
                    contentControl.Content = CreateStaticRibbonCommandIcon(owner, contentRibbonIcon, tall);
                else if (contentControl.Content is TextBlock contentText && IsRibbonIconTextBlock(contentText))
                    contentControl.Content = CreateStaticRibbonVectorIcon(owner, contentText, tall);
                else
                    ReplaceRibbonGlyphIcons(contentControl.Content, owner, tall);
                return;
        }
    }

    private static bool IsRibbonIconTextBlock(TextBlock textBlock)
    {
        return RibbonMetadata.IsCommandIcon(textBlock);
    }

    private static FrameworkElement CreateStaticRibbonCommandIcon(ButtonBase owner, RibbonIcon source, bool tall)
    {
        var commandName = !string.IsNullOrWhiteSpace(source.CommandName)
            ? source.CommandName.Trim()
            : source.Kind == RibbonCommandIconKind.Previous
            ? "Back to workbook"
            : GetStaticRibbonIconCommandName(owner, source.Kind.ToString());
        var fallbackIcon = new RibbonCommandIcon(source.Kind);
        var iconSize = IsWhiteBrush(source.Foreground) ? source.IconSize : tall ? 32 : 22;
        var commandIcon = RibbonIconFactory.CreateCommandIcon(
            commandName,
            fallbackIcon,
            iconSize,
            source.Foreground ?? owner.Foreground);
        RibbonMetadata.SetRole(commandIcon, RibbonMetadataRole.CommandIcon);
        commandIcon.HorizontalAlignment = source.HorizontalAlignment;
        commandIcon.VerticalAlignment = source.VerticalAlignment;
        commandIcon.Margin = source.Margin;
        return commandIcon;
    }

    private static FrameworkElement CreateStaticRibbonVectorIcon(ButtonBase owner, TextBlock source, bool tall)
    {
        var commandName = GetStaticRibbonIconCommandName(owner, source.Text);
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);
        var iconSize = tall ? 32 : 22;
        var commandIcon = RibbonIconFactory.CreateCommandIcon(commandName, icon, iconSize, source.Foreground);
        RibbonMetadata.SetRole(commandIcon, RibbonMetadataRole.CommandIcon);
        commandIcon.HorizontalAlignment = source.HorizontalAlignment;
        commandIcon.VerticalAlignment = source.VerticalAlignment;
        commandIcon.Margin = source.Margin;
        return commandIcon;
    }

    private static string GetStaticRibbonIconCommandName(ButtonBase owner, string fallback)
    {
        if (RibbonMetadata.TryGetCommandName(owner, out var commandName))
            return commandName;

        var title = owner is FrameworkElement element
            ? RibbonTooltip.GetTitle(element)
            : null;
        if (!string.IsNullOrWhiteSpace(title))
            return title;

        if (!string.IsNullOrWhiteSpace(owner.Name))
        {
            var name = owner.Name;
            foreach (var suffix in new[] { "Button", "Btn" })
            {
                if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
                {
                    name = name[..^suffix.Length];
                    break;
                }
            }

            return name;
        }

        return fallback switch
        {
            nameof(RibbonCommandIconKind.WindowMinimize) => "Minimize",
            nameof(RibbonCommandIconKind.WindowMaximize) => "Maximize",
            nameof(RibbonCommandIconKind.WindowClose) => "Close",
            nameof(RibbonCommandIconKind.Previous) => "Back to workbook",
            _ => fallback
        };
    }

    private static bool IsWhiteBrush(Brush brush)
    {
        return brush is SolidColorBrush solid &&
               solid.Color.R >= 245 &&
               solid.Color.G >= 245 &&
               solid.Color.B >= 245;
    }


    private static IEnumerable<TextBlock> EnumerateRibbonTextContent(object? content)
    {
        if (content is TextBlock textBlock)
        {
            yield return textBlock;
            yield break;
        }

        if (content is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                foreach (var text in EnumerateRibbonTextContent(child))
                    yield return text;
            }
        }
        else if (content is ContentControl contentControl &&
                 !ReferenceEquals(contentControl.Content, content))
        {
            foreach (var text in EnumerateRibbonTextContent(contentControl.Content))
                yield return text;
        }
        else if (content is Decorator decorator)
        {
            foreach (var text in EnumerateRibbonTextContent(decorator.Child))
                yield return text;
        }
    }

    private void ApplyToolbarDropdownWhiteBackgrounds(RibbonStaticSurfaceSnapshot surface)
    {
        foreach (var comboBox in surface.ComboBoxes)
        {
            comboBox.Background = Brushes.White;
            comboBox.Foreground = Brushes.Black;
            comboBox.Resources[SystemColors.WindowBrushKey] = Brushes.White;
            comboBox.Resources[SystemColors.ControlBrushKey] = Brushes.White;
            comboBox.Resources[SystemColors.MenuBrushKey] = Brushes.White;
            comboBox.DropDownOpened -= ToolbarComboBox_DropDownOpened;
            comboBox.DropDownOpened += ToolbarComboBox_DropDownOpened;
        }
    }

    private static void ToolbarComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        comboBox.Dispatcher.BeginInvoke((Action)(() =>
        {
            comboBox.ApplyTemplate();
            if (comboBox.Template.FindName("PART_Popup", comboBox) is not Popup popup ||
                popup.Child is not DependencyObject popupRoot)
            {
                return;
            }

            ForceDropdownWhite(popupRoot);
        }));
    }

    private static void ForceDropdownWhite(DependencyObject root)
    {
        if (root is Control control)
        {
            control.Background = Brushes.White;
            control.Foreground = Brushes.Black;
        }
        else if (root is Border border)
        {
            border.Background = Brushes.White;
        }
        else if (root is Panel panel)
        {
            panel.Background = Brushes.White;
        }

        if (root is ComboBoxItem item)
        {
            item.Background = Brushes.White;
            item.Foreground = Brushes.Black;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            ForceDropdownWhite(VisualTreeHelper.GetChild(root, i));
    }

    private static FrameworkElement CreateRibbonCommandContent(string commandName, string label, RibbonCommandLayoutKind layoutKind)
    {
        var tall = layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium;
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);
        var (slotBackground, slotBorder, glyphBrush) = GetRibbonIconAccentBrushes(icon.Accent);
        var iconSize = layoutKind == RibbonCommandLayoutKind.Large ? 32 : 22;
        var slotSize = layoutKind == RibbonCommandLayoutKind.Large ? 34 : 24;
        var iconSlot = new Border
        {
            Width = slotSize,
            Height = slotSize,
            CornerRadius = tall ? new CornerRadius(3) : new CornerRadius(2),
            Background = slotBackground,
            BorderBrush = slotBorder,
            BorderThickness = slotBorder is null ? new Thickness(0) : new Thickness(1),
            Child = RibbonIconFactory.CreateCommandIcon(commandName, icon, iconSize, glyphBrush),
            SnapsToDevicePixels = true,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = tall ? new Thickness(0, 0, 0, 2) : new Thickness(0, 0, 5, 0)
        };
        RibbonMetadata.SetRole(iconSlot, RibbonMetadataRole.CommandIcon);

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.Normal,
            TextWrapping = tall ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxWidth = tall ? 96 : double.PositiveInfinity,
            TextTrimming = tall ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            HorizontalAlignment = tall ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            TextAlignment = tall ? TextAlignment.Center : TextAlignment.Left,
            LineHeight = tall ? 14 : double.NaN
        };
        RibbonMetadata.SetRole(labelBlock, RibbonMetadataRole.CommandLabel);

        var contentLayout = layoutKind == RibbonCommandLayoutKind.Large
            ? RibbonCommandContentLayout.Large
            : layoutKind == RibbonCommandLayoutKind.Medium
                ? RibbonCommandContentLayout.Medium
                : RibbonCommandContentLayout.Small;

        if (tall)
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Children =
                {
                    iconSlot,
                    labelBlock
                }
            };
            RibbonMetadata.SetCommandContentLayout(stack, contentLayout);
            return stack;
        }

        var compactGrid = new Grid
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        RibbonMetadata.SetCommandContentLayout(compactGrid, contentLayout);
        var iconColumn = new ColumnDefinition { Width = new GridLength(slotSize) };
        var spacerColumn = new ColumnDefinition { Width = new GridLength(5) };
        var labelColumn = new ColumnDefinition { Width = GridLength.Auto };
        RibbonMetadata.SetRole(spacerColumn, RibbonMetadataRole.CommandSpacer);
        compactGrid.ColumnDefinitions.Add(iconColumn);
        compactGrid.ColumnDefinitions.Add(spacerColumn);
        compactGrid.ColumnDefinitions.Add(labelColumn);

        iconSlot.Margin = new Thickness(0);
        labelBlock.Margin = new Thickness(0);
        Grid.SetColumn(iconSlot, 0);
        Grid.SetColumn(labelBlock, 2);
        compactGrid.Children.Add(iconSlot);
        compactGrid.Children.Add(labelBlock);
        return compactGrid;
    }

    private static FrameworkElement CreateRibbonIconOnlyContent(string commandName, double iconSize)
    {
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);
        var (_, _, glyphBrush) = GetRibbonIconAccentBrushes(icon.Accent);
        var iconElement = RibbonIconFactory.CreateCommandIcon(commandName, icon, iconSize, glyphBrush);
        RibbonMetadata.SetRole(iconElement, RibbonMetadataRole.CommandIcon);

        var grid = new Grid
        {
            Width = 24,
            Height = 24,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Children = { iconElement }
        };
        RibbonMetadata.SetCommandContentLayout(grid, RibbonCommandContentLayout.IconOnly);
        return grid;
    }

    private static (Brush? SlotBackground, Brush? SlotBorder, Brush GlyphBrush) GetRibbonIconAccentBrushes(
        RibbonCommandIconAccent accent)
    {
        static (Brush? SlotBackground, Brush? SlotBorder, Brush GlyphBrush) Glyph(byte r, byte g, byte b) =>
            (Brushes.Transparent, null, BrushFromRgb(r, g, b));

        return accent switch
        {
            RibbonCommandIconAccent.Green => Glyph(23, 50, 77),
            RibbonCommandIconAccent.Chart => Glyph(47, 84, 150),
            RibbonCommandIconAccent.Data => Glyph(0, 92, 135),
            RibbonCommandIconAccent.Theme => Glyph(85, 35, 125),
            RibbonCommandIconAccent.Fill => Glyph(116, 88, 0),
            RibbonCommandIconAccent.Color => Glyph(150, 0, 0),
            RibbonCommandIconAccent.Border => Glyph(31, 31, 31),
            RibbonCommandIconAccent.Warning => Glyph(138, 91, 0),
            RibbonCommandIconAccent.Protect => Glyph(23, 50, 77),
            RibbonCommandIconAccent.Help => Glyph(47, 84, 150),
            _ => (Brushes.Transparent, null, Brushes.Black)
        };
    }

    private static SolidColorBrush BrushFromRgb(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    private static void ApplyRibbonCommandSize(ButtonBase button, RibbonCommandLayoutKind layoutKind)
    {
        switch (layoutKind)
        {
            case RibbonCommandLayoutKind.Large:
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, GetLargeRibbonCommandWidth(GetRibbonButtonDisplayLabel(button)));
                button.Height = 76;
                button.Padding = new Thickness(3, 2, 3, 2);
                button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                break;
            case RibbonCommandLayoutKind.Medium:
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, 74);
                button.Height = 48;
                button.Padding = new Thickness(3, 2, 3, 2);
                button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                break;
            default:
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, 72);
                button.Height = Math.Max(button.Height is > 0 ? button.Height : 0, 24);
                button.Padding = new Thickness(4, 2, 4, 2);
                button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                button.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
                break;
        }
    }

    private void NormalizeRibbonCommandGroups(RibbonStaticSurfaceSnapshot surface)
    {
        NormalizeRibbonCommandColumns(surface);
    }

    private void NormalizeRibbonCommandColumns(RibbonStaticSurfaceSnapshot surface)
    {
        var panels = surface.StackPanels
            .Where(panel => panel != HomeRibbonPanel &&
                            panel.Orientation == Orientation.Vertical &&
                            FindVisualAncestor<ButtonBase>(panel) is null)
            .ToList();

        foreach (var panel in panels)
        {
            var directButtons = panel.Children.OfType<Button>().Where(button => button.Visibility == Visibility.Visible).ToList();
            if (directButtons.Count <= 3)
                continue;

            var parent = VisualTreeHelper.GetParent(panel) ?? LogicalTreeHelper.GetParent(panel);
            if (parent is not Panel parentPanel)
                continue;

            var index = parentPanel.Children.IndexOf(panel);
            if (index < 0)
                continue;

            var row = Grid.GetRow(panel);
            var column = Grid.GetColumn(panel);
            var rowSpan = Grid.GetRowSpan(panel);
            var columnSpan = Grid.GetColumnSpan(panel);
            var margin = panel.Margin;
            var verticalAlignment = panel.VerticalAlignment;
            var horizontalAlignment = panel.HorizontalAlignment;

            panel.Children.Clear();
            var grid = new UniformGrid
            {
                Rows = 3,
                Columns = (int)Math.Ceiling(directButtons.Count / 3.0),
                Margin = margin,
                VerticalAlignment = verticalAlignment,
                HorizontalAlignment = horizontalAlignment
            };

            Grid.SetRow(grid, row);
            Grid.SetColumn(grid, column);
            Grid.SetRowSpan(grid, rowSpan);
            Grid.SetColumnSpan(grid, columnSpan);

            foreach (var button in directButtons)
            {
                NormalizeDenseRibbonColumnButton(button);
                grid.Children.Add(button);
            }

            parentPanel.Children.RemoveAt(index);
            parentPanel.Children.Insert(index, grid);
        }
    }

    private static void NormalizeDenseRibbonColumnButton(Button button)
    {
        var commandName = GetRibbonButtonCommandName(button);
        if (string.IsNullOrWhiteSpace(commandName))
            return;

        var label = FindRibbonContentLabel(button.Content) ?? commandName;
        button.Height = 24;
        button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, GetSmallRibbonCommandWidth(label));
        SetRibbonCompactWidths(button, button.Width, 24);
        button.Padding = new Thickness(4, 2, 4, 2);
        button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
        button.Content = CreateRibbonCommandContent(commandName, label, RibbonCommandLayoutKind.Small);
    }

    private static double GetSmallRibbonCommandWidth(string label)
    {
        var length = string.IsNullOrWhiteSpace(label) ? 0 : label.Trim().Length;
        return length switch
        {
            <= 3 => 58,
            <= 6 => 66,
            <= 10 => 92,
            <= 14 => 126,
            _ => Math.Min(150, 44 + length * 6)
        };
    }

    private static double GetIconLabelRowRibbonCommandWidth(string label)
    {
        var length = string.IsNullOrWhiteSpace(label) ? 0 : label.Trim().Length;
        return Math.Min(156, 48 + length * 5.8);
    }

    private static double GetLargeRibbonCommandWidth(string label)
    {
        var length = string.IsNullOrWhiteSpace(label) ? 0 : label.Trim().Length;
        return length switch
        {
            <= 5 => 62,
            <= 9 => 76,
            <= 14 => 88,
            _ => 96
        };
    }
}
