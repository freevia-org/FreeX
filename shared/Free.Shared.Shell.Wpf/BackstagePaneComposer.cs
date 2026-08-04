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
    private const string DirtySuffix = "  (unsaved changes)";

    private readonly BackstageVisualKit _kit;

    public BackstagePaneComposer(BackstageVisualKit kit)
    {
        ArgumentNullException.ThrowIfNull(kit);
        _kit = kit;
    }

    public UIElement BuildInfoPane(BackstageInfoPaneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(_kit.HeadingText(BackstageInfoPaneText.Title));
        panel.Children.Add(_kit.Field(
            spec.DocumentKindLabel,
            spec.DisplayName + (spec.IsDirty ? DirtySuffix : string.Empty)));
        panel.Children.Add(_kit.Field(BackstageInfoPaneText.LocationLabel, spec.Location ?? BackstageInfoPaneText.NotSavedYet));

        if (spec.Properties.Count > 0)
        {
            panel.Children.Add(_kit.SubHeading(BackstageInfoPaneText.PropertiesHeading));
            AddFields(panel, spec.Properties);
        }

        if (!string.IsNullOrWhiteSpace(spec.EditPropertiesText) && spec.EditProperties is not null)
        {
            var edit = _kit.LinkButton(spec.EditPropertiesText, spec.EditProperties);
            edit.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(edit);
        }

        if (spec.Statistics.Count > 0)
        {
            panel.Children.Add(_kit.SubHeading(BackstageInfoPaneText.StatisticsHeading));
            AddFields(panel, spec.Statistics);
        }

        if (spec.ActionGroups is { Count: > 0 })
            AddActionGroups(panel, spec.ActionGroups);

        return _kit.Scroll(panel);
    }

    public UIElement BuildRecentPane(BackstageRecentPaneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(_kit.HeadingText("Recent"));

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
                Text = Path.GetFileName(path),
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

        var panel = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(_kit.HeadingText("Options"));
        panel.Children.Add(new TextBlock
        {
            Text = spec.Description,
            Foreground = _kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        AddFields(panel, spec.Fields);

        if (!string.IsNullOrWhiteSpace(spec.EditText) && spec.Edit is not null)
        {
            var edit = _kit.LinkButton(spec.EditText, spec.Edit);
            edit.Margin = new Thickness(0, 14, 0, 0);
            panel.Children.Add(edit);
        }

        return panel;
    }

    public UIElement BuildAccountPane(BackstageAccountPaneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Groups);

        var panel = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(_kit.HeadingText(spec.Heading));
        panel.Children.Add(new TextBlock
        {
            Text = spec.Description,
            Foreground = _kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        foreach (var group in spec.Groups)
        {
            panel.Children.Add(_kit.SubHeading(group.Heading));
            AddFields(panel, group.Fields);
        }

        if (!string.IsNullOrWhiteSpace(spec.OptionsText) && spec.OpenOptions is not null)
        {
            var options = _kit.LinkButton(spec.OptionsText, spec.OpenOptions);
            options.Margin = new Thickness(0, 18, 0, 0);
            panel.Children.Add(options);
        }

        return _kit.Scroll(panel);
    }

    public UIElement BuildActionPane(BackstageActionPaneSpec spec) =>
        BuildActionPane(spec, ActionRow);

    public UIElement BuildExportActionPane(BackstageActionPaneSpec spec) =>
        BuildActionPane(spec, ExportActionRow);

    private UIElement BuildActionPane(
        BackstageActionPaneSpec spec,
        Func<BackstageActionRow, UIElement> actionRow)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(_kit.HeadingText(spec.Heading));

        if (!string.IsNullOrWhiteSpace(spec.Description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = spec.Description,
                Foreground = _kit.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });
        }

        foreach (var group in spec.Groups)
        {
            panel.Children.Add(_kit.SubHeading(group.Heading));
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
        panel.Children.Add(_kit.SubHeading(group.Heading));

        foreach (var action in group.Actions)
            panel.Children.Add(ActionRow(action));
    }

    private void AddFields(Panel panel, IReadOnlyList<BackstageFieldRow> fields)
    {
        foreach (var field in fields)
            panel.Children.Add(_kit.Field(field.Label, field.Value));
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
            Margin = new Thickness(0, 0, 0, 10)
        };
        button.Click += (_, _) => action.Invoke();
        // The action label is the shared semantic contract. Keep it on the
        // button even though the visual content is a two-line StackPanel so
        // accessibility clients and the parity harness see the same action.
        AutomationProperties.SetName(button, action.Label);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = action.Label,
            Foreground = _kit.Link,
            FontSize = 14
        });

        if (!string.IsNullOrWhiteSpace(action.Description))
        {
            stack.Children.Add(new TextBlock
            {
                Text = action.Description,
                Foreground = _kit.Muted,
                FontSize = BackstageVisualContract.Pane.ActionDescriptionFontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = ToThickness(BackstageVisualContract.Pane.ActionDescriptionMargin)
            });
        }

        button.Content = stack;
        return button;
    }

    private UIElement ExportActionRow(BackstageActionRow action)
    {
        var row = new StackPanel { Margin = ToThickness(BackstageVisualContract.Pane.ActionRowMargin) };
        var button = _kit.LinkButton(action.Label, action.Invoke);
        button.FontSize = BackstageVisualContract.Pane.ActionFontSize;
        AutomationProperties.SetName(button, action.Label);
        row.Children.Add(button);

        if (!string.IsNullOrWhiteSpace(action.Description))
        {
            row.Children.Add(new TextBlock
            {
                Text = action.Description,
                Foreground = _kit.Muted,
                FontSize = BackstageVisualContract.Pane.ActionDescriptionFontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = ToThickness(BackstageVisualContract.Pane.ActionDescriptionMargin)
            });
        }

        return row;
    }

    private static Thickness ToThickness(BackstageVisualThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
}

public sealed record BackstageRecentPaneSpec(
    IReadOnlyList<string> Paths,
    string EmptyText,
    Action<string> OpenPath);

public sealed record BackstageTemplatePaneSpec(
    string Heading,
    string TileCaption,
    string FooterText,
    Action Create);

public sealed record BackstageOptionsPaneSpec(
    string Description,
    IReadOnlyList<BackstageFieldRow> Fields,
    string? EditText = null,
    Action? Edit = null);

public sealed record BackstageAccountPaneSpec(
    string Heading,
    string Description,
    IReadOnlyList<SisterBackstageAccountFieldGroup> Groups,
    string? OptionsText = null,
    Action? OpenOptions = null);

public sealed record BackstageActionPaneSpec(
    string Heading,
    string Description,
    IReadOnlyList<BackstageActionGroup> Groups);

