using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Builds the common Office-style Backstage pane bodies shared by the sister WPF apps.
/// Hosts provide app-specific model values and callbacks; this class owns only WPF composition.
/// </summary>
public sealed class BackstagePaneComposer
{
    private const string DirtySuffix = "  (unsaved changes)";
    private const string NotSavedYetText = "Not saved yet";

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
        panel.Children.Add(_kit.HeadingText("Info"));
        panel.Children.Add(_kit.Field(
            spec.DocumentKindLabel,
            spec.DisplayName + (spec.IsDirty ? DirtySuffix : string.Empty)));
        panel.Children.Add(_kit.Field("Location", spec.Location ?? NotSavedYetText));

        if (spec.Properties.Count > 0)
        {
            panel.Children.Add(_kit.SubHeading("Properties"));
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
            panel.Children.Add(_kit.SubHeading("Statistics"));
            AddFields(panel, spec.Statistics);
        }

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

    private void AddFields(Panel panel, IReadOnlyList<BackstageFieldRow> fields)
    {
        foreach (var field in fields)
            panel.Children.Add(_kit.Field(field.Label, field.Value));
    }
}

public sealed record BackstageFieldRow(string Label, string Value);

public sealed record BackstageInfoPaneSpec(
    string DocumentKindLabel,
    string DisplayName,
    bool IsDirty,
    string? Location,
    IReadOnlyList<BackstageFieldRow> Properties,
    IReadOnlyList<BackstageFieldRow> Statistics,
    string? EditPropertiesText = null,
    Action? EditProperties = null);

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
