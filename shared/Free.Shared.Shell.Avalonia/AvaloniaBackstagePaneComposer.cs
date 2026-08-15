using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Projects portable sister-app pane specs to Avalonia controls. Apps retain only native template
/// tiles and command adapters while labels, rows, grouping, and status remain renderer-neutral.
/// </summary>
public sealed class AvaloniaBackstagePaneComposer
{
    private readonly AvaloniaBackstageChromeStyle _style;
    private readonly BackstagePaneComposerProfile _profile;

    public AvaloniaBackstagePaneComposer(
        AvaloniaBackstageChromeStyle style,
        BackstagePaneComposerProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(style);
        _profile = profile ?? BackstagePaneComposerProfile.Default;
        var metrics = _profile.Metrics;
        _style = style with
        {
            HeadingFontSize = metrics.HeadingFontSize,
            HeadingMargin = ToThickness(metrics.HeadingMargin),
            DescriptionFontSize = metrics.DescriptionFontSize,
            SectionHeaderFontSize = metrics.SectionHeaderFontSize,
            SectionHeaderMargin = ToThickness(metrics.SectionHeaderMargin),
            DetailGridMargin = ToThickness(metrics.DetailGridMargin),
            DetailLabelMargin = ToThickness(metrics.DetailGridMargin),
            DetailValueMargin = ToThickness(metrics.DetailGridMargin),
            DetailLabelColumnWidth = metrics.DetailLabelColumnWidth,
            DetailFontSize = metrics.DetailFontSize,
            ActionFontSize = metrics.ActionFontSize,
            ActionDescriptionFontSize = metrics.ActionDescriptionFontSize,
            ActionRowMargin = ToThickness(metrics.ActionRowMargin),
            ActionDescriptionMargin = ToThickness(metrics.ActionDescriptionMargin),
        };
    }

    public Control BuildInfoPane(BackstageInfoPaneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var text = spec.EffectiveText;

        var panel = CreatePane(_profile.InfoPaneMaxWidth);
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(text.Heading, _style));
        var documentGrid = AvaloniaBackstageChrome.CreateDetailGrid(_style);
        AvaloniaBackstageChrome.AddDetailRow(
            documentGrid,
            spec.DocumentKindLabel,
            spec.DisplayName + (spec.IsDirty ? text.DirtySuffix : string.Empty),
            "InfoDocumentName",
            _style);
        AvaloniaBackstageChrome.AddDetailRow(
            documentGrid,
            text.LocationLabel,
            spec.Location ?? text.NotSavedYet,
            "InfoDocumentPath",
            _style);
        panel.Children.Add(documentGrid);

        if (spec.Properties.Count > 0)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(text.PropertiesHeading, _style));
            AddFields(panel, spec.Properties, "InfoProperty");
        }

        if (!string.IsNullOrWhiteSpace(spec.EditPropertiesText) && spec.EditProperties is not null)
        {
            var edit = ActionButton(spec.EditPropertiesText, "BackstageEditDocumentProperties", spec.EditProperties);
            edit.Margin = ToThickness(_profile.InfoEditActionMargin);
            panel.Children.Add(edit);
        }

        if (spec.Statistics.Count > 0)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(text.StatisticsHeading, _style));
            AddFields(panel, spec.Statistics, "InfoStatistic");
        }

        foreach (var group in spec.ActionGroups ?? [])
            AddActionGroup(panel, group, "BackstageAction");

        return ComposePane(panel);
    }

    public Control BuildRecentPane(BackstageRecentPaneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = CreatePane(_profile.RecentPaneMaxWidth);
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(spec.Heading, _style));
        if (spec.Paths.Count == 0)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
                spec.EmptyText,
                _style,
                margin: new Thickness(0, 4, 0, 0)));
            return ComposePane(panel);
        }

        foreach (var path in spec.Paths)
        {
            var capturedPath = path;
            panel.Children.Add(AvaloniaBackstageChrome.CreateStackedActionButton(
                new AvaloniaBackstageStackedActionButtonSpec(
                    BackstageRecentActionRowsPlanner.FileNameOrPath(capturedPath),
                    capturedPath,
                    "BackstageRecent_" + AutomationIdToken.KeepLettersAndDigits(capturedPath),
                    () => spec.OpenPath(capturedPath)),
                _style));
        }

        return ComposePane(panel);
    }

    public Control BuildTemplatePane(
        BackstageTemplatePaneSpec spec,
        Func<string, Action, Control> buildTemplateTile)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(buildTemplateTile);

        var panel = CreatePane(_profile.InfoPaneMaxWidth);
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(spec.Heading, _style));
        panel.Children.Add(buildTemplateTile(spec.TileCaption, spec.Create));
        if (!string.IsNullOrWhiteSpace(spec.FooterText))
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
                spec.FooterText,
                _style,
                margin: new Thickness(0, 18, 0, 0)));
        }

        return ComposePane(panel);
    }

    public Control BuildOptionsPane(
        BackstageOptionsPaneSpec spec,
        string editAutomationId = "BackstageEditOptions")
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = CreatePane(_profile.OptionsPaneMaxWidth);
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(spec.Heading, _style));
        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            spec.Description,
            _style,
            margin: ToThickness(_profile.DescriptionMargin)));
        AddFields(panel, spec.Fields, "Options");

        if (!string.IsNullOrWhiteSpace(spec.EditText) && spec.Edit is not null)
        {
            var edit = ActionButton(spec.EditText, editAutomationId, spec.Edit);
            edit.Margin = ToThickness(_profile.OptionsEditActionMargin);
            panel.Children.Add(edit);
        }

        return ComposePane(panel);
    }

    public Control BuildAccountPane(
        BackstageAccountPaneSpec spec,
        string optionsAutomationId = "BackstageAccountOptions")
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = CreatePane(_profile.AccountPaneMaxWidth);
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(spec.Heading, _style));
        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            spec.Description,
            _style,
            margin: ToThickness(_profile.DescriptionMargin)));

        foreach (var group in spec.Groups)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(group.Heading, _style));
            AddFields(panel, group.Fields, "Account_" + AutomationIdToken.KeepLettersAndDigits(group.Heading));
        }

        if (!string.IsNullOrWhiteSpace(spec.OptionsText) && spec.OpenOptions is not null)
        {
            var options = ActionButton(
                spec.OptionsText,
                spec.OptionsAutomationId ?? optionsAutomationId,
                spec.OpenOptions);
            options.FontSize = _profile.AccountOptionsFontSize;
            options.Margin = ToThickness(_profile.AccountOptionsMargin);
            panel.Children.Add(options);
        }

        return ComposePane(panel);
    }

    public Control BuildActionPane(BackstageActionPaneSpec spec, string automationPrefix) =>
        BuildActionPane(spec, automationPrefix, useClassicScrollChrome: false);

    public Control BuildExportActionPane(BackstageActionPaneSpec spec, string automationPrefix) =>
        BuildActionPane(spec, automationPrefix, useClassicScrollChrome: true);

    private Control BuildActionPane(
        BackstageActionPaneSpec spec,
        string automationPrefix,
        bool useClassicScrollChrome)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationPrefix);

        var panel = CreatePane(_profile.ActionPaneMaxWidth);
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(spec.Heading, _style));
        if (!string.IsNullOrWhiteSpace(spec.Description))
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
                spec.Description,
                _style,
                margin: ToThickness(_profile.DescriptionMargin)));
        }

        foreach (var group in spec.Groups)
            AddActionGroup(panel, group, automationPrefix);

        return ComposePane(panel, useClassicScrollChrome);
    }

    private void AddActionGroup(Panel panel, BackstageActionGroup group, string automationPrefix)
    {
        if (group.Actions.Count == 0)
            return;

        panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(group.Heading, _style));
        foreach (var action in group.Actions)
        {
            var automationId = action.ResolveAutomationId(automationPrefix + "_");
            if (_profile.UseLinkActionRows)
            {
                panel.Children.Add(LinkActionRow(action, automationId));
            }
            else
            {
                var button = AvaloniaBackstageChrome.CreateStackedActionButton(
                    new AvaloniaBackstageStackedActionButtonSpec(
                        action.Label,
                        action.Description,
                        automationId,
                        action.Invoke),
                    _style);
                button.IsEnabled = action.IsEnabled;
                panel.Children.Add(button);
            }
        }
    }

    private Button ActionButton(string text, string automationId, Action action)
    {
        if (!_profile.UseLinkActionRows)
        {
            return AvaloniaBackstageChrome.CreateActionButton(new AvaloniaBackstageActionButtonSpec(
                text,
                automationId,
                action)
            {
                HorizontalAlignment = HorizontalAlignment.Left,
            });
        }

        var button = new Button
        {
            Content = text,
            Foreground = _style.ActionInk ?? _style.PrimaryInk,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => action();
        return button;
    }

    private Control LinkActionRow(BackstageActionRow action, string automationId)
    {
        var row = new StackPanel { Margin = _style.ActionRowMargin };
        var button = ActionButton(action.Label, automationId, action.Invoke);
        button.FontSize = _style.ActionFontSize;
        button.IsEnabled = action.IsEnabled;
        if (_profile.UseTextBlockActionContent)
        {
            button.Content = new TextBlock
            {
                Text = action.Label,
                Foreground = _style.ActionInk ?? _style.PrimaryInk,
                FontSize = _style.ActionFontSize,
            };
        }
        row.Children.Add(button);

        if (!string.IsNullOrWhiteSpace(action.Description))
        {
            row.Children.Add(new TextBlock
            {
                Text = action.Description,
                Foreground = _style.SecondaryInk,
                FontSize = _style.ActionDescriptionFontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = _style.ActionDescriptionMargin,
            });
        }

        return row;
    }

    private void AddFields(Panel panel, IReadOnlyList<BackstageFieldRow> fields, string automationPrefix)
    {
        var grid = AvaloniaBackstageChrome.CreateDetailGrid(_style);
        foreach (var field in fields)
        {
            AvaloniaBackstageChrome.AddDetailRow(
                grid,
                field.Label,
                field.Value,
                automationPrefix + "_" + AutomationIdToken.KeepLettersAndDigits(field.Label),
                _style);
        }
        panel.Children.Add(grid);
    }

    private StackPanel CreatePane(double maxWidth) => new()
    {
        MaxWidth = maxWidth,
        HorizontalAlignment = HorizontalAlignment.Left,
        Spacing = _profile.PaneSpacing,
    };

    private Control ComposePane(Control content, bool useClassicScrollChrome = false)
    {
        if (!_profile.WrapPanesInScrollViewer)
            return content;

        var scroll = new ScrollViewer { Content = content };
        AvaloniaBackstageScrollChrome.Apply(scroll, _profile, useClassicScrollChrome);

        if (useClassicScrollChrome &&
            _style.ScrollTrackBrush is { } track &&
            _style.ScrollThumbBrush is { } thumb)
        {
            var width = _profile.ClassicScrollBarWidth;
            scroll.Styles.Add(new Style(selector => selector
                .OfType<ScrollBar>()
                .Class(":vertical"))
            {
                Setters =
                {
                    new Setter(Layoutable.WidthProperty, width),
                    new Setter(Layoutable.MinWidthProperty, width),
                    new Setter(Layoutable.MaxWidthProperty, width),
                    new Setter(TemplatedControl.BackgroundProperty, track),
                },
            });
            scroll.Styles.Add(new Style(selector => selector
                .OfType<ScrollBar>()
                .Class(":vertical")
                .Template()
                .OfType<global::Avalonia.Controls.Shapes.Rectangle>()
                .Name("TrackRect"))
            {
                Setters = { new Setter(global::Avalonia.Controls.Shapes.Shape.FillProperty, track) },
            });
            scroll.Styles.Add(new Style(selector => selector
                .OfType<ScrollBar>()
                .Class(":vertical")
                .Template()
                .OfType<Thumb>())
            {
                Setters =
                {
                    new Setter(Layoutable.WidthProperty, width),
                    new Setter(Layoutable.MinWidthProperty, width),
                    new Setter(Layoutable.MaxWidthProperty, width),
                    new Setter(TemplatedControl.BackgroundProperty, thumb),
                    new Setter(TemplatedControl.BorderBrushProperty, thumb),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                },
            });
        }

        return scroll;
    }

    private static Thickness ToThickness(BackstageVisualThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);

}
