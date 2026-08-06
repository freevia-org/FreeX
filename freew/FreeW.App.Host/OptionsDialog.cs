using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
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
    private readonly OptionsDialogSession _session;
    private readonly OptionsDialogSurfaceSpec _surface;

    private readonly TextBox _recentFilesCap = new() { MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _defaultFormat = new() { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _uiLanguage = new() { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };

    private readonly IReadOnlyDictionary<OptionsDialogToggleKind, CheckBox> _toggles;
    private readonly DataGrid _replacements = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = true,
        CanUserDeleteRows = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Height = OptionsDialogPlanner.ReplacementTableHeight,
        Margin = new Thickness(0, 6, 0, 0),
    };
    private readonly ObservableCollection<ReplacementRow> _replacementRows = new();

    /// <summary>The normalized options produced on OK; equals the input options on Cancel.</summary>
    public FreeWOptions Result { get; private set; }

    public OptionsDialog(Window owner, FreeWOptions options)
    {
        _session = new OptionsDialogSession(options, CultureInfo.CurrentCulture);
        _surface = _session.Surface;
        _toggles = Enum.GetValues<OptionsDialogToggleKind>()
            .ToDictionary(kind => kind, _ => new CheckBox());
        Result = _session.InitialResult;

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
        {
            var item = new ComboBoxItem { Content = choice.Label, Tag = choice.Extension };
            _defaultFormat.Items.Add(item);
            if (choice.Extension == _session.InitialState.SelectedFormat)
                _defaultFormat.SelectedItem = item;
        }
        if (_defaultFormat.SelectedIndex < 0)
            _defaultFormat.SelectedIndex = 0;

        _recentFilesCap.Text = _session.InitialState.RecentFilesCapText;
        _uiLanguage.Text = _session.InitialState.UiLanguage;

        var tabs = new TabControl { Margin = new Thickness(OptionsDialogPlanner.TabMargin, OptionsDialogPlanner.TabMargin, OptionsDialogPlanner.TabMargin, 0) };
        var tabContents = new[] { BuildGeneralTab(), BuildAutoCorrectTab(), BuildAutoFormatTab() };
        for (var index = 0; index < _surface.Tabs.Count; index++)
        {
            var tab = new TabItem { Header = _surface.Tabs[index].Header, Content = tabContents[index] };
            AutomationProperties.SetAutomationId(tab, _surface.Tabs[index].AutomationId);
            tabs.Items.Add(tab);
        }

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
        for (var i = 0; i < _surface.General.Fields.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var index = 0; index < _surface.General.Fields.Count; index++)
        {
            var field = _surface.General.Fields[index];
            AddRow(grid, index, field.Label, GeneralControlFor(field.Kind), field.Hint);
        }
        return grid;
    }

    private FrameworkElement BuildAutoFormatTab()
    {
        var masterToggle = ToggleFor(_surface.AutoFormat.MasterToggle.Kind);
        ConfigureToggle(masterToggle, _surface.AutoFormat.MasterToggle);
        foreach (var spec in _surface.AutoFormat.RuleToggles)
            ConfigureToggle(ToggleFor(spec.Kind), spec);

        var ruleBoxes = _surface.AutoFormat.RuleToggles
            .Select(spec => ToggleFor(spec.Kind))
            .ToArray();

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
            var enabledState = _session.PlanEnabledState(
                masterToggle.IsChecked == true,
                ToggleFor(OptionsDialogToggleKind.ReplaceText).IsChecked == true);
            foreach (var box in ruleBoxes)
                box.IsEnabled = enabledState.AutoFormatRulesEnabled;
        }
        masterToggle.Checked += (_, _) => SyncEnabled();
        masterToggle.Unchecked += (_, _) => SyncEnabled();

        var panel = new StackPanel
        {
            Margin = new Thickness(
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentBottomMargin)
        };
        masterToggle.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(masterToggle);
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

        foreach (var replacement in _session.InitialState.Replacements)
            _replacementRows.Add(new ReplacementRow { Replace = replacement.Replace, With = replacement.With });

        foreach (var column in _surface.AutoCorrect.ReplacementColumns)
        {
            _replacements.Columns.Add(new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new System.Windows.Data.Binding(ReplacementPropertyFor(column.Kind))
                {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                },
                Width = new DataGridLength(column.WidthWeight, DataGridLengthUnitType.Star),
            });
        }
        _replacements.ItemsSource = _replacementRows;
        _replacements.Loaded += (_, _) => TryApplyReplacementColumnWidths(_replacements);
        _replacements.SizeChanged += (_, _) => TryApplyReplacementColumnWidths(_replacements);
        _replacements.LayoutUpdated += RealizeReplacementColumnsAfterMeasure;

        var toggles = _surface.AutoCorrect.Toggles
            .Select(spec => ToggleFor(spec.Kind))
            .ToArray();
        var replaceTextToggle = ToggleFor(OptionsDialogToggleKind.ReplaceText);

        var panel = new Grid
        {
            Margin = new Thickness(
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentBottomMargin)
        };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        void AddPanelRow(FrameworkElement child)
        {
            var row = panel.RowDefinitions.Count;
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(child, row);
            Grid.SetColumn(child, 0);
            panel.Children.Add(child);
        }

        foreach (var box in toggles)
        {
            box.Margin = new Thickness(0, OptionsDialogPlanner.ToggleTopMargin, 0, 0);
            AddPanelRow(box);
        }
        AddPanelRow(new TextBlock
        {
            Text = _surface.AutoCorrect.ReplacementsLabel,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, OptionsDialogPlanner.SectionHeaderTopMargin, 0, 0),
        });
        AddPanelRow(_replacements);
        AddPanelRow(new TextBlock
        {
            Text = _surface.AutoCorrect.ReplacementsHelpText,
            FontSize = OptionsDialogPlanner.HelpTextFontSize,
            Foreground = System.Windows.Media.Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, OptionsDialogPlanner.ToggleTopMargin + 2, 0, 0),
        });

        // The replace table only applies when "Replace text as you type" is on; mirror that in the UI.
        void SyncTable() => _replacements.IsEnabled = _session.PlanEnabledState(
            ToggleFor(OptionsDialogToggleKind.AutoCorrectEnabled).IsChecked == true,
            replaceTextToggle.IsChecked == true).ReplacementsEnabled;
        replaceTextToggle.Checked += (_, _) => SyncTable();
        replaceTextToggle.Unchecked += (_, _) => SyncTable();
        SyncTable();

        return panel;
    }

    private void Commit()
    {
        // Commit any in-progress cell edit so the last-typed row is captured before we read the rows.
        _replacements.CommitEdit(DataGridEditingUnit.Row, true);

        var input = new OptionsDialogInput(
            _recentFilesCap.Text,
            (_defaultFormat.SelectedItem as ComboBoxItem)?.Tag as string,
            _uiLanguage.Text,
            CheckedToggles(),
            _replacementRows
                .Select(row => new OptionsDialogReplacementInput(row.Replace, row.With))
                .ToArray());
        var plan = _session.PlanAcceptance(input);
        if (!plan.ShouldApply)
        {
            DialogMessageHelper.ShowWarning(this, plan.Validation!.Message, Title);
            if (plan.Validation.Target == OptionsDialogValidationTarget.RecentFilesCap)
                DialogFocus.FocusAndSelect(_recentFilesCap);
            return;
        }

        Result = plan.Result!;
        DialogResult = true;
    }

    private OptionsDialogToggleKind[] CheckedToggles() =>
        _toggles.Where(entry => entry.Value.IsChecked == true).Select(entry => entry.Key).ToArray();

    // WPF can settle star columns at MinWidth while a DataGrid is measured inside a size-to-content dialog.
    // Realize the declared 1:2 weights against the current viewport once it is available, and again only
    // when the table is resized. The post-measure hook removes itself after the first successful pass.
    private void RealizeReplacementColumnsAfterMeasure(object? sender, EventArgs e)
    {
        if (TryApplyReplacementColumnWidths(_replacements))
            _replacements.LayoutUpdated -= RealizeReplacementColumnsAfterMeasure;
    }

    private static bool TryApplyReplacementColumnWidths(DataGrid table)
    {
        if (table.Columns.Count != 2 || table.ActualWidth <= 0)
            return false;

        var viewport = FindVisualChildren<ScrollViewer>(table).FirstOrDefault()?.ViewportWidth ?? table.ActualWidth;
        if (viewport <= 0)
            return false;

        var firstWidth = viewport / 3;
        var secondWidth = viewport * 2 / 3;
        if (Math.Abs(table.Columns[0].ActualWidth - firstWidth) > 0.5)
            table.Columns[0].Width = new DataGridLength(firstWidth, DataGridLengthUnitType.Pixel);
        if (Math.Abs(table.Columns[1].ActualWidth - secondWidth) > 0.5)
            table.Columns[1].Width = new DataGridLength(secondWidth, DataGridLengthUnitType.Pixel);
        return true;
    }

    // A mutable two-property row backing the AutoCorrect replace-table DataGrid (DataGrid edits need a
    // settable, public class; the immutable AutoCorrectReplacement record is built from these on Commit).
    private sealed class ReplacementRow
    {
        public string? Replace { get; set; }
        public string? With { get; set; }
    }

    private static void ConfigureToggle(CheckBox box, OptionsDialogToggleSpec spec)
    {
        box.Content = spec.Label;
        box.IsChecked = spec.IsChecked;
    }

    private CheckBox ToggleFor(OptionsDialogToggleKind kind) => _toggles[kind];

    private FrameworkElement GeneralControlFor(OptionsDialogGeneralFieldKind kind) =>
        kind switch
        {
            OptionsDialogGeneralFieldKind.RecentFilesCap => _recentFilesCap,
            OptionsDialogGeneralFieldKind.DefaultSaveFormat => _defaultFormat,
            OptionsDialogGeneralFieldKind.UiLanguage => _uiLanguage,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static string ReplacementPropertyFor(OptionsDialogReplacementFieldKind kind) =>
        kind == OptionsDialogReplacementFieldKind.Replace
            ? nameof(ReplacementRow.Replace)
            : nameof(ReplacementRow.With);

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

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value)
            yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in FindVisualChildren<T>(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }

}
