using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Builds the common Office-style Backstage pane bodies shared by the sister WPF apps.
/// Hosts provide app-specific model values and callbacks; this class owns only WPF composition.
/// </summary>
public sealed class BackstagePaneComposer
{
    private readonly BackstageVisualKit _kit;
    private readonly BackstagePaneComposerProfile _profile;

    public BackstagePaneComposer(
        BackstageVisualKit kit,
        BackstagePaneComposerProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(kit);
        _kit = kit;
        _profile = profile ?? BackstagePaneComposerProfile.Default;
    }

    public UIElement BuildInfoPane(BackstageInfoPaneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var text = spec.EffectiveText;

        var panel = CreatePane(_profile.InfoPaneMaxWidth);
        panel.Children.Add(Heading(text.Heading));
        panel.Children.Add(Field(
            spec.DocumentKindLabel,
            spec.DisplayName + (spec.IsDirty ? text.DirtySuffix : string.Empty)));
        panel.Children.Add(Field(text.LocationLabel, spec.Location ?? text.NotSavedYet));

        if (spec.Properties.Count > 0)
        {
            panel.Children.Add(SubHeading(text.PropertiesHeading));
            AddFields(panel, spec.Properties);
        }

        if (!string.IsNullOrWhiteSpace(spec.EditPropertiesText) && spec.EditProperties is not null)
        {
            var edit = _kit.LinkButton(spec.EditPropertiesText, spec.EditProperties);
            edit.Margin = ToThickness(_profile.InfoEditActionMargin);
            panel.Children.Add(edit);
        }

        if (spec.Statistics.Count > 0)
        {
            panel.Children.Add(SubHeading(text.StatisticsHeading));
            AddFields(panel, spec.Statistics);
        }

        if (spec.ActionGroups is { Count: > 0 })
            AddActionGroups(panel, spec.ActionGroups);

        return _kit.Scroll(panel);
    }

    public UIElement BuildRecentPane(BackstageRecentPaneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = CreatePane(_profile.RecentPaneMaxWidth);
        panel.Children.Add(Heading(spec.Heading));

        if (spec.Paths.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = spec.EmptyText,
                Foreground = _kit.Muted,
                Margin = new Thickness(0, 4, 0, 0)
            });
            return panel;
        }

        foreach (var path in spec.Paths)
        {
            var item = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Cursor = Cursors.Hand };
            item.Children.Add(new TextBlock
            {
                Text = BackstageRecentActionRowsPlanner.FileNameOrPath(path),
                Foreground = _kit.Link,
                FontSize = 14
            });
            item.Children.Add(new TextBlock
            {
                Text = path,
                Foreground = _kit.Muted,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            item.MouseLeftButtonUp += (_, _) => spec.OpenPath(path);
            panel.Children.Add(item);
        }

        return _kit.Scroll(panel);
    }

    public UIElement BuildTemplatePane(BackstageTemplatePaneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(_kit.HeadingText(spec.Heading));

        var gallery = new WrapPanel { Orientation = Orientation.Horizontal };
        gallery.Children.Add(_kit.TemplateTile(spec.TileCaption, spec.Create));
        panel.Children.Add(gallery);

        if (!string.IsNullOrWhiteSpace(spec.FooterText))
        {
            panel.Children.Add(new TextBlock
            {
                Text = spec.FooterText,
                Foreground = _kit.Muted,
                Margin = new Thickness(0, 18, 0, 0)
            });
        }

        return panel;
    }

    public UIElement BuildOptionsPane(BackstageOptionsPaneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = CreatePane(_profile.OptionsPaneMaxWidth);
        panel.Children.Add(Heading(spec.Heading));
        panel.Children.Add(Description(spec.Description));

        AddFields(panel, spec.Fields);

        if (!string.IsNullOrWhiteSpace(spec.EditText) && spec.Edit is not null)
        {
            var edit = _kit.LinkButton(spec.EditText, spec.Edit);
            edit.Margin = ToThickness(_profile.OptionsEditActionMargin);
            panel.Children.Add(edit);
        }

        return panel;
    }

    public UIElement BuildAccountPane(BackstageAccountPaneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Groups);

        var panel = CreatePane(_profile.AccountPaneMaxWidth);
        panel.Children.Add(Heading(spec.Heading));
        panel.Children.Add(Description(spec.Description));

        foreach (var group in spec.Groups)
        {
            panel.Children.Add(SubHeading(group.Heading));
            AddFields(panel, group.Fields);
        }

        if (!string.IsNullOrWhiteSpace(spec.OptionsText) && spec.OpenOptions is not null)
        {
            var options = _kit.LinkButton(spec.OptionsText, spec.OpenOptions);
            options.FontSize = _profile.AccountOptionsFontSize;
            options.Margin = ToThickness(_profile.AccountOptionsMargin);
            if (!string.IsNullOrWhiteSpace(spec.OptionsAutomationId))
                AutomationProperties.SetAutomationId(options, spec.OptionsAutomationId);
            panel.Children.Add(options);
        }

        return _kit.Scroll(panel);
    }

    public UIElement BuildActionPane(BackstageActionPaneSpec spec) =>
        BuildActionPane(spec, _profile.UseLinkActionRows ? ExportActionRow : ActionRow);

    public UIElement BuildExportActionPane(BackstageActionPaneSpec spec) =>
        BuildActionPane(spec, ExportActionRow);

    private UIElement BuildActionPane(
        BackstageActionPaneSpec spec,
        Func<BackstageActionRow, UIElement> actionRow)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = CreatePane(_profile.ActionPaneMaxWidth);
        panel.Children.Add(Heading(spec.Heading));

        if (!string.IsNullOrWhiteSpace(spec.Description))
        {
            panel.Children.Add(Description(spec.Description));
        }

        foreach (var group in spec.Groups)
        {
            panel.Children.Add(SubHeading(group.Heading));
            foreach (var action in group.Actions)
                panel.Children.Add(actionRow(action));
        }

        return _kit.Scroll(panel);
    }

    private void AddActionGroups(Panel panel, IReadOnlyList<BackstageActionGroup> groups)
    {
        foreach (var group in groups)
            AddActionGroup(panel, group);
    }

    private void AddActionGroup(Panel panel, BackstageActionGroup group)
    {
        panel.Children.Add(SubHeading(group.Heading));

        foreach (var action in group.Actions)
            panel.Children.Add(ActionRow(action));
    }

    private void AddFields(Panel panel, IReadOnlyList<BackstageFieldRow> fields)
    {
        foreach (var field in fields)
            panel.Children.Add(Field(field.Label, field.Value));
    }

    private UIElement ActionRow(BackstageActionRow action)
    {
        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
            Margin = ToThickness(_profile.Metrics.ActionRowMargin)
        };
        button.Click += (_, _) => action.Invoke();
        button.IsEnabled = action.IsEnabled;
        // The action label is the shared semantic contract. Keep it on the
        // button even though the visual content is a two-line StackPanel so
        // accessibility clients and the parity harness see the same action.
        AutomationProperties.SetName(button, action.Label);
        if (!string.IsNullOrWhiteSpace(action.AutomationId))
            AutomationProperties.SetAutomationId(button, action.AutomationId);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = action.Label,
            Foreground = _kit.Link,
            FontSize = _profile.Metrics.ActionFontSize
        });

        if (!string.IsNullOrWhiteSpace(action.Description))
        {
            stack.Children.Add(new TextBlock
            {
                Text = action.Description,
                Foreground = _kit.Muted,
                FontSize = _profile.Metrics.ActionDescriptionFontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = ToThickness(_profile.Metrics.ActionDescriptionMargin)
            });
        }

        button.Content = stack;
        return button;
    }

    private UIElement ExportActionRow(BackstageActionRow action)
    {
        var row = new StackPanel { Margin = ToThickness(_profile.Metrics.ActionRowMargin) };
        var button = _kit.LinkButton(action.Label, action.Invoke);
        button.FontSize = _profile.Metrics.ActionFontSize;
        button.IsEnabled = action.IsEnabled;
        AutomationProperties.SetName(button, action.Label);
        if (!string.IsNullOrWhiteSpace(action.AutomationId))
            AutomationProperties.SetAutomationId(button, action.AutomationId);
        row.Children.Add(button);

        if (!string.IsNullOrWhiteSpace(action.Description))
        {
            row.Children.Add(new TextBlock
            {
                Text = action.Description,
                Foreground = _kit.Muted,
                FontSize = _profile.Metrics.ActionDescriptionFontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = ToThickness(_profile.Metrics.ActionDescriptionMargin)
            });
        }

        return row;
    }

    private StackPanel CreatePane(double maxWidth) => new()
    {
        MaxWidth = maxWidth,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = _profile.Metrics.HeadingFontSize,
        FontWeight = FontWeights.Light,
        Foreground = _kit.Heading,
        Margin = ToThickness(_profile.Metrics.HeadingMargin),
    };

    private TextBlock SubHeading(string text) => new()
    {
        Text = text,
        FontSize = _profile.Metrics.SectionHeaderFontSize,
        FontWeight = FontWeights.SemiBold,
        Foreground = _kit.Heading,
        Margin = ToThickness(_profile.Metrics.SectionHeaderMargin),
    };

    private TextBlock Description(string text) => new()
    {
        Text = text,
        Foreground = _kit.Muted,
        FontSize = _profile.Metrics.DescriptionFontSize,
        TextWrapping = TextWrapping.Wrap,
        Margin = ToThickness(_profile.DescriptionMargin),
    };

    private UIElement Field(string label, string value)
    {
        var metrics = _profile.Metrics;
        var grid = new Grid { Margin = ToThickness(metrics.DetailGridMargin) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(metrics.DetailLabelColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var name = new TextBlock
        {
            Text = label,
            Foreground = _kit.Muted,
            FontSize = metrics.DetailFontSize,
        };
        Grid.SetColumn(name, 0);
        grid.Children.Add(name);

        var content = new TextBlock
        {
            Text = value,
            Foreground = _kit.Heading,
            FontSize = metrics.DetailFontSize,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    private static Thickness ToThickness(BackstageVisualThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
}

