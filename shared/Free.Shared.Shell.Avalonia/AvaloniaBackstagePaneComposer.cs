using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Projects portable sister-app pane specs to Avalonia controls. Apps retain only native template
/// tiles and command adapters while labels, rows, grouping, and status remain renderer-neutral.
/// </summary>
public sealed class AvaloniaBackstagePaneComposer
{
    private const string DirtySuffix = "  (unsaved changes)";

    private readonly AvaloniaBackstageChromeStyle _style;

    public AvaloniaBackstagePaneComposer(AvaloniaBackstageChromeStyle style)
    {
        _style = style ?? throw new ArgumentNullException(nameof(style));
    }

    public Control BuildInfoPane(BackstageInfoPaneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = CreatePane();
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(BackstageInfoPaneText.Title, _style));
        AddField(panel, spec.DocumentKindLabel, spec.DisplayName + (spec.IsDirty ? DirtySuffix : string.Empty));
        AddField(panel, BackstageInfoPaneText.LocationLabel, spec.Location ?? BackstageInfoPaneText.NotSavedYet);

        if (spec.Properties.Count > 0)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(BackstageInfoPaneText.PropertiesHeading, _style));
            AddFields(panel, spec.Properties, "InfoProperty");
        }

        if (!string.IsNullOrWhiteSpace(spec.EditPropertiesText) && spec.EditProperties is not null)
            panel.Children.Add(ActionButton(spec.EditPropertiesText, "BackstageEditProperties", spec.EditProperties));

        if (spec.Statistics.Count > 0)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(BackstageInfoPaneText.StatisticsHeading, _style));
            AddFields(panel, spec.Statistics, "InfoStatistic");
        }

        foreach (var group in spec.ActionGroups ?? [])
            AddActionGroup(panel, group, "BackstageInfoAction");

        return panel;
    }

    public Control BuildRecentPane(BackstageRecentPaneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = CreatePane();
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading("Recent", _style));
        if (spec.Paths.Count == 0)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
                spec.EmptyText,
                _style,
                margin: new Thickness(0, 4, 0, 0)));
            return panel;
        }

        foreach (var path in spec.Paths)
        {
            var capturedPath = path;
            panel.Children.Add(AvaloniaBackstageChrome.CreateStackedActionButton(
                new AvaloniaBackstageStackedActionButtonSpec(
                    BackstageRecentActionRowsPlanner.FileNameOrPath(capturedPath),
                    capturedPath,
                    "BackstageRecent_" + AutomationToken(capturedPath),
                    () => spec.OpenPath(capturedPath)),
                _style));
        }

        return panel;
    }

    public Control BuildTemplatePane(
        BackstageTemplatePaneSpec spec,
        Func<string, Action, Control> buildTemplateTile)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(buildTemplateTile);

        var panel = CreatePane();
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(spec.Heading, _style));
        panel.Children.Add(buildTemplateTile(spec.TileCaption, spec.Create));
        if (!string.IsNullOrWhiteSpace(spec.FooterText))
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
                spec.FooterText,
                _style,
                margin: new Thickness(0, 18, 0, 0)));
        }

        return panel;
    }

    public Control BuildOptionsPane(
        BackstageOptionsPaneSpec spec,
        string editAutomationId = "BackstageEditOptions")
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = CreatePane(560);
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading("Options", _style));
        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            spec.Description,
            _style,
            margin: new Thickness(0, 0, 0, 8)));
        AddFields(panel, spec.Fields, "Options");

        if (!string.IsNullOrWhiteSpace(spec.EditText) && spec.Edit is not null)
            panel.Children.Add(ActionButton(spec.EditText, editAutomationId, spec.Edit));

        return panel;
    }

    public Control BuildAccountPane(
        BackstageAccountPaneSpec spec,
        string optionsAutomationId = "BackstageAccountOptions")
    {
        ArgumentNullException.ThrowIfNull(spec);

        var panel = CreatePane();
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(spec.Heading, _style));
        panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
            spec.Description,
            _style,
            margin: new Thickness(0, 0, 0, 8)));

        foreach (var group in spec.Groups)
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(group.Heading, _style));
            AddFields(panel, group.Fields, "Account_" + AutomationToken(group.Heading));
        }

        if (!string.IsNullOrWhiteSpace(spec.OptionsText) && spec.OpenOptions is not null)
            panel.Children.Add(ActionButton(spec.OptionsText, optionsAutomationId, spec.OpenOptions));

        return panel;
    }

    public Control BuildActionPane(BackstageActionPaneSpec spec, string automationPrefix)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationPrefix);

        var panel = CreatePane(720);
        panel.Children.Add(AvaloniaBackstageChrome.CreateHeading(spec.Heading, _style));
        if (!string.IsNullOrWhiteSpace(spec.Description))
        {
            panel.Children.Add(AvaloniaBackstageChrome.CreateNote(
                spec.Description,
                _style,
                margin: new Thickness(0, 0, 0, 8)));
        }

        foreach (var group in spec.Groups)
            AddActionGroup(panel, group, automationPrefix);

        return panel;
    }

    private void AddActionGroup(Panel panel, BackstageActionGroup group, string automationPrefix)
    {
        if (group.Actions.Count == 0)
            return;

        panel.Children.Add(AvaloniaBackstageChrome.CreateSectionHeader(group.Heading, _style));
        foreach (var action in group.Actions)
        {
            var button = AvaloniaBackstageChrome.CreateStackedActionButton(
                new AvaloniaBackstageStackedActionButtonSpec(
                    action.Label,
                    action.Description,
                    action.AutomationId ?? automationPrefix + "_" + AutomationToken(action.Label),
                    action.Invoke),
                _style);
            button.IsEnabled = action.IsEnabled;
            panel.Children.Add(button);
        }
    }

    private Button ActionButton(string text, string automationId, Action action) =>
        AvaloniaBackstageChrome.CreateActionButton(new AvaloniaBackstageActionButtonSpec(
            text,
            automationId,
            action)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
        });

    private void AddFields(Panel panel, IReadOnlyList<BackstageFieldRow> fields, string automationPrefix)
    {
        var grid = AvaloniaBackstageChrome.CreateDetailGrid();
        foreach (var field in fields)
        {
            AvaloniaBackstageChrome.AddDetailRow(
                grid,
                field.Label,
                field.Value,
                automationPrefix + "_" + AutomationToken(field.Label),
                _style);
        }
        panel.Children.Add(grid);
    }

    private void AddField(Panel panel, string label, string value) =>
        AddFields(panel, [new BackstageFieldRow(label, value)], "BackstageField");

    private static StackPanel CreatePane(double maxWidth = 640) => new()
    {
        MaxWidth = maxWidth,
        HorizontalAlignment = HorizontalAlignment.Left,
        Spacing = 10,
    };

    private static string AutomationToken(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit));
}
