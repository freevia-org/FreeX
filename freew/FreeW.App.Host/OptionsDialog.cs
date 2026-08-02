using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Options;
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
    private readonly OptionsDialogSurfaceSpec _surface;

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

    // AutoCorrect tab (Word's Proofing > AutoCorrect Options): the word-completion rules plus the editable
    // "replace text as you type" table. Distinct from the AutoFormat-As-You-Type tab above.
    private readonly CheckBox _correctTwoInitialCaps = new() { Content = "Correct TWo INitial CApitals" };
    private readonly CheckBox _capitalizeDayNames = new() { Content = "Capitalize names of days" };
    private readonly CheckBox _replaceText = new() { Content = "Replace text as you type" };
    private readonly DataGrid _replacements = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = true,
        CanUserDeleteRows = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        Height = OptionsDialogPlanner.ReplacementTableHeight,
        Margin = new Thickness(0, 6, 0, 0),
    };
    private readonly ObservableCollection<ReplacementRow> _replacementRows = new();

    /// <summary>The normalized options produced on OK; equals the input options on Cancel.</summary>
    public FreeWOptions Result { get; private set; }

    public OptionsDialog(Window owner, FreeWOptions options)
    {
        _options = options ?? new FreeWOptions();
        _surface = OptionsDialogPlanner.BuildSurface(_options, SystemLanguageLabel());
        Result = _options;

        Owner = owner;
        Title = _surface.Title;
        Width = OptionsDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        // The single .docx format FreeW ships today, surfaced as a (currently single-entry) picker so the
        // setting reads honestly and is ready to grow. The Tag carries the persisted extension value.
        foreach (var choice in _surface.General.FormatChoices)
            _defaultFormat.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Extension });
        _defaultFormat.SelectedIndex = 0;

        _recentFilesCap.Text = _options.RecentFilesCap.ToString(CultureInfo.CurrentCulture);
        _uiLanguage.Text = _options.UiLanguage;

        var tabs = new TabControl { Margin = new Thickness(OptionsDialogPlanner.TabMargin, OptionsDialogPlanner.TabMargin, OptionsDialogPlanner.TabMargin, 0) };
        tabs.Items.Add(new TabItem { Header = _surface.Tabs[0].Header, Content = BuildGeneralTab() });
        tabs.Items.Add(new TabItem { Header = _surface.AutoCorrect.Header, Content = BuildAutoCorrectTab() });
        tabs.Items.Add(new TabItem { Header = _surface.AutoFormat.Header, Content = BuildAutoFormatTab() });

        var buttons = DialogButtonRowFactory.Create(
            Commit,
            buttonWidth: OptionsDialogPlanner.ActionButtonWidth,
            rowMargin: new Thickness(
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ActionRowTopMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ActionRowBottomMargin));

        var outer = new StackPanel();
        outer.Children.Add(tabs);
        outer.Children.Add(buttons);
        Content = outer;

        Loaded += (_, _) => DialogFocus.FocusAndSelect(_recentFilesCap);
    }

    private FrameworkElement BuildGeneralTab()
    {
        var grid = new Grid
        {
            Margin = new Thickness(
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentBottomMargin)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, _surface.General.RecentFilesLabel, _recentFilesCap);
        AddRow(grid, 1, _surface.General.DefaultSaveFormatLabel, _defaultFormat);
        AddRow(grid, 2, _surface.General.UiLanguageLabel, _uiLanguage, hint: _surface.General.UiLanguageHint);
        return grid;
    }

    private FrameworkElement BuildAutoFormatTab()
    {
        ConfigureToggle(_autoCorrectEnabled, _surface.AutoFormat.MasterToggle);
        foreach (var spec in _surface.AutoFormat.RuleToggles)
            ConfigureToggle(ToggleFor(spec.Kind), spec);

        var ruleBoxes = new[]
        {
            _smartQuotes, _dashes, _ellipsis, _symbols, _capitalization,
            _bulletedLists, _numberedLists, _ordinals, _fractions, _hyperlinks,
        };

        var rules = new StackPanel { Margin = new Thickness(OptionsDialogPlanner.ContentMargin, OptionsDialogPlanner.ToggleTopMargin, OptionsDialogPlanner.ContentMargin, 0) };
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

        var panel = new StackPanel
        {
            Margin = new Thickness(
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentBottomMargin)
        };
        _autoCorrectEnabled.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(_autoCorrectEnabled);
        panel.Children.Add(new TextBlock
        {
            Text = _surface.AutoFormat.RuleSectionLabel,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, OptionsDialogPlanner.ToggleTopMargin, 0, 0)
        });
        panel.Children.Add(rules);
        SyncEnabled();
        return panel;
    }

    private FrameworkElement BuildAutoCorrectTab()
    {
        foreach (var spec in _surface.AutoCorrect.Toggles)
            ConfigureToggle(ToggleFor(spec.Kind), spec);

        OptionsDialogPlanner.TryParseAutoCorrectReplacements(
            _surface.AutoCorrect.ReplacementsText,
            out var replacements,
            out _);
        foreach (var r in replacements)
            _replacementRows.Add(new ReplacementRow { Replace = r.Replace, With = r.With });

        _replacements.Columns.Add(new DataGridTextColumn
        {
            Header = "Replace",
            Binding = new System.Windows.Data.Binding(nameof(ReplacementRow.Replace)) { Mode = System.Windows.Data.BindingMode.TwoWay },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _replacements.Columns.Add(new DataGridTextColumn
        {
            Header = "With",
            Binding = new System.Windows.Data.Binding(nameof(ReplacementRow.With)) { Mode = System.Windows.Data.BindingMode.TwoWay },
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
        });
        _replacements.ItemsSource = _replacementRows;

        var toggles = new[] { _correctTwoInitialCaps, _capitalizeDayNames, _replaceText };

        var panel = new StackPanel
        {
            Margin = new Thickness(
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentBottomMargin)
        };
        foreach (var box in toggles)
        {
            box.Margin = new Thickness(0, OptionsDialogPlanner.ToggleTopMargin, 0, 0);
            panel.Children.Add(box);
        }
        panel.Children.Add(new TextBlock
        {
            Text = _surface.AutoCorrect.ReplacementsLabel,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, OptionsDialogPlanner.SectionHeaderTopMargin, 0, 0),
        });
        panel.Children.Add(_replacements);
        panel.Children.Add(new TextBlock
        {
            Text = _surface.AutoCorrect.ReplacementsHelpText,
            FontSize = OptionsDialogPlanner.HelpTextFontSize,
            Foreground = System.Windows.Media.Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, OptionsDialogPlanner.ToggleTopMargin + 2, 0, 0),
        });

        // The replace table only applies when "Replace text as you type" is on; mirror that in the UI.
        void SyncTable() => _replacements.IsEnabled = _replaceText.IsChecked == true;
        _replaceText.Checked += (_, _) => SyncTable();
        _replaceText.Unchecked += (_, _) => SyncTable();
        SyncTable();

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
        // Commit any in-progress cell edit so the last-typed row is captured before we read the rows.
        _replacements.CommitEdit(DataGridEditingUnit.Row, true);

        var autoCorrect = new AutoCorrectOptions
        {
            CorrectTwoInitialCapitals = _correctTwoInitialCaps.IsChecked == true,
            CapitalizeDayNames = _capitalizeDayNames.IsChecked == true,
            ReplaceText = _replaceText.IsChecked == true,
            Replacements = _replacementRows
                .Where(r => !string.IsNullOrWhiteSpace(r.Replace) && !string.IsNullOrEmpty(r.With))
                .Select(r => new AutoCorrectReplacement(r.Replace!.Trim(), r.With!))
                .ToList(),
        };

        Result = OptionsDialogPlanner.BuildResult(
            cap, format, _uiLanguage.Text, _autoCorrectEnabled.IsChecked == true, autoFormat, autoCorrect);
        DialogResult = true;
    }

    // A mutable two-property row backing the AutoCorrect replace-table DataGrid (DataGrid edits need a
    // settable, public class; the immutable AutoCorrectReplacement record is built from these on Commit).
    private sealed class ReplacementRow
    {
        public string? Replace { get; set; }
        public string? With { get; set; }
    }

    private static string SystemLanguageLabel()
    {
        var name = CultureInfo.CurrentCulture.Name;
        return string.IsNullOrEmpty(name) ? "invariant" : name;
    }

    private static void ConfigureToggle(CheckBox box, OptionsDialogToggleSpec spec)
    {
        box.Content = spec.Label;
        box.IsChecked = spec.IsChecked;
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
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

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
