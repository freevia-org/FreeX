using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void NormalizeRibbonCommandButtons()
    {
        if (RibbonTabs is null)
            return;

        foreach (var button in EnumerateVisualDescendants(RibbonTabs).OfType<ButtonBase>())
        {
            if (button is CheckBox or RadioButton)
                continue;

            if (button.Content is not string label || string.IsNullOrWhiteSpace(label))
                continue;

            var title = RibbonTooltip.GetTitle(button);
            var commandName = string.IsNullOrWhiteSpace(title) ? label : title;
            var layoutKind = RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label);
            ApplyRibbonCommandSize(button, layoutKind);
            if (layoutKind is RibbonCommandLayoutKind.Small)
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, GetSmallRibbonCommandWidth(label));
            var fullWidth = button.Width is > 0 ? button.Width : Math.Max(button.ActualWidth, 64);
            var compactWidth = layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium ? 38 : 24;
            SetRibbonCompactWidthTag(button, fullWidth, compactWidth);

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
            NormalizeRibbonCommandButtons();
            NormalizeExistingRibbonIconText();
            ConfigureInsertRibbonSurface();
            NormalizeRibbonCommandGroups();
            AlignRibbonIconColumns();
            HideRibbonScrollBars();
            ApplyToolbarDropdownWhiteBackgrounds();
            UpdateRibbonCompactMode(force: forceCompact);
        }
        finally
        {
            _normalizingRibbonSurface = false;
        }
    }

    private void HideRibbonScrollBars()
    {
        if (RibbonTabs is null)
            return;

        foreach (var scrollViewer in EnumerateVisualDescendants(RibbonTabs).OfType<ScrollViewer>())
            scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
    }

    private void NormalizeRibbonSurfaceAfterTabSelection()
    {
        NormalizeRibbonSurface(forceCompact: true);
        Dispatcher.BeginInvoke(
            (Action)(() => NormalizeRibbonSurface(forceCompact: true)),
            DispatcherPriority.Loaded);
    }

    private void ConfigureInsertRibbonSurface()
    {
        var insertTab = RibbonTabs?.Items
            .OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), "Insert", StringComparison.Ordinal));

        if (insertTab is null)
            return;

        var contentRoot = GetRibbonTabContentRoot(insertTab);
        foreach (var button in EnumerateVisualDescendants(contentRoot)
                     .Concat(EnumerateLogicalDescendants(contentRoot))
                     .OfType<Button>()
                     .Distinct())
        {
            var title = GetRibbonButtonTitleOrLabel(button);
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
            if (current is Grid grid &&
                grid.Children.OfType<Border>().Any(border => Grid.GetRow(border) == 1))
            {
                return GetRibbonGroupName(grid);
            }

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return "";
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

    private static string? FindRibbonContentLabel(object? content)
    {
        if (content is TextBlock textBlock &&
            string.Equals(textBlock.Tag?.ToString(), "RibbonLabel", StringComparison.Ordinal) &&
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

    private void AlignRibbonIconColumns()
    {
        if (RibbonTabs is null)
            return;

        foreach (var stack in EnumerateVisualDescendants(RibbonTabs).OfType<StackPanel>())
        {
            if (stack.Tag?.ToString()?.StartsWith("RibbonCommandContent", StringComparison.Ordinal) is true)
                continue;

            if (stack.Orientation != Orientation.Horizontal || stack.Children.Count < 2)
                continue;

            if (stack.Children[0] is not FrameworkElement icon || stack.Children[1] is not TextBlock label)
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

    private void NormalizeExistingRibbonIconText()
    {
        foreach (var button in EnumerateVisualDescendants(this).OfType<ButtonBase>())
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
                if (string.Equals(textBlock.Tag?.ToString(), "RibbonLabel", StringComparison.Ordinal))
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

                var isIcon = string.Equals(textBlock.Tag?.ToString(), "RibbonIcon", StringComparison.Ordinal);
                if (!isIcon)
                    continue;

                textBlock.Tag = "RibbonIcon";
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

        var commandName = GetRibbonButtonTitleOrLabel(button);
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        element.Width = 26;
        element.Height = 26;
        SetRibbonCompactWidthTag(button, 26, 24);
        element.Margin = new Thickness(1, 0, 1, 0);
        if (button is Control control)
            control.Padding = new Thickness(1);

        button.Content = CreateRibbonIconOnlyContent(commandName, 20);
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

        var commandName = GetRibbonButtonTitleOrLabel(button);
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        element.Width = GetSmallRibbonCommandWidth(commandName);
        element.Height = 24;
        SetRibbonCompactWidthTag(button, element.Width, 24);
        if (button is Control control)
            control.Padding = new Thickness(4, 2, 4, 2);
        button.Content = CreateRibbonCommandContent(commandName, commandName, RibbonCommandLayoutKind.Small);
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
            FindVisualAncestor<TabControl>(commandButton) != RibbonTabs ||
            !ContainsUnreplacedRibbonIcon(commandButton.Content))
        {
            return false;
        }

        var commandName = GetRibbonButtonTitleOrLabel(commandButton);
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        var label = commandName;
        var layoutKind = RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label);
        ApplyRibbonCommandSize(commandButton, layoutKind);
        if (layoutKind is RibbonCommandLayoutKind.Small)
            commandButton.Width = Math.Max(commandButton.Width is > 0 ? commandButton.Width : 0, GetSmallRibbonCommandWidth(label));
        SetRibbonCompactWidthTag(
            commandButton,
            commandButton.Width is > 0 ? commandButton.Width : Math.Max(commandButton.ActualWidth, 64),
            layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium ? 38 : 24);

        commandButton.Content = CreateRibbonCommandContent(commandName, label, layoutKind);
        if (layoutKind is RibbonCommandLayoutKind.Small)
            commandButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        commandButton.HorizontalContentAlignment = layoutKind is RibbonCommandLayoutKind.Small
            ? System.Windows.HorizontalAlignment.Left
            : System.Windows.HorizontalAlignment.Center;
        return true;
    }

    private static void NormalizeRibbonButtonSizeForCommandIcons(ButtonBase button, bool tall)
    {
        if (button is not FrameworkElement element || !ContainsRibbonCommandIcon(button.Content))
            return;

        if (tall)
        {
            var tallLabel = FindRibbonContentLabel(button.Content) ?? GetRibbonButtonTitleOrLabel(button);
            element.Width = Math.Max(element.Width is > 0 ? element.Width : 0, GetLargeRibbonCommandWidth(tallLabel));
            element.Height = Math.Max(element.Height is > 0 ? element.Height : 0, 76);
            SetRibbonCompactWidthTag(button, element.Width, 38);
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
        SetRibbonCompactWidthTag(button, element.Width, 24);
    }

    private static void SetRibbonCompactWidthTag(ButtonBase button, double fullWidth, double compactWidth)
    {
        button.Tag = $"RibbonCompact:{fullWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{compactWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static bool ContainsRibbonCommandIcon(object? content)
    {
        switch (content)
        {
            case FrameworkElement element when string.Equals(element.Tag?.ToString(), "RibbonIcon", StringComparison.Ordinal):
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
        return string.Equals(textBlock.Tag?.ToString(), "RibbonIcon", StringComparison.Ordinal);
    }

    private static FrameworkElement CreateStaticRibbonCommandIcon(ButtonBase owner, RibbonIcon source, bool tall)
    {
        var commandName = source.Kind == RibbonCommandIconKind.Previous
            ? "Back to workbook"
            : GetStaticRibbonIconCommandName(owner, source.Kind.ToString());
        var fallbackIcon = new RibbonCommandIcon(source.Kind);
        var iconSize = IsWhiteBrush(source.Foreground) ? source.IconSize : tall ? 32 : 22;
        if (IsWhiteBrush(source.Foreground))
        {
            var fallbackElement = RibbonIconFactory.CreateIcon(
                fallbackIcon,
                iconSize,
                source.Foreground ?? owner.Foreground);
            fallbackElement.Tag = "RibbonIcon";
            fallbackElement.HorizontalAlignment = source.HorizontalAlignment;
            fallbackElement.VerticalAlignment = source.VerticalAlignment;
            fallbackElement.Margin = source.Margin;
            return fallbackElement;
        }

        var commandIcon = RibbonIconFactory.CreateCommandIcon(
            commandName,
            fallbackIcon,
            iconSize,
            source.Foreground ?? owner.Foreground);
        commandIcon.Tag = "RibbonIcon";
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
        commandIcon.Tag = "RibbonIcon";
        commandIcon.HorizontalAlignment = source.HorizontalAlignment;
        commandIcon.VerticalAlignment = source.VerticalAlignment;
        commandIcon.Margin = source.Margin;
        return commandIcon;
    }

    private static string GetStaticRibbonIconCommandName(ButtonBase owner, string fallback)
    {
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

    private void ApplyToolbarDropdownWhiteBackgrounds()
    {
        if (RibbonTabs is null)
            return;

        foreach (var comboBox in EnumerateVisualDescendants(RibbonTabs).OfType<ComboBox>())
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
        var iconSize = layoutKind == RibbonCommandLayoutKind.Large ? 32 : 20;
        var slotSize = layoutKind == RibbonCommandLayoutKind.Large ? 34 : 20;
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

        var labelBlock = new TextBlock
        {
            Text = label,
            Tag = "RibbonLabel",
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

        var contentTag = layoutKind == RibbonCommandLayoutKind.Large
            ? "RibbonCommandContent:L"
            : layoutKind == RibbonCommandLayoutKind.Medium
                ? "RibbonCommandContent:M"
                : "RibbonCommandContent:S";

        if (tall)
        {
            return new StackPanel
            {
                Tag = contentTag,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Children =
                {
                    iconSlot,
                    labelBlock
                }
            };
        }

        var compactGrid = new Grid
        {
            Tag = contentTag,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        compactGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(slotSize) });
        compactGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        iconSlot.Margin = new Thickness(0);
        labelBlock.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(iconSlot, 0);
        Grid.SetColumn(labelBlock, 1);
        compactGrid.Children.Add(iconSlot);
        compactGrid.Children.Add(labelBlock);
        return compactGrid;
    }

    private static FrameworkElement CreateRibbonIconOnlyContent(string commandName, double iconSize)
    {
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);
        var (_, _, glyphBrush) = GetRibbonIconAccentBrushes(icon.Accent);
        var iconElement = RibbonIconFactory.CreateCommandIcon(commandName, icon, iconSize, glyphBrush);
        iconElement.Tag = "RibbonIcon";

        return new Grid
        {
            Tag = "RibbonCommandContent",
            Width = 24,
            Height = 24,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Children = { iconElement }
        };
    }

    private static (Brush? SlotBackground, Brush? SlotBorder, Brush GlyphBrush) GetRibbonIconAccentBrushes(
        RibbonCommandIconAccent accent)
    {
        return accent switch
        {
            RibbonCommandIconAccent.Green => (BrushFromRgb(232, 244, 239), BrushFromRgb(33, 115, 70), BrushFromRgb(24, 92, 55)),
            RibbonCommandIconAccent.Chart => (BrushFromRgb(232, 241, 252), BrushFromRgb(68, 114, 196), BrushFromRgb(47, 84, 150)),
            RibbonCommandIconAccent.Data => (BrushFromRgb(229, 243, 250), BrushFromRgb(0, 120, 170), BrushFromRgb(0, 92, 135)),
            RibbonCommandIconAccent.Theme => (BrushFromRgb(241, 236, 250), BrushFromRgb(112, 48, 160), BrushFromRgb(85, 35, 125)),
            RibbonCommandIconAccent.Fill => (BrushFromRgb(255, 248, 218), BrushFromRgb(191, 144, 0), BrushFromRgb(116, 88, 0)),
            RibbonCommandIconAccent.Color => (BrushFromRgb(255, 235, 235), BrushFromRgb(192, 0, 0), BrushFromRgb(150, 0, 0)),
            RibbonCommandIconAccent.Border => (BrushFromRgb(245, 245, 245), BrushFromRgb(96, 96, 96), BrushFromRgb(31, 31, 31)),
            RibbonCommandIconAccent.Warning => (BrushFromRgb(255, 244, 214), BrushFromRgb(214, 157, 0), BrushFromRgb(138, 91, 0)),
            RibbonCommandIconAccent.Protect => (BrushFromRgb(232, 244, 239), BrushFromRgb(33, 115, 70), BrushFromRgb(24, 92, 55)),
            RibbonCommandIconAccent.Help => (BrushFromRgb(235, 242, 255), BrushFromRgb(68, 114, 196), BrushFromRgb(47, 84, 150)),
            _ => (Brushes.Transparent, null, Brushes.Black)
        };
    }

    private static SolidColorBrush BrushFromRgb(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    private static void ApplyRibbonCommandSize(ButtonBase button, RibbonCommandLayoutKind layoutKind)
    {
        switch (layoutKind)
        {
            case RibbonCommandLayoutKind.Large:
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, GetLargeRibbonCommandWidth(GetRibbonButtonTitleOrLabel(button)));
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

    private void NormalizeRibbonCommandGroups()
    {
        if (RibbonTabs is null)
            return;

        NormalizeRibbonCommandColumns();
    }

    private void NormalizeRibbonCommandColumns()
    {
        if (RibbonTabs is null)
            return;

        var panels = EnumerateVisualDescendants(RibbonTabs)
            .OfType<StackPanel>()
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
        var commandName = GetRibbonButtonTitleOrLabel(button);
        if (string.IsNullOrWhiteSpace(commandName))
            return;

        var label = FindRibbonContentLabel(button.Content) ?? commandName;
        button.Height = 24;
        button.Width = GetSmallRibbonCommandWidth(label);
        SetRibbonCompactWidthTag(button, button.Width, 24);
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
