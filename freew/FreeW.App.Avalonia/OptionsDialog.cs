using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// Compact Avalonia editor for the FreeW options that the cross-platform shell consumes today.
/// Parsing and normalization stay in <see cref="OptionsDialogPlanner"/>.
/// </summary>
internal sealed class OptionsDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            DefaultButtonBorderBrush = AvaloniaCompactDialogChrome.NeutralButtonBorderBrush,
        };

    private readonly FreeWOptions _seed;
    private readonly OptionsDialogSurfaceSpec _surface;
    private readonly TextBox _recentFilesCap = new() { Width = 72, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _defaultFormat = new() { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _uiLanguage = new() { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _autoCorrectEnabled = new();
    private readonly CheckBox _smartQuotes = new();
    private readonly CheckBox _dashes = new();
    private readonly CheckBox _ellipsis = new();
    private readonly CheckBox _symbols = new();
    private readonly CheckBox _capitalization = new();
    private readonly CheckBox _bulletedLists = new();
    private readonly CheckBox _numberedLists = new();
    private readonly CheckBox _ordinals = new();
    private readonly CheckBox _fractions = new();
    private readonly CheckBox _hyperlinks = new();
    private readonly CheckBox _correctTwoInitialCaps = new();
    private readonly CheckBox _capitalizeDayNames = new();
    private readonly CheckBox _replaceText = new();
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

    internal TextBox RecentFilesCapForTest => _recentFilesCap;

    internal IReadOnlyList<(TextBox Replace, TextBox With)> ReplacementEditorsForTest =>
        _replacementEditors.Select(row => (row.Replace, row.With)).ToArray();

    internal void AcceptForTest() => Accept();

    public OptionsDialog(FreeWOptions options)
    {
        _seed = options ?? new FreeWOptions();
        _surface = OptionsDialogPlanner.BuildSurface(_seed, SystemLanguageLabel());

        Title = _surface.Title;
        Width = OptionsDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _recentFilesCap.Text = _seed.RecentFilesCap.ToString();
        _defaultFormat.ItemsSource = _surface.General.FormatChoices;
        _defaultFormat.SelectedIndex = 0;
        _uiLanguage.Text = _seed.UiLanguage;
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
        tabs.Items.Add(new TabItem { Header = _surface.Tabs[0].Header, Content = BuildGeneralTab() });
        tabs.Items.Add(new TabItem { Header = _surface.AutoCorrect.Header, Content = BuildAutoCorrectTab() });
        tabs.Items.Add(new TabItem { Header = _surface.AutoFormat.Header, Content = BuildAutoFormatTab() });
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
        if (!OptionsDialogWorkflowPlanner.TryBuildResult(input, out var result, out var validation))
        {
            _status.Text = validation!.Message;
            _status.IsVisible = true;
            if (validation.Target == OptionsDialogValidationTarget.RecentFilesCap)
                AvaloniaCompactDialogChrome.FocusAndSelect(_recentFilesCap);
            return;
        }

        Result = result!;
        Close();
    }

    private OptionsDialogToggleKind[] CheckedToggles() =>
        Enum.GetValues<OptionsDialogToggleKind>()
            .Where(kind => ToggleFor(kind).IsChecked == true)
            .ToArray();

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
        AddRow(grid, 0, _surface.General.RecentFilesLabel, _recentFilesCap);
        AddRow(grid, 1, _surface.General.DefaultSaveFormatLabel, _defaultFormat);
        AddRow(grid, 2, _surface.General.UiLanguageLabel, _uiLanguage, _surface.General.UiLanguageHint);
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
        panel.Children.Add(_correctTwoInitialCaps);
        panel.Children.Add(_capitalizeDayNames);
        panel.Children.Add(_replaceText);
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

        void SyncReplacements() => _replacements.IsEnabled = OptionsDialogWorkflowPlanner.PlanEnabledState(
            _autoCorrectEnabled.IsChecked == true,
            _replaceText.IsChecked == true).ReplacementsEnabled;
        _replaceText.IsCheckedChanged += (_, _) => SyncReplacements();
        SyncReplacements();
        return panel;
    }

    private void BuildReplacementTable()
    {
        _replacementGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        _replacementGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
        _replacementGrid.RowDefinitions.Add(new RowDefinition(new GridLength(26)));
        _replacementGrid.Children.Add(HeaderCell("Replace", 0));
        _replacementGrid.Children.Add(HeaderCell("With", 1));

        OptionsDialogPlanner.TryParseAutoCorrectReplacements(
            _surface.AutoCorrect.ReplacementsText,
            out var replacements,
            out _);
        foreach (var replacement in replacements)
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
        ConfigureToggle(_autoCorrectEnabled, _surface.AutoFormat.MasterToggle, contentSpacing: 7);
        foreach (var spec in _surface.AutoFormat.RuleToggles)
            ConfigureToggle(ToggleFor(spec.Kind), spec, contentSpacing: 7);

        var ruleBoxes = new[]
        {
            _smartQuotes,
            _dashes,
            _ellipsis,
            _symbols,
            _capitalization,
            _bulletedLists,
            _numberedLists,
            _ordinals,
            _fractions,
            _hyperlinks,
        };

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
        _autoCorrectEnabled.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(_autoCorrectEnabled);
        panel.Children.Add(new TextBlock
        {
            Text = _surface.AutoFormat.RuleSectionLabel,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, OptionsDialogPlanner.ToggleTopMargin, 0, 0),
        });
        panel.Children.Add(rules);

        void SyncEnabled()
        {
            var enabledState = OptionsDialogWorkflowPlanner.PlanEnabledState(
                _autoCorrectEnabled.IsChecked == true,
                _replaceText.IsChecked == true);
            foreach (var box in ruleBoxes)
                box.IsEnabled = enabledState.AutoFormatRulesEnabled;
        }
        _autoCorrectEnabled.IsCheckedChanged += (_, _) => SyncEnabled();
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

    private CheckBox ToggleFor(OptionsDialogToggleKind kind) =>
        kind switch
        {
            OptionsDialogToggleKind.AutoCorrectEnabled => _autoCorrectEnabled,
            OptionsDialogToggleKind.SmartQuotes => _smartQuotes,
            OptionsDialogToggleKind.Dashes => _dashes,
            OptionsDialogToggleKind.Ellipsis => _ellipsis,
            OptionsDialogToggleKind.Symbols => _symbols,
            OptionsDialogToggleKind.Capitalization => _capitalization,
            OptionsDialogToggleKind.BulletedLists => _bulletedLists,
            OptionsDialogToggleKind.NumberedLists => _numberedLists,
            OptionsDialogToggleKind.Ordinals => _ordinals,
            OptionsDialogToggleKind.Fractions => _fractions,
            OptionsDialogToggleKind.Hyperlinks => _hyperlinks,
            OptionsDialogToggleKind.CorrectTwoInitialCapitals => _correctTwoInitialCaps,
            OptionsDialogToggleKind.CapitalizeDayNames => _capitalizeDayNames,
            OptionsDialogToggleKind.ReplaceText => _replaceText,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    private static void AddRow(Grid grid, int row, string label, Control field, string? hint = null)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var text = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);

        field.Margin = new Thickness(0, 4, 0, 4);

        Control value = field;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            value = new StackPanel
            {
                Children =
                {
                    field,
                    new TextBlock
                    {
                        Text = hint,
                        FontSize = 11,
                        Foreground = Brushes.Gray,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 4),
                    },
                },
            };
        }

        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);

        grid.Children.Add(text);
        grid.Children.Add(value);
    }

    private static string SystemLanguageLabel()
    {
        var name = System.Globalization.CultureInfo.CurrentCulture.Name;
        return string.IsNullOrEmpty(name) ? "invariant" : name;
    }

    private sealed class ReplacementEditor
    {
        public TextBox Replace { get; } = new();
        public TextBox With { get; } = new();
    }
}
