using Avalonia;
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

internal sealed class StyleDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
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

    private readonly TextBox _name = new() { MinWidth = 280 };
    private readonly ComboBox _basedOn = new() { MinWidth = 280 };
    private readonly ComboBox _nextStyle = new() { MinWidth = 280 };
    private readonly CheckBox _bold = new() { Content = "Bold", Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox _italic = new() { Content = "Italic", Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox _underline = new() { Content = "Underline" };
    private readonly ComboBox _size = new() { MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _color = new() { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _alignment = new() { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StyleDialogSession _session;

    internal static double ControlHeightForTests => DialogChromeStyle.ControlHeight;
    internal static double ButtonHeightForTests => DialogChromeStyle.ButtonHeight;
    internal static double CheckBoxHeightForTests => StyleDialogMetrics.CheckBoxHeight;

    private StyleDialog(
        string title,
        IReadOnlyDictionary<string, string> styleNamesById,
        string? fixedName,
        string? defaultBasedOnId,
        RunFormatting seedRun,
        ParagraphFormatting seedParagraph,
        string? defaultNextStyleId)
    {
        _session = StyleDialogPlanner.CreateSession(
            title,
            styleNamesById,
            fixedName,
            defaultBasedOnId,
            seedRun,
            seedParagraph,
            defaultNextStyleId);
        var state = _session.InitialState;

        Title = state.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _name.Text = state.Name;
        _name.IsReadOnly = state.NameIsReadOnly;
        _basedOn.ItemsSource = state.BasedOnOptions.Select(e => e.Key).ToArray();
        _basedOn.SelectedIndex = state.BasedOnIndex;
        _nextStyle.ItemsSource = state.NextStyleOptions.Select(e => e.Key).ToArray();
        _nextStyle.SelectedIndex = state.NextStyleIndex;
        _bold.IsChecked = state.Bold;
        _italic.IsChecked = state.Italic;
        _underline.IsChecked = state.Underline;
        _size.ItemsSource = StyleDialogPlanner.FontSizes.Select(s => s.Label).ToArray();
        _size.SelectedIndex = state.FontSizeIndex;
        _color.ItemsSource = StyleDialogPlanner.Colors.Select(c => c.Label).ToArray();
        _color.SelectedIndex = state.ColorIndex;
        _alignment.ItemsSource = StyleDialogPlanner.AlignmentLabels.ToArray();
        _alignment.SelectedIndex = state.AlignmentIndex;
        ApplyCompactChrome();

        var effects = new StackPanel { Orientation = Orientation.Horizontal };
        effects.Children.Add(_bold);
        effects.Children.Add(_italic);
        effects.Children.Add(_underline);

        var panel = new StackPanel { Margin = new Thickness(StyleDialogMetrics.DialogMargin) };
        AddRow(panel, "Name:", _name);
        AddRow(panel, "Style based on:", _basedOn);
        AddRow(panel, "Style for following paragraph:", _nextStyle);
        AddRow(panel, "Formatting:", effects);
        AddRow(panel, "Font size:", _size);
        AddRow(panel, "Text colour:", _color);
        AddRow(panel, "Alignment:", _alignment);

        var actionRow = AvaloniaCompactDialogChrome.CreateOkCancelRow(
            () => _ = AcceptAsync(),
            () => Close(null),
            buttonWidth: 72,
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
                AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, 72, button.IsDefault);
            if (!state.NameIsReadOnly)
                _name.Focus(NavigationMethod.Tab);
            else
                _basedOn.Focus(NavigationMethod.Tab);
        };
    }

    public static Task<StyleDefinitionResult?> AskNewAsync(
        Window owner,
        IReadOnlyDictionary<string, string> styleNamesById,
        string? defaultBasedOnId) =>
        new StyleDialog("New Style", styleNamesById, fixedName: null, defaultBasedOnId,
            RunFormatting.Default, ParagraphFormatting.Default, defaultNextStyleId: null)
            .ShowDialog<StyleDefinitionResult?>(owner);

    public static Task<StyleDefinitionResult?> AskModifyAsync(
        Window owner,
        IReadOnlyDictionary<string, string> styleNamesById,
        DocumentStyle existing) =>
        new StyleDialog($"Modify Style \u2014 {existing.Name}", styleNamesById, fixedName: existing.Name,
            existing.BasedOnStyleId, existing.Run, existing.Paragraph, existing.NextStyleId)
            .ShowDialog<StyleDefinitionResult?>(owner);

    public static async Task ShowNewAndApplyAsync(Window owner, DocumentView editor)
    {
        var definition = await AskNewAsync(owner, StyleNamesById(editor.Document), editor.CurrentParagraphStyleId);
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

    internal static IReadOnlyDictionary<string, string> StyleNamesById(TextDocument document) =>
        StyleDialogPlanner.BuildStyleNamesById(document);

    private async Task AcceptAsync()
    {
        var acceptance = _session.PlanAcceptance(new StyleDialogControlState(
            _name.Text,
            _basedOn.SelectedIndex,
            _nextStyle.SelectedIndex,
            _bold.IsChecked == true,
            _italic.IsChecked == true,
            _underline.IsChecked == true,
            _size.SelectedIndex,
            _color.SelectedIndex,
            _alignment.SelectedIndex));

        if (!acceptance.IsAccepted)
        {
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                acceptance.ErrorMessage ?? string.Empty,
                StyleDialogSession.ValidationTitle);
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
            Margin = new Thickness(0, 0, 0, 2),
        });
        field.Margin = new Thickness(0, 0, 0, StyleDialogMetrics.FieldBottomMargin);
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
        foreach (var checkBox in new[] { _bold, _italic, _underline })
        {
            AvaloniaCompactDialogChrome.ApplyCompactCheckBox(checkBox, DialogChromeStyle);
            checkBox.Height = StyleDialogMetrics.CheckBoxHeight;
            checkBox.MinHeight = StyleDialogMetrics.CheckBoxHeight;
            checkBox.MaxHeight = StyleDialogMetrics.CheckBoxHeight;
            checkBox.Foreground = Brushes.Black;
        }
    }
}

internal sealed class ManageStylesDialog : FreeWDialogWindow
{
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
    private readonly ListBox _styles = new() { MinHeight = 220, MinWidth = 320 };
    private readonly ComboBox _sortOrder = new() { MinWidth = 160 };
    private readonly Button _apply;
    private readonly Button _modify;
    private readonly Button _delete;

    private ManageStylesDialog(TextDocument document, string? preselectStyleId)
    {
        _session = StyleDialogPlanner.CreateManageStylesSession(document, preselectStyleId);
        Title = ManageStylesDialogSession.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _sortOrder.ItemsSource = StyleDialogPlanner.ManageStyleSortLabels;
        _sortOrder.SelectedIndex = 0;
        _sortOrder.SelectionChanged += (_, _) => RebuildList(_sortOrder.SelectedIndex);
        _styles.SelectionChanged += (_, _) => SyncButtons();

        _sortOrder.Margin = new Thickness(0, 0, 0, 8);
        var sortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        sortRow.Children.Add(new TextBlock
        {
            Text = "Sort:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        sortRow.Children.Add(_sortOrder);

        var listPane = new StackPanel();
        listPane.Children.Add(sortRow);
        listPane.Children.Add(_styles);

        _apply = Button("Apply", (_, _) =>
        {
            if (_session.PlanAction(ManageStyleActionKind.Apply, _styles.SelectedIndex) is { } action)
                Close(action);
        });
        _apply.IsDefault = true;
        _modify = Button("Modify\u2026", (_, _) =>
        {
            if (_session.PlanAction(ManageStyleActionKind.Modify, _styles.SelectedIndex) is { } action)
                Close(action);
        });
        _delete = Button("Delete", (_, _) =>
        {
            if (_session.PlanAction(ManageStyleActionKind.Delete, _styles.SelectedIndex) is { } action)
                Close(action);
        });
        var close = Button("Close", (_, _) => Close(null));
        close.IsCancel = true;

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
                AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, 80, button.IsDefault);
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
                    var definition = await StyleDialog.AskModifyAsync(owner, StyleDialog.StyleNamesById(editor.Document), existing);
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
        _modify.IsEnabled = buttons.ModifyEnabled;
        _delete.IsEnabled = buttons.DeleteEnabled;
    }

    private static Button Button(string text, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 0, 8) };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80);
        button.Click += click;
        return button;
    }
}
