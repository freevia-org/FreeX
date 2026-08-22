using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed partial class StyleDialog : FreeWDialogWindow
{
    private static readonly StyleDialogSurfaceSpec Surface = StyleDialogPlanner.Surface;
    private static AvaloniaCompactDialogChromeStyle DialogChromeStyle =>
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            // These are the measured WPF metrics for this dialog. Keep the correction local;
            // other dialogs retain their own shared density contracts.
            ControlHeight = StyleDialogMetrics.ComboBoxHeight,
            ButtonHeight = StyleDialogMetrics.ButtonHeight,
            ButtonPadding = new Thickness(10, 1),
            ComboBoxBackgroundBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new global::Avalonia.Media.GradientStop(Color.FromRgb(240, 240, 240), 0),
                    new global::Avalonia.Media.GradientStop(Color.FromRgb(229, 229, 229), 1),
                },
            },
            InputBorderBrush = new SolidColorBrush(Color.FromRgb(172, 172, 172)),
            ButtonBorderBrush = new SolidColorBrush(Color.FromRgb(112, 112, 112)),
        };

    private readonly TextBox _name = new() { MinWidth = Surface.Field(StyleDialogFieldKind.Name).MinWidth };
    private readonly ComboBox _basedOn = new() { MinWidth = Surface.Field(StyleDialogFieldKind.BasedOn).MinWidth };
    private readonly ComboBox _nextStyle = new() { MinWidth = Surface.Field(StyleDialogFieldKind.NextStyle).MinWidth };
    private readonly CheckBox _bold = Check(StyleDialogEffectKind.Bold);
    private readonly CheckBox _italic = Check(StyleDialogEffectKind.Italic);
    private readonly CheckBox _underline = Check(StyleDialogEffectKind.Underline);
    private readonly ComboBox _size = new() { MinWidth = Surface.Field(StyleDialogFieldKind.FontSize).MinWidth, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _color = new() { MinWidth = Surface.Field(StyleDialogFieldKind.TextColor).MinWidth, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _alignment = new() { MinWidth = Surface.Field(StyleDialogFieldKind.Alignment).MinWidth, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StyleDialogSession _session;

    private StyleDialog(StyleDialogSession session)
        : base(DialogChromeStyle)
    {
        _session = session;
        var state = _session.InitialState;

        Title = state.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _name.Text = state.Name;
        _name.IsReadOnly = state.NameIsReadOnly;
        _basedOn.ItemsSource = state.BasedOnOptions;
        _basedOn.SelectedIndex = state.BasedOnIndex;
        _nextStyle.ItemsSource = state.NextStyleOptions;
        _nextStyle.SelectedIndex = state.NextStyleIndex;
        foreach (var spec in Surface.Effects)
        {
            var checkBox = EffectControlFor(spec.Kind);
            checkBox.IsChecked = state.EffectValue(spec.Kind);
            AutomationProperties.SetAutomationId(checkBox, spec.AutomationId);
        }
        _size.ItemsSource = StyleDialogPlanner.FontSizes;
        _size.SelectedIndex = state.FontSizeIndex;
        _color.ItemsSource = StyleDialogPlanner.Colors;
        _color.SelectedIndex = state.ColorIndex;
        _alignment.ItemsSource = StyleDialogPlanner.AlignmentLabels.ToArray();
        _alignment.SelectedIndex = state.AlignmentIndex;
        ApplyCompactChrome();

        var effects = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var spec in Surface.Effects)
            effects.Children.Add(EffectControlFor(spec.Kind));

        var fields = new Dictionary<StyleDialogFieldKind, Control>
        {
            [StyleDialogFieldKind.Name] = _name,
            [StyleDialogFieldKind.BasedOn] = _basedOn,
            [StyleDialogFieldKind.NextStyle] = _nextStyle,
            [StyleDialogFieldKind.Formatting] = effects,
            [StyleDialogFieldKind.FontSize] = _size,
            [StyleDialogFieldKind.TextColor] = _color,
            [StyleDialogFieldKind.Alignment] = _alignment,
        };
        foreach (var spec in Surface.Fields)
            AutomationProperties.SetAutomationId(fields[spec.Kind], spec.AutomationId);

        var panel = new StackPanel { Margin = new Thickness(StyleDialogMetrics.DialogMargin) };
        foreach (var spec in Surface.Fields)
            AddRow(panel, spec.Label, fields[spec.Kind]);

        var actionRow = AvaloniaCompactDialogChrome.CreateOkCancelRow(
            () => _ = AcceptAsync(),
            () => Close(null),
            buttonWidth: Surface.ActionButtonWidth,
            margin: new Thickness(0, StyleDialogMetrics.ActionRowTopMargin, -1, 0),
            style: DialogChromeStyle);
        panel.Children.Add(actionRow);
        Content = panel;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
        Opened += (_, _) =>
        {
            ApplyCompactChrome();
            foreach (var button in actionRow.Children.OfType<Button>())
                AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, Surface.ActionButtonWidth, button.IsDefault);
            if (state.InitialFocus == StyleDialogFocusTarget.BasedOn)
                _basedOn.Focus(NavigationMethod.Tab);
            else
                _name.Focus(NavigationMethod.Tab);
        };
    }

    public static Task<StyleDefinitionResult?> AskNewAsync(
        Window owner,
        TextDocument document,
        string? defaultBasedOnId) =>
        new StyleDialog(StyleDialogPlanner.CreateNewSession(document, defaultBasedOnId))
            .ShowDialog<StyleDefinitionResult?>(owner);

    public static Task<StyleDefinitionResult?> AskModifyAsync(
        Window owner,
        TextDocument document,
        DocumentStyle existing) =>
        new StyleDialog(StyleDialogPlanner.CreateModifySession(document, existing))
            .ShowDialog<StyleDefinitionResult?>(owner);

    public static async Task ShowNewAndApplyAsync(Window owner, DocumentView editor)
    {
        var definition = await AskNewAsync(owner, editor.Document, editor.CurrentParagraphStyleId);
        if (definition is null)
            return;

        editor.CreateParagraphStyleAndApply(
            definition.Name,
            definition.BasedOnId,
            definition.Run,
            definition.Paragraph,
            definition.NextStyleId);
        editor.Focus();
    }

    private async Task AcceptAsync()
    {
        var acceptance = _session.PlanAcceptance(StyleDialogPlanner.CaptureControlState(
            _name.Text,
            _basedOn.SelectedIndex,
            _nextStyle.SelectedIndex,
            _size.SelectedIndex,
            _color.SelectedIndex,
            _alignment.SelectedIndex,
            kind => EffectControlFor(kind).IsChecked == true));

        if (!acceptance.IsAccepted)
        {
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                acceptance.ErrorMessage ?? string.Empty,
                _session.ValidationTitle);
            if (acceptance.FocusField == StyleDialogField.Name)
                _name.Focus(NavigationMethod.Tab);
            return;
        }

        Close(acceptance.Result);
    }

    private static void AddRow(Panel panel, string label, Control field)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brushes.Black,
            FontFamily = DialogChromeStyle.FontFamily,
            FontSize = DialogChromeStyle.FontSize,
            MinHeight = StyleDialogMetrics.LabelHeight,
            Margin = new Thickness(0, 0, 0, 2),
        });
        field.Margin = new Thickness(0, 0, 0, StyleDialogMetrics.AvaloniaFieldBottomMargin);
        panel.Children.Add(field);
    }

    private void ApplyCompactChrome()
    {
        AvaloniaCompactDialogChrome.ApplyTextBox(_name, DialogChromeStyle);
        _name.Foreground = Brushes.Black;
        _name.Height = StyleDialogMetrics.NameTextBoxHeight;
        _name.MinHeight = StyleDialogMetrics.NameTextBoxHeight;
        _name.MaxHeight = StyleDialogMetrics.NameTextBoxHeight;
        AvaloniaCompactDialogChrome.ApplyComboBox(_basedOn, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_nextStyle, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_size, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_color, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_alignment, DialogChromeStyle);
        foreach (var comboBox in new[] { _basedOn, _nextStyle, _size, _color, _alignment })
            comboBox.Foreground = Brushes.Black;
        foreach (var spec in Surface.Effects)
        {
            var checkBox = EffectControlFor(spec.Kind);
            AvaloniaCompactDialogChrome.ApplyCompactCheckBox(checkBox, DialogChromeStyle);
            checkBox.Height = StyleDialogMetrics.CheckBoxHeight;
            checkBox.MinHeight = StyleDialogMetrics.CheckBoxHeight;
            checkBox.MaxHeight = StyleDialogMetrics.CheckBoxHeight;
            checkBox.Foreground = Brushes.Black;
        }
    }

    private static CheckBox Check(StyleDialogEffectKind kind) => new()
    {
        Content = Surface.Effect(kind).Label,
        Margin = new Thickness(0, 0, kind == StyleDialogEffectKind.Underline ? 0 : 12, 0),
    };

    private CheckBox EffectControlFor(StyleDialogEffectKind kind) => kind switch
    {
        StyleDialogEffectKind.Bold => _bold,
        StyleDialogEffectKind.Italic => _italic,
        StyleDialogEffectKind.Underline => _underline,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

internal sealed class ManageStylesDialog : FreeWDialogWindow
{
    private static readonly ManageStyleSurfaceSpec Surface = StyleDialogPlanner.Surface.Manage;
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            ControlHeight = 22,
            ButtonHeight = 21,
            ButtonPadding = new Thickness(10, 1),
            ListBoxItemMinHeight = 21,
            ListBoxItemPadding = new Thickness(4, 0),
        };

    private readonly ManageStylesDialogSession _session;
    private readonly ListBox _styles = new()
    {
        MinHeight = Surface.Field(ManageStyleFieldKind.Styles).MinHeight,
        MinWidth = Surface.Field(ManageStyleFieldKind.Styles).MinWidth,
    };
    private readonly ComboBox _sortOrder = new() { MinWidth = Surface.Field(ManageStyleFieldKind.Sort).MinWidth };
    private readonly Button _apply;
    private readonly Button _modify;
    private readonly Button _delete;

    private ManageStylesDialog(TextDocument document, string? preselectStyleId)
    {
        _session = StyleDialogPlanner.CreateManageStylesSession(document, preselectStyleId);
        Title = Surface.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _sortOrder.ItemsSource = StyleDialogPlanner.ManageStyleSortLabels;
        _sortOrder.SelectedIndex = _session.State.SortIndex;
        AutomationProperties.SetAutomationId(_sortOrder, Surface.Field(ManageStyleFieldKind.Sort).AutomationId);
        AutomationProperties.SetAutomationId(_styles, Surface.Field(ManageStyleFieldKind.Styles).AutomationId);
        _sortOrder.SelectionChanged += (_, _) => RebuildList(_sortOrder.SelectedIndex);
        _styles.SelectionChanged += (_, _) => SyncButtons();

        _sortOrder.Margin = new Thickness(0, 0, 0, 8);
        var sortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        sortRow.Children.Add(new TextBlock
        {
            Text = Surface.Field(ManageStyleFieldKind.Sort).Label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        sortRow.Children.Add(_sortOrder);

        var listPane = new StackPanel();
        listPane.Children.Add(sortRow);
        listPane.Children.Add(_styles);

        _apply = Button(Surface.Action(ManageStyleCommandKind.Apply), (_, _) =>
        {
            if (_session.PlanAction(ManageStyleActionKind.Apply, _styles.SelectedIndex) is { } action)
                Close(action);
        });
        _apply.IsDefault = true;
        _modify = Button(Surface.Action(ManageStyleCommandKind.Modify), (_, _) =>
        {
            if (_session.PlanAction(ManageStyleActionKind.Modify, _styles.SelectedIndex) is { } action)
                Close(action);
        });
        _delete = Button(Surface.Action(ManageStyleCommandKind.Delete), (_, _) =>
        {
            if (_session.PlanAction(ManageStyleActionKind.Delete, _styles.SelectedIndex) is { } action)
                Close(action);
        });
        var close = Button(Surface.Action(ManageStyleCommandKind.Close), (_, _) => Close(null));

        var buttonPane = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12, 0, 0, 0) };
        buttonPane.Children.Add(_apply);
        buttonPane.Children.Add(_modify);
        buttonPane.Children.Add(_delete);
        buttonPane.Children.Add(close);

        var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16) };
        body.Children.Add(listPane);
        body.Children.Add(buttonPane);
        Content = body;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
        Opened += (_, _) =>
        {
            AvaloniaCompactDialogChrome.ApplyComboBox(_sortOrder, DialogChromeStyle);
            AvaloniaCompactDialogChrome.ApplyListBox(_styles, DialogChromeStyle);
            foreach (var button in new[] { _apply, _modify, _delete, close })
                AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, Surface.ActionButtonWidth, button.IsDefault);
            _styles.Focus(NavigationMethod.Tab);
        };

        RebuildList(_sortOrder.SelectedIndex);
    }

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        while (true)
        {
            var action = await new ManageStylesDialog(editor.Document, editor.CurrentParagraphStyleId)
                .ShowDialog<ManageStyleAction?>(owner);
            if (action is null)
                return;

            switch (action)
            {
                case ManageStyleAction.Apply apply:
                    editor.ApplyNamedStyle(apply.StyleId);
                    editor.Focus();
                    return;

                case ManageStyleAction.Delete delete:
                    editor.DeleteParagraphStyle(delete.StyleId);
                    continue;

                case ManageStyleAction.Modify modify:
                    if (!editor.Document.Styles.TryGetValue(modify.StyleId, out var existing))
                        continue;
                    var definition = await StyleDialog.AskModifyAsync(owner, editor.Document, existing);
                    if (definition is null)
                        continue;
                    editor.ModifyParagraphStyle(
                        modify.StyleId,
                        definition.Run,
                        definition.Paragraph,
                        definition.BasedOnId,
                        definition.NextStyleId);
                    continue;
            }
        }
    }

    internal static IReadOnlyList<StyleDialogRow> BuildRows(TextDocument document, StyleDialogSortOrder order) =>
        StyleDialogPlanner.BuildRows(document, order);

    private void RebuildList(int sortIndex)
    {
        var state = _session.PlanSort(sortIndex);
        _styles.ItemsSource = state.Rows.Select(row => row.Display).ToArray();
        _styles.SelectedIndex = state.SelectedIndex;
        ApplyButtonState(state.Buttons);
    }

    private void SyncButtons()
    {
        ApplyButtonState(_session.SelectRow(_styles.SelectedIndex).Buttons);
    }

    private void ApplyButtonState(ManageStyleButtonState buttons)
    {
        _apply.IsEnabled = buttons.ApplyEnabled;
        _modify.IsEnabled = buttons.IsEnabled(ManageStyleCommandKind.Modify);
        _delete.IsEnabled = buttons.IsEnabled(ManageStyleCommandKind.Delete);
    }

    private static Button Button(ManageStyleActionSpec spec, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button
        {
            Content = spec.Label,
            IsDefault = spec.IsDefault,
            IsCancel = spec.IsCancel,
            Margin = new Thickness(0, 0, 0, spec.Kind == ManageStyleCommandKind.Close ? 0 : 8),
        };
        AutomationProperties.SetAutomationId(button, spec.AutomationId);
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: Surface.ActionButtonWidth);
        button.Click += click;
        return button;
    }
}
