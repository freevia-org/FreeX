using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Globalization;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// Compact Avalonia editor for the FreeW options that the cross-platform shell consumes today.
/// Parsing, normalization, and commit planning stay in <see cref="OptionsDialogSession"/>.
/// </summary>
internal sealed partial class OptionsDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            DefaultButtonBorderBrush = AvaloniaCompactDialogChrome.NeutralButtonBorderBrush,
        };

    private readonly OptionsDialogSession _session;
    private readonly OptionsDialogSurfaceSpec _surface;
    private readonly TextBox _recentFilesCap = new() { Width = 72, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _defaultFormat = new() { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _uiLanguage = new() { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly IReadOnlyDictionary<OptionsDialogToggleKind, CheckBox> _toggles;
    private readonly Border _replacements = new()
    {
        Height = OptionsDialogPlanner.ReplacementTableHeight,
        Margin = new Thickness(0, 7, 0, 0),
        VerticalAlignment = VerticalAlignment.Top
    };
    private readonly Grid _replacementGrid = new();
    private readonly List<ReplacementEditor> _replacementEditors = [];
    private readonly TextBlock _status = new();

    public FreeWOptions? Result { get; private set; }

    public OptionsDialog(FreeWOptions options)
    {
        _session = new OptionsDialogSession(options, CultureInfo.CurrentCulture);
        _surface = _session.Surface;
        _toggles = Enum.GetValues<OptionsDialogToggleKind>()
            .ToDictionary(kind => kind, _ => new CheckBox());

        Title = _surface.Title;
        Width = OptionsDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _recentFilesCap.Text = _session.InitialState.RecentFilesCapText;
        _defaultFormat.ItemsSource = _surface.General.FormatChoices;
        _defaultFormat.SelectedItem = _surface.General.FormatChoices.FirstOrDefault(choice =>
            choice.Extension == _session.InitialState.SelectedFormat);
        if (_defaultFormat.SelectedIndex < 0)
            _defaultFormat.SelectedIndex = 0;
        _uiLanguage.Text = _session.InitialState.UiLanguage;
        BuildReplacementTable();

        AvaloniaCompactDialogChrome.ApplyTextBox(_recentFilesCap, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_defaultFormat, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_uiLanguage, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(16, 8, 16, 0));

        var tabs = new TabControl
        {
            Margin = new Thickness(OptionsDialogPlanner.TabMargin, OptionsDialogPlanner.TabMargin, OptionsDialogPlanner.TabMargin, 0),
            Focusable = true,
            IsTabStop = true,
        };
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            tabs,
            DialogChromeStyle,
            contentPaneMargin: new Thickness(-12, -1, 0, 0));
        var tabContents = new[] { BuildGeneralTab(), BuildAutoCorrectTab(), BuildAutoFormatTab() };
        for (var index = 0; index < _surface.Tabs.Count; index++)
        {
            var tab = new TabItem { Header = _surface.Tabs[index].Header, Content = tabContents[index] };
            AutomationProperties.SetAutomationId(tab, _surface.Tabs[index].AutomationId);
            tabs.Items.Add(tab);
        }
        void ApplyAutoCorrectPaneInset()
        {
            tabs.ApplyTemplate();
            var selectedPane = tabs.GetVisualDescendants()
                .OfType<ContentPresenter>()
                .FirstOrDefault(presenter => presenter.Name == "PART_SelectedContentHost");
            if (selectedPane is not null)
                selectedPane.Margin = new Thickness(
                    0,
                    -1,
                    tabs.SelectedIndex == 1 ? OptionsDialogPlanner.AutoCorrectTabPaneRightInset : 0,
                    0);
        }
        tabs.SelectionChanged += (_, _) =>
        {
            // WPF keeps focus on the tab strip when a secondary options page is selected;
            // keeping that focus target also prevents the default OK adorner from appearing
            // on a page that the user has not keyboard-focused.
            if (tabs.SelectedIndex > 0)
                tabs.Focus();
            Dispatcher.UIThread.Post(ApplyAutoCorrectPaneInset, DispatcherPriority.Render);
        };
        tabs.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(ApplyAutoCorrectPaneInset, DispatcherPriority.Render);

        var buttons = AvaloniaDialogButtonRowFactory.CreateOkCancel(
            Accept,
            Close,
            buttonWidth: OptionsDialogPlanner.ActionButtonWidth,
            rowMargin: new Thickness(
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ActionRowTopMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ActionRowBottomMargin),
            style: DialogChromeStyle);

        Content = new StackPanel
        {
            Children =
            {
                tabs,
                _status,
                buttons,
            },
        };

        Opened += (_, _) =>
        {
            // FreeWDialogWindow applies the shared default chrome during construction. Reapply
            // this route's WPF action-row palette after the visual tree exists so the default
            // button remains neutral gray until it is actually focused.
            AvaloniaCompactDialogChrome.ApplyDescendantChrome(this, DialogChromeStyle);
            _recentFilesCap.HorizontalAlignment = HorizontalAlignment.Left;
            _defaultFormat.HorizontalAlignment = HorizontalAlignment.Left;
            _uiLanguage.HorizontalAlignment = HorizontalAlignment.Left;
            AvaloniaCompactDialogChrome.FocusAndSelect(_recentFilesCap);
        };
    }

    private void Accept()
    {
        _status.IsVisible = false;
        var input = new OptionsDialogInput(
            _recentFilesCap.Text,
            (_defaultFormat.SelectedItem as OptionsDialogFormatChoice)?.Extension,
            _uiLanguage.Text,
            CheckedToggles(),
            _replacementEditors
                .Select(row => new OptionsDialogReplacementInput(row.Replace.Text, row.With.Text))
                .ToArray());
        var plan = _session.PlanAcceptance(input);
        if (!plan.ShouldApply)
        {
            _status.Text = plan.Validation!.Message;
            _status.IsVisible = true;
            if (plan.Validation.Target == BasicApplicationOptionsValidationTarget.RecentFilesCap)
                AvaloniaCompactDialogChrome.FocusAndSelect(_recentFilesCap);
            return;
        }

        Result = plan.Result!;
        Close();
    }

    private OptionsDialogToggleKind[] CheckedToggles() =>
        _toggles.Where(entry => entry.Value.IsChecked == true).Select(entry => entry.Key).ToArray();

    private Control BuildGeneralTab()
    {
        var grid = new Grid
        {
            Margin = new Thickness(
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentBottomMargin),
            ColumnDefinitions = new ColumnDefinitions
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };
        for (var index = 0; index < _surface.General.Fields.Count; index++)
        {
            var field = _surface.General.Fields[index];
            AvaloniaLabeledFormRow.Add(grid, index, field.Label, GeneralControlFor(field.Kind), field.Hint);
        }
        return grid;
    }

    private Control BuildAutoCorrectTab()
    {
        foreach (var spec in _surface.AutoCorrect.Toggles)
            ConfigureToggle(ToggleFor(spec.Kind), spec);

        var panel = new StackPanel
        {
            Margin = new Thickness(
                OptionsDialogPlanner.ContentMargin + 2,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin + 2,
                OptionsDialogPlanner.ContentBottomMargin + 3)
        };
        foreach (var spec in _surface.AutoCorrect.Toggles)
            panel.Children.Add(ToggleFor(spec.Kind));
        panel.Children.Add(new TextBlock
        {
            Text = _surface.AutoCorrect.ReplacementsLabel,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, OptionsDialogPlanner.SectionHeaderTopMargin, 0, 0),
        });
        panel.Children.Add(_replacements);
        panel.Children.Add(new TextBlock
        {
            Text = _surface.AutoCorrect.ReplacementsHelpText,
            FontSize = OptionsDialogPlanner.HelpTextFontSize,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, OptionsDialogPlanner.ToggleTopMargin + 2, 0, 0),
        });

        var replaceTextToggle = ToggleFor(OptionsDialogToggleKind.ReplaceText);
        void SyncReplacements() => _replacements.IsEnabled = _session.PlanEnabledState(
            ToggleFor(OptionsDialogToggleKind.AutoCorrectEnabled).IsChecked == true,
            replaceTextToggle.IsChecked == true).ReplacementsEnabled;
        replaceTextToggle.IsCheckedChanged += (_, _) => SyncReplacements();
        SyncReplacements();
        return panel;
    }

    private void BuildReplacementTable()
    {
        foreach (var column in _surface.AutoCorrect.ReplacementColumns)
        {
            _replacementGrid.ColumnDefinitions.Add(
                new ColumnDefinition(new GridLength(column.WidthWeight, GridUnitType.Star)));
        }
        _replacementGrid.RowDefinitions.Add(new RowDefinition(new GridLength(26)));
        for (var index = 0; index < _surface.AutoCorrect.ReplacementColumns.Count; index++)
            _replacementGrid.Children.Add(HeaderCell(_surface.AutoCorrect.ReplacementColumns[index].Header, index));

        foreach (var replacement in _session.InitialState.Replacements)
            AddReplacementRow(replacement.Replace, replacement.With);

        AddReplacementRow();
        _replacements.Child = new ScrollViewer
        {
            Content = _replacementGrid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _replacements.BorderBrush = new SolidColorBrush(Color.FromRgb(171, 173, 179));
        _replacements.BorderThickness = new Thickness(1);
        _replacements.ClipToBounds = true;
    }

    private void AddReplacementRow(string replace = "", string with = "")
    {
        var row = new ReplacementEditor();
        row.Replace.Text = replace;
        row.With.Text = with;
        AvaloniaCompactDialogChrome.ApplyTextBox(row.Replace, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(row.With, DialogChromeStyle);
        var gridlineBrush = Brushes.Black;
        row.Replace.BorderBrush = gridlineBrush;
        row.With.BorderBrush = gridlineBrush;

        var rowIndex = _replacementGrid.RowDefinitions.Count;
        _replacementGrid.RowDefinitions.Add(new RowDefinition(new GridLength(20)));
        Grid.SetRow(row.Replace, rowIndex);
        Grid.SetColumn(row.Replace, 0);
        Grid.SetRow(row.With, rowIndex);
        Grid.SetColumn(row.With, 1);
        _replacementGrid.Children.Add(row.Replace);
        _replacementGrid.Children.Add(row.With);
        _replacementEditors.Add(row);

        row.Replace.TextChanged += ReplacementChanged;
        row.With.TextChanged += ReplacementChanged;
    }

    private void ReplacementChanged(object? sender, TextChangedEventArgs e)
    {
        if (_replacementEditors.Count == 0)
            return;

        var last = _replacementEditors[^1];
        if (!string.IsNullOrWhiteSpace(last.Replace.Text) || !string.IsNullOrWhiteSpace(last.With.Text))
            AddReplacementRow();
    }

    private static Border HeaderCell(string text, int column)
    {
        var cell = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(171, 173, 179)),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = new TextBlock
            {
                Text = text,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(4, 3),
            },
        };
        Grid.SetRow(cell, 0);
        Grid.SetColumn(cell, column);
        return cell;
    }

    private Control BuildAutoFormatTab()
    {
        var masterToggle = ToggleFor(_surface.AutoFormat.MasterToggle.Kind);
        ConfigureToggle(masterToggle, _surface.AutoFormat.MasterToggle, contentSpacing: 7);
        foreach (var spec in _surface.AutoFormat.RuleToggles)
            ConfigureToggle(ToggleFor(spec.Kind), spec, contentSpacing: 7);

        var ruleBoxes = _surface.AutoFormat.RuleToggles
            .Select(spec => ToggleFor(spec.Kind))
            .ToArray();

        var rules = new StackPanel
        {
            Margin = new Thickness(
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ToggleTopMargin,
                OptionsDialogPlanner.ContentMargin,
                0)
        };
        foreach (var box in ruleBoxes)
            rules.Children.Add(box);

        var panel = new StackPanel
        {
            Margin = new Thickness(
                OptionsDialogPlanner.ContentMargin + OptionsDialogPlanner.ToggleTopMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentBottomMargin)
        };
        masterToggle.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(masterToggle);
        panel.Children.Add(new TextBlock
        {
            Text = _surface.AutoFormat.RuleSectionLabel,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, OptionsDialogPlanner.ToggleTopMargin, 0, 0),
        });
        panel.Children.Add(rules);

        void SyncEnabled()
        {
            var enabledState = _session.PlanEnabledState(
                masterToggle.IsChecked == true,
                ToggleFor(OptionsDialogToggleKind.ReplaceText).IsChecked == true);
            foreach (var box in ruleBoxes)
                box.IsEnabled = enabledState.AutoFormatRulesEnabled;
        }
        masterToggle.IsCheckedChanged += (_, _) => SyncEnabled();
        SyncEnabled();
        return panel;
    }

    private static void ConfigureToggle(
        CheckBox box,
        OptionsDialogToggleSpec spec,
        double contentSpacing = 4)
    {
        box.Content = spec.Label;
        box.IsChecked = spec.IsChecked;
        box.Margin = new Thickness(0, OptionsDialogPlanner.ToggleTopMargin, 0, 0);
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(box, DialogChromeStyle, contentSpacing);
        box.Height = 16;
        box.MinHeight = 16;
        box.MaxHeight = 16;
    }

    private CheckBox ToggleFor(OptionsDialogToggleKind kind) => _toggles[kind];

    private Control GeneralControlFor(OptionsDialogGeneralFieldKind kind) =>
        kind switch
        {
            OptionsDialogGeneralFieldKind.RecentFilesCap => _recentFilesCap,
            OptionsDialogGeneralFieldKind.DefaultSaveFormat => _defaultFormat,
            OptionsDialogGeneralFieldKind.UiLanguage => _uiLanguage,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    private sealed class ReplacementEditor
    {
        public TextBox Replace { get; } = new();
        public TextBox With { get; } = new();
    }
}
