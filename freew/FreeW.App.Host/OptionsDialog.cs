using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// FreeW's modal settings editor, backed by <see cref="FreeWOptions"/>. It edits the real persisted
/// settings the model exposes today — the recent-files cap, the default save format, and the UI language
/// override — and nothing it cannot persist. On OK it builds a normalized <see cref="Result"/> options
/// object; the host then applies it live and saves it through the shared <c>JsonSettingsStore</c>.
///
/// <para>
/// Code-only to match the rest of the FreeW window style (see <see cref="PropertiesDialog"/>). The button
/// row, automatic content sizing, and initial focus come from the shared dialog helpers in
/// <c>Free.Shared.Shell</c>, and the surface itself from <c>Free.Shared.Ribbon.Wpf.DialogWindow</c>, so no
/// chrome is re-authored here.
/// </para>
/// </summary>
internal sealed class OptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly FreeWOptions _options;

    private readonly TextBox _recentFilesCap = new() { MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _defaultFormat = new() { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _uiLanguage = new() { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };

    // AutoFormat-As-You-Type tab: a master "AutoCorrect" switch plus one checkbox per rule. Disabling the
    // master switch greys out (and ignores) the per-rule boxes, matching Word's proofing UI.
    private readonly CheckBox _autoCorrectEnabled = new() { Content = "Enable AutoCorrect (smart typing) as you type" };
    private readonly CheckBox _smartQuotes = new() { Content = "Straight quotes with smart quotes (\" \" and ' ')" };
    private readonly CheckBox _dashes = new() { Content = "Hyphens (--) with dash (–/—)" };
    private readonly CheckBox _ellipsis = new() { Content = "Three periods (...) with ellipsis (…)" };
    private readonly CheckBox _symbols = new() { Content = "Symbols ( (c) (r) (tm) ) with © ® ™" };
    private readonly CheckBox _capitalization = new() { Content = "Capitalize first letter of sentences" };
    private readonly CheckBox _bulletedLists = new() { Content = "Automatic bulleted lists" };
    private readonly CheckBox _numberedLists = new() { Content = "Automatic numbered lists" };
    private readonly CheckBox _ordinals = new() { Content = "Ordinals (1st) with superscript" };
    private readonly CheckBox _fractions = new() { Content = "Fractions (1/2) with fraction character (½)" };
    private readonly CheckBox _hyperlinks = new() { Content = "Internet and network paths with hyperlinks" };

    /// <summary>The normalized options produced on OK; equals the input options on Cancel.</summary>
    public FreeWOptions Result { get; private set; }

    public OptionsDialog(Window owner, FreeWOptions options)
    {
        _options = options ?? new FreeWOptions();
        Result = _options;

        Owner = owner;
        Title = "FreeW Options";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        // The single .docx format FreeW ships today, surfaced as a (currently single-entry) picker so the
        // setting reads honestly and is ready to grow. The Tag carries the persisted extension value.
        _defaultFormat.Items.Add(new ComboBoxItem { Content = "Word Document (*.docx)", Tag = FreeWOptions.DocxDefaultFormat });
        _defaultFormat.SelectedIndex = 0;

        _recentFilesCap.Text = _options.RecentFilesCap.ToString(CultureInfo.CurrentCulture);
        _uiLanguage.Text = _options.UiLanguage;

        var tabs = new TabControl { Margin = new Thickness(14, 14, 14, 0) };
        tabs.Items.Add(new TabItem { Header = "General", Content = BuildGeneralTab() });
        tabs.Items.Add(new TabItem { Header = "AutoFormat As You Type", Content = BuildAutoFormatTab() });

        var buttons = DialogButtonRowFactory.Create(Commit, buttonWidth: 84, rowMargin: new Thickness(16, 8, 16, 12));

        var outer = new StackPanel();
        outer.Children.Add(tabs);
        outer.Children.Add(buttons);
        Content = outer;

        Loaded += (_, _) => DialogFocus.FocusAndSelect(_recentFilesCap);
    }

    private FrameworkElement BuildGeneralTab()
    {
        var grid = new Grid { Margin = new Thickness(16, 16, 16, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Recent files to keep:", _recentFilesCap);
        AddRow(grid, 1, "Default save format:", _defaultFormat);
        AddRow(grid, 2, "UI language:", _uiLanguage, hint: $"Empty = follow the system culture (currently {SystemLanguageLabel()}).");
        return grid;
    }

    private FrameworkElement BuildAutoFormatTab()
    {
        var af = _options.AutoFormat ?? AutoFormatOptions.Default;
        _autoCorrectEnabled.IsChecked = _options.AutoCorrectEnabled;
        _smartQuotes.IsChecked = af.SmartQuotes;
        _dashes.IsChecked = af.Dashes;
        _ellipsis.IsChecked = af.Ellipsis;
        _symbols.IsChecked = af.Symbols;
        _capitalization.IsChecked = af.Capitalization;
        _bulletedLists.IsChecked = af.BulletedLists;
        _numberedLists.IsChecked = af.NumberedLists;
        _ordinals.IsChecked = af.Ordinals;
        _fractions.IsChecked = af.Fractions;
        _hyperlinks.IsChecked = af.Hyperlinks;

        var ruleBoxes = new[]
        {
            _smartQuotes, _dashes, _ellipsis, _symbols, _capitalization,
            _bulletedLists, _numberedLists, _ordinals, _fractions, _hyperlinks,
        };

        var rules = new StackPanel { Margin = new Thickness(16, 4, 16, 0) };
        foreach (var box in ruleBoxes)
        {
            box.Margin = new Thickness(0, 4, 0, 0);
            rules.Children.Add(box);
        }

        // The per-rule boxes only apply when the master switch is on; mirror that in the UI by enabling /
        // disabling them with the master checkbox.
        void SyncEnabled()
        {
            foreach (var box in ruleBoxes)
                box.IsEnabled = _autoCorrectEnabled.IsChecked == true;
        }
        _autoCorrectEnabled.Checked += (_, _) => SyncEnabled();
        _autoCorrectEnabled.Unchecked += (_, _) => SyncEnabled();

        var panel = new StackPanel { Margin = new Thickness(16, 16, 16, 12) };
        _autoCorrectEnabled.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(_autoCorrectEnabled);
        panel.Children.Add(new TextBlock
        {
            Text = "Apply as you type:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0)
        });
        panel.Children.Add(rules);
        SyncEnabled();
        return panel;
    }

    private void Commit()
    {
        if (!OptionsDialogPlanner.TryParseRecentFilesCap(_recentFilesCap.Text, out var cap))
        {
            DialogMessageHelper.ShowWarning(
                this,
                $"Enter a whole number between {FreeWOptions.MinRecentFilesCap} and {FreeWOptions.MaxRecentFilesCap} for the recent-files count.",
                Title);
            DialogFocus.FocusAndSelect(_recentFilesCap);
            return;
        }

        var format = (_defaultFormat.SelectedItem as ComboBoxItem)?.Tag as string;
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
        Result = OptionsDialogPlanner.BuildResult(
            cap, format, _uiLanguage.Text, _autoCorrectEnabled.IsChecked == true, autoFormat);
        DialogResult = true;
    }

    private static string SystemLanguageLabel()
    {
        var name = CultureInfo.CurrentCulture.Name;
        return string.IsNullOrEmpty(name) ? "invariant" : name;
    }

    private static void AddRow(Grid grid, int row, string label, FrameworkElement field, string? hint = null)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 12, 0)
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        field.Margin = new Thickness(0, 8, 0, 0);

        if (hint is null)
        {
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
            return;
        }

        var stack = new StackPanel();
        stack.Children.Add(field);
        stack.Children.Add(new TextBlock
        {
            Text = hint,
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetRow(stack, row);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
    }
}
