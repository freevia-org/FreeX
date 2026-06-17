using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Ribbon;

/// <summary>
/// Renders a portable <see cref="RibbonDefinition"/> into an Avalonia control tree: a tab strip of
/// tabs, each a row of bordered groups, each group a wrapped set of controls over a caption. Button
/// clicks and combo selections dispatch through the <see cref="IRibbonCommandRegistry"/>; unregistered
/// command ids render disabled (the registry never throws). This is the Avalonia counterpart to the
/// shared WPF ribbon renderer the FreeW host uses.
/// </summary>
internal static class AvaloniaRibbonRenderer
{
    private static readonly IBrush GroupBorder = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    private static readonly IBrush HeaderInk = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
    private static readonly IBrush RibbonSurface = new SolidColorBrush(Color.FromRgb(0xFB, 0xFB, 0xFB));

    public static Control Build(RibbonDefinition definition, IRibbonCommandRegistry registry, Action? afterExecute = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(registry);

        var tabs = new TabControl { Background = RibbonSurface, Padding = new Thickness(0), MinHeight = 104 };
        foreach (var tab in definition.VisibleTabs)
            tabs.Items.Add(new TabItem { Header = tab.Header, Content = BuildTab(tab, registry, afterExecute) });
        if (tabs.Items.Count > 0)
            tabs.SelectedIndex = definition.VisibleTabs.ToList().FindIndex(t => t.Id == "home") is var i && i >= 0 ? i : 0;
        return tabs;
    }

    private static Control BuildTab(RibbonTab tab, IRibbonCommandRegistry registry, Action? afterExecute)
    {
        var lane = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 6, 6, 4), Spacing = 6 };
        foreach (var group in tab.Groups)
            lane.Children.Add(BuildGroup(group, registry, afterExecute));
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = lane };
    }

    private static Control BuildGroup(RibbonGroup group, IRibbonCommandRegistry registry, Action? afterExecute)
    {
        var controls = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 280, Margin = new Thickness(4, 2) };
        foreach (var control in group.Controls)
        {
            var element = BuildControl(control, registry, afterExecute);
            if (element is not null)
                controls.Children.Add(element);
        }

        var header = new TextBlock
        {
            Text = group.Header,
            FontSize = 11,
            Foreground = HeaderInk,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };

        var stack = new StackPanel();
        stack.Children.Add(controls);
        stack.Children.Add(header);

        return new Border
        {
            BorderBrush = GroupBorder,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(6, 2),
            Child = stack,
        };
    }

    private static Control? BuildControl(RibbonControl control, IRibbonCommandRegistry registry, Action? afterExecute)
    {
        switch (control)
        {
            case RibbonButton button:
                return MakeButton(button.Label, button.CommandId, registry, afterExecute);
            case RibbonToggleButton toggle:
                return MakeButton(toggle.Label, toggle.CommandId, registry, afterExecute);
            case RibbonComboBox combo:
                return MakeCombo(combo, registry, afterExecute);
            case RibbonSeparator:
                return new Border { Width = 1, Margin = new Thickness(3, 2), Background = GroupBorder };
            case RibbonLabel label:
                return new TextBlock { Text = label.Label, Margin = new Thickness(4, 6), VerticalAlignment = VerticalAlignment.Center };
            default:
                // SplitButton / Dropdown / Gallery etc. — render as a plain command button for now.
                if (TryGetCommandId(control, out var id, out var text))
                    return MakeButton(text, id, registry, afterExecute);
                return null;
        }
    }

    private static Button MakeButton(string label, RibbonCommandId id, IRibbonCommandRegistry registry, Action? afterExecute)
    {
        var enabled = registry.TryGet(id, out _);
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(8, 4),
            Margin = new Thickness(1),
            MinWidth = 30,
            IsEnabled = enabled,
        };
        button.Click += (_, _) =>
        {
            if (registry.TryGet(id, out var command) && command is not null)
                command.Execute(RibbonCommandContext.Empty);
            afterExecute?.Invoke();
        };
        return button;
    }

    private static Control MakeCombo(RibbonComboBox combo, IRibbonCommandRegistry registry, Action? afterExecute)
    {
        var box = new ComboBox
        {
            ItemsSource = combo.Items,
            Width = combo.Width ?? 72,
            Margin = new Thickness(2),
            IsEnabled = registry.TryGet(combo.CommandId, out _),
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string value && registry.TryGet(combo.CommandId, out var command) && command is not null)
                command.Execute(RibbonCommandContext.ForSelectedValue(value));
            afterExecute?.Invoke();
        };
        return box;
    }

    private static bool TryGetCommandId(RibbonControl control, out RibbonCommandId id, out string label)
    {
        switch (control)
        {
            case RibbonSplitButton sb: id = sb.CommandId; label = sb.Label; return true;
            case RibbonDropdown dd: id = dd.CommandId; label = dd.Label; return true;
            case RibbonGallery gal: id = gal.CommandId; label = gal.Label; return true;
            case RibbonCheckBox cb: id = cb.CommandId; label = cb.Label; return true;
            default: id = default; label = string.Empty; return false;
        }
    }
}
