using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// A modal Sort dialog matching Word's "Sort Text" / "Sort" dialog. Supports up to three sort keys
/// (Sort by + Then by × 2), with per-key sort type (Text / Number / Date) and direction
/// (Ascending / Descending), plus global case-sensitive and header-row toggles. Built on the shared
/// <see cref="Free.Shared.Ribbon.Wpf.DialogWindow"/> + dialog helpers so it matches the rest of
/// FreeW/FreeX's dialogs. Returns the shared <see cref="SortDialogResult"/>, or null if cancelled.
/// </summary>
internal sealed class SortDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly SortDialogSession _session;
    // Per-key controls: type box and ascending radio.
    private readonly ComboBox   _type1;
    private readonly RadioButton _asc1;
    private readonly ComboBox   _type2;
    private readonly RadioButton _asc2;
    private readonly CheckBox   _useKey2;
    private readonly ComboBox   _type3;
    private readonly RadioButton _asc3;
    private readonly CheckBox   _useKey3;
    private readonly CheckBox   _caseSensitive;
    private readonly CheckBox   _hasHeaderRow;
    private SortDialogResult? _result;

    private SortDialog(Window? owner, bool forTable)
    {
        _session = new SortDialogSession(forTable);
        Owner = owner;
        Title = SortDialogPlanner.Title;
        Width = SortDialogVisualMetrics.WindowWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, SortDialogPlanner.AutomationId);

        _type1 = TypeCombo(_session.TypeChoices);
        _asc1  = AscRadio();
        var desc1 = DescRadio();

        _useKey2 = new CheckBox
        {
            Content = SortDialogPlanner.ThenByLabel,
            Margin = new Thickness(
                0,
                SortDialogVisualMetrics.OptionalKeyTopMargin,
                0,
                SortDialogVisualMetrics.OptionalKeyBottomMargin)
        };
        _type2 = TypeCombo(_session.TypeChoices);
        _asc2  = AscRadio();
        var desc2 = DescRadio();
        System.Windows.Automation.AutomationProperties.SetAutomationId(_type2, SortDialogPlanner.Key2TypeAutomationId);

        _useKey3 = new CheckBox
        {
            Content = SortDialogPlanner.ThenBySecondLabel,
            Margin = new Thickness(
                0,
                SortDialogVisualMetrics.OptionalKeyTopMargin,
                0,
                SortDialogVisualMetrics.OptionalKeyBottomMargin)
        };
        _type3 = TypeCombo(_session.TypeChoices);
        _asc3  = AscRadio();
        var desc3 = DescRadio();
        System.Windows.Automation.AutomationProperties.SetAutomationId(_type1, SortDialogPlanner.Key1TypeAutomationId);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_type3, SortDialogPlanner.Key3TypeAutomationId);

        _caseSensitive = new CheckBox
        {
            Content = SortDialogPlanner.CaseSensitiveLabel,
            Margin = new Thickness(
                0,
                SortDialogVisualMetrics.CaseSensitiveTopMargin,
                0,
                SortDialogVisualMetrics.CaseSensitiveBottomMargin)
        };
        _hasHeaderRow  = new CheckBox { Content = SortDialogPlanner.HeaderRowLabel, Margin = new Thickness(0, 0, 0, 0) };

        void ApplyEnabledState()
        {
            var state = _session.PlanEnabledState(_useKey2.IsChecked == true, _useKey3.IsChecked == true);
            SetKeyEnabled(_type2, _asc2, desc2, state.Key2Enabled);
            SetKeyEnabled(_type3, _asc3, desc3, state.Key3Enabled);
        }
        ApplyEnabledState();
        _useKey2.Checked += (_, _) => ApplyEnabledState();
        _useKey2.Unchecked += (_, _) => ApplyEnabledState();
        _useKey3.Checked += (_, _) => ApplyEnabledState();
        _useKey3.Unchecked += (_, _) => ApplyEnabledState();

        var panel = new StackPanel { Margin = new Thickness(SortDialogVisualMetrics.RootInset) };
        panel.Children.Add(new TextBlock
        {
            Text = _session.Prompt,
            Margin = new Thickness(0, 0, 0, SortDialogVisualMetrics.PromptBottomMargin)
        });

        // Key 1: Sort by
        panel.Children.Add(new TextBlock
        {
            Text = SortDialogPlanner.SortByLabel,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, SortDialogVisualMetrics.PrimaryHeadingBottomMargin)
        });
        panel.Children.Add(KeyRow(_type1));
        panel.Children.Add(_asc1);
        panel.Children.Add(desc1);

        // Key 2: Then by (optional)
        panel.Children.Add(_useKey2);
        panel.Children.Add(KeyRow(_type2));
        panel.Children.Add(_asc2);
        panel.Children.Add(desc2);

        // Key 3: Then by (2nd) (optional)
        panel.Children.Add(_useKey3);
        panel.Children.Add(KeyRow(_type3));
        panel.Children.Add(_asc3);
        panel.Children.Add(desc3);

        panel.Children.Add(_caseSensitive);
        panel.Children.Add(_hasHeaderRow);

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        panel.Children.Add(DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: SortDialogVisualMetrics.ActionButtonWidth,
            rowMargin: new Thickness(0, SortDialogVisualMetrics.ActionRowTopMargin, 0, 0)));

        Content = panel;
        Loaded += (_, _) => _type1.Focus();
    }

    private static ComboBox TypeCombo<TValue>(IReadOnlyList<SortDialogChoice<TValue>> choices)
    {
        var box = new ComboBox
        {
            MinWidth = SortDialogVisualMetrics.TypeMinimumWidth,
            Margin = new Thickness(0, 0, 0, SortDialogVisualMetrics.TypeControlBottomMargin)
        };
        foreach (var choice in choices)
            box.Items.Add(choice.Label);
        box.SelectedIndex = 0;
        return box;
    }

    private static RadioButton AscRadio() =>
        new()
        {
            Content = SortDialogPlanner.AscendingLabel,
            IsChecked = true,
            Margin = new Thickness(
                SortDialogVisualMetrics.RadioLeftMargin,
                0,
                SortDialogVisualMetrics.AscendingRightMargin,
                SortDialogVisualMetrics.RadioBottomMargin)
        };

    private static RadioButton DescRadio() =>
        new()
        {
            Content = SortDialogPlanner.DescendingLabel,
            Margin = new Thickness(
                SortDialogVisualMetrics.RadioLeftMargin,
                0,
                0,
                SortDialogVisualMetrics.RadioBottomMargin)
        };

    private static StackPanel KeyRow(ComboBox typeBox)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, SortDialogVisualMetrics.KeyRowBottomMargin)
        };
        row.Children.Add(new TextBlock
        {
            Text = SortDialogPlanner.TypeLabel,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, SortDialogVisualMetrics.TypeLabelTrailingMargin, 0)
        });
        row.Children.Add(typeBox);
        return row;
    }

    private static void SetKeyEnabled(ComboBox typeBox, RadioButton asc, RadioButton desc, bool enabled)
    {
        typeBox.IsEnabled = enabled;
        asc.IsEnabled     = enabled;
        desc.IsEnabled    = enabled;
    }

    private void Accept()
    {
        _result = _session.PlanAcceptance(new SortDialogInput(
            _type1.SelectedIndex,
            _asc1.IsChecked == true,
            _useKey2.IsChecked == true,
            _type2.SelectedIndex,
            _asc2.IsChecked == true,
            _useKey3.IsChecked == true,
            _type3.SelectedIndex,
            _asc3.IsChecked == true,
            _caseSensitive.IsChecked == true,
            _hasHeaderRow.IsChecked == true));
        Close();
    }

    /// <summary>
    /// Show the Sort dialog. <paramref name="forTable"/> tailors the prompt text (sorting table rows by
    /// the caret's column vs. sorting selected paragraphs). Returns the chosen options, or null if
    /// cancelled.
    /// </summary>
    public static SortDialogResult? Prompt(Window? owner, bool forTable)
    {
        var dialog = new SortDialog(owner, forTable);
        dialog.ShowDialog();
        return dialog._result;
    }
}
