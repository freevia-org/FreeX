using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
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
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly FreeWOptions _seed;
    private readonly OptionsDialogSurfaceSpec _surface;
    private readonly TextBox _recentFilesCap = new() { Width = 72 };
    private readonly ComboBox _defaultFormat = new() { Width = 180 };
    private readonly TextBox _uiLanguage = new() { Width = 180 };
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
    private readonly Border _replacements = new() { Height = 180, VerticalAlignment = VerticalAlignment.Top };
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
        Width = 460;
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

        var tabs = new TabControl { Margin = new Thickness(14, 14, 14, 0) };
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(tabs);
        tabs.Items.Add(new TabItem { Header = _surface.Tabs[0].Header, Content = BuildGeneralTab() });
        tabs.Items.Add(new TabItem { Header = _surface.AutoCorrect.Header, Content = BuildAutoCorrectTab() });
        tabs.Items.Add(new TabItem { Header = _surface.AutoFormat.Header, Content = BuildAutoFormatTab() });

        var buttons = AvaloniaDialogButtonRowFactory.CreateOkCancel(
            Accept,
            Close,
            buttonWidth: 84,
            rowMargin: new Thickness(16, 8, 16, 12),
            style: DialogChromeStyle);
        DockPanel.SetDock(buttons, Dock.Bottom);

        Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                buttons,
                new StackPanel { Children = { tabs, _status } },
            },
        };

        Opened += (_, _) => AvaloniaCompactDialogChrome.FocusAndSelect(_recentFilesCap);
    }

    private void Accept()
    {
        _status.IsVisible = false;
        if (!OptionsDialogPlanner.TryParseRecentFilesCap(_recentFilesCap.Text, out var cap))
        {
            _status.Text = $"Enter a whole number between {FreeWOptions.MinRecentFilesCap} and {FreeWOptions.MaxRecentFilesCap}.";
            _status.IsVisible = true;
            _recentFilesCap.Focus();
            return;
        }

        var format = (_defaultFormat.SelectedItem as OptionsDialogFormatChoice)?.Extension;
        var autoFormat = new AutoFormatOptions
        {
            SmartQuotes = _smartQuotes.IsChecked == true,
            Dashes = _dashes.IsChecked == true,
            Ellipsis = _ellipsis.IsChecked == true,
            Symbols = _symbols.IsChecked == true,
            Capitalization = _capitalization.IsChecked == true,
            BulletedLists = _bulletedLists.IsChecked == true,
            NumberedLists = _numberedLists.IsChecked == true,
            Ordinals = _ordinals.IsChecked == true,
            Fractions = _fractions.IsChecked == true,
            Hyperlinks = _hyperlinks.IsChecked == true,
        };
        var autoCorrect = new AutoCorrectOptions
        {
            CorrectTwoInitialCapitals = _correctTwoInitialCaps.IsChecked == true,
            CapitalizeDayNames = _capitalizeDayNames.IsChecked == true,
            ReplaceText = _replaceText.IsChecked == true,
            Replacements = _replacementEditors
                .Select(row => new { Replace = row.Replace.Text, With = row.With.Text })
                .Where(row => !string.IsNullOrWhiteSpace(row.Replace) && !string.IsNullOrWhiteSpace(row.With))
                .Select(row => new AutoCorrectReplacement(row.Replace!.Trim(), row.With!))
                .ToList(),
        };

        Result = OptionsDialogPlanner.BuildResult(
            cap,
            format,
            _uiLanguage.Text,
            _autoCorrectEnabled.IsChecked == true,
            autoFormat,
            autoCorrect);
        Close();
    }

    private Control BuildGeneralTab()
    {
        var grid = new Grid
        {
            Margin = new Thickness(16, 16, 16, 12),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
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

        var panel = new StackPanel { Margin = new Thickness(16, 16, 16, 12), Spacing = 4 };
        panel.Children.Add(_correctTwoInitialCaps);
        panel.Children.Add(_capitalizeDayNames);
        panel.Children.Add(_replaceText);
        panel.Children.Add(new TextBlock
        {
            Text = _surface.AutoCorrect.ReplacementsLabel,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 0),
        });
        panel.Children.Add(_replacements);
        panel.Children.Add(new TextBlock
        {
            Text = _surface.AutoCorrect.ReplacementsHelpText,
            FontSize = 11,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });

        void SyncReplacements() => _replacements.IsEnabled = _replaceText.IsChecked == true;
        _replaceText.IsCheckedChanged += (_, _) => SyncReplacements();
        SyncReplacements();
        return panel;
    }

    private void BuildReplacementTable()
    {
        _replacementGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        _replacementGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
        _replacementGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _replacementGrid.Children.Add(HeaderCell("Replace", 0));
        _replacementGrid.Children.Add(HeaderCell("With", 1));

        foreach (var replacement in _seed.AutoCorrect?.Replacements ?? [])
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

        var rowIndex = _replacementGrid.RowDefinitions.Count;
        _replacementGrid.RowDefinitions.Add(new RowDefinition(new GridLength(24)));
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
        ConfigureToggle(_autoCorrectEnabled, _surface.AutoFormat.MasterToggle);
        foreach (var spec in _surface.AutoFormat.RuleToggles)
            ConfigureToggle(ToggleFor(spec.Kind), spec);

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

        var rules = new StackPanel { Margin = new Thickness(16, 4, 16, 0), Spacing = 4 };
        foreach (var box in ruleBoxes)
            rules.Children.Add(box);

        var panel = new StackPanel { Margin = new Thickness(16, 16, 16, 12) };
        panel.Children.Add(_autoCorrectEnabled);
        panel.Children.Add(new TextBlock
        {
            Text = _surface.AutoFormat.RuleSectionLabel,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 0),
        });
        panel.Children.Add(rules);

        void SyncEnabled()
        {
            foreach (var box in ruleBoxes)
                box.IsEnabled = _autoCorrectEnabled.IsChecked == true;
        }
        _autoCorrectEnabled.IsCheckedChanged += (_, _) => SyncEnabled();
        SyncEnabled();
        return panel;
    }

    private static void ConfigureToggle(CheckBox box, OptionsDialogToggleSpec spec)
    {
        box.Content = spec.Label;
        box.IsChecked = spec.IsChecked;
        box.Margin = new Thickness(0, 4, 0, 0);
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
