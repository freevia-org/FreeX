using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class LegacyFormFieldOptionsDialog : FreeWDialogWindow
{
    private readonly LegacyFormFieldOptionsModel _model;
    private readonly Grid _fields = new();
    private readonly TextBlock _validation = new();
    private readonly List<Action> _commit = [];
    private int _row;

    internal LegacyFormFieldOptionsDialog(LegacyFormField field)
    {
        _model = new LegacyFormFieldOptionsModel(field);
        Title = _model.Title;
        Width = 470;
        MaxHeight = 740;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, "LegacyFormFieldOptionsDialog");

        _fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
        _fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (_model.Kind == LegacyFormFieldKind.CheckBox)
            BuildCheckBoxSection();
        else if (_model.Kind == LegacyFormFieldKind.DropDown)
            BuildDropDownSection();
        else
            BuildTextSection();
        BuildCommonSection();

        _validation.Margin = new Thickness(14, 4, 14, 0);
        _validation.TextWrapping = TextWrapping.Wrap;
        _validation.IsVisible = false;
        var ok = InsertDialogLayout.MakeButton(InsertDialogTextResources.OkButton, (_, _) => Accept());
        ok.IsDefault = true;
        AutomationProperties.SetAutomationId(ok, "LegacyFormFieldOptionsAccept");
        var cancel = InsertDialogLayout.MakeButton(InsertDialogTextResources.CancelButton, (_, _) => Close());
        cancel.IsCancel = true;
        var actions = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(14, 8, 14, 12));

        Content = new StackPanel
        {
            Children =
            {
                new ScrollViewer
                {
                    MaxHeight = 620,
                    Content = _fields,
                    Margin = new Thickness(14, 12, 14, 0),
                },
                _validation,
                actions,
            },
        };

        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;
            Close();
            args.Handled = true;
        };
    }

    public static Task<LegacyFormField?> ShowAsync(Window owner, LegacyFormField field) =>
        new LegacyFormFieldOptionsDialog(field).ShowDialog<LegacyFormField?>(owner);

    private void BuildTextSection()
    {
        var type = new ComboBox
        {
            ItemsSource = LegacyFormFieldOptionsModel.TextTypes.Select(LegacyFormFieldOptionsModel.TextTypeLabel).ToArray(),
            SelectedIndex = Math.Max(0, LegacyFormFieldOptionsModel.TextTypes.ToList().IndexOf(_model.TextType)),
            MinWidth = 190,
        };
        var defaultText = new TextBox { Text = _model.DefaultText };
        var maxLength = new TextBox { Text = _model.MaxLengthText };
        var format = new ComboBox { IsEditable = true, Text = _model.TextFormat, MinWidth = 190 };

        void RefreshFormats()
        {
            var current = format.Text;
            format.ItemsSource = _model.TextFormatSuggestions.ToArray();
            format.Text = current;
        }

        type.SelectionChanged += (_, _) =>
        {
            if (type.SelectedIndex >= 0 && type.SelectedIndex < LegacyFormFieldOptionsModel.TextTypes.Count)
                _model.TextType = LegacyFormFieldOptionsModel.TextTypes[type.SelectedIndex];
            RefreshFormats();
        };
        RefreshFormats();

        AddRow("Options_Type", type);
        AddRow("Options_DefaultText", defaultText);
        AddRow("Options_MaxLength", maxLength);
        AddRow("Options_TextFormat", format);
        _commit.Add(() =>
        {
            _model.DefaultText = defaultText.Text;
            _model.MaxLengthText = maxLength.Text;
            _model.TextFormat = format.Text;
        });
    }

    private void BuildCheckBoxSection()
    {
        var auto = new RadioButton
        {
            Content = LegacyFormFieldOptionsModel.Text("Options_SizeAuto"),
            IsChecked = _model.CheckBoxAutoSize,
            GroupName = "LegacyFormFieldSize",
        };
        var exact = new RadioButton
        {
            Content = LegacyFormFieldOptionsModel.Text("Options_SizeExactly"),
            IsChecked = !_model.CheckBoxAutoSize,
            GroupName = "LegacyFormFieldSize",
        };
        var size = new TextBox { Text = _model.CheckBoxSizeText, Width = 72 };
        AddFullRow(auto);
        AddFullRow(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { exact, size },
        });
        var defaultChecked = new CheckBox
        {
            Content = LegacyFormFieldOptionsModel.Text("Options_DefaultChecked"),
            IsChecked = _model.DefaultChecked,
        };
        AddFullRow(defaultChecked);
        _commit.Add(() =>
        {
            _model.CheckBoxAutoSize = auto.IsChecked == true;
            _model.CheckBoxSizeText = size.Text;
            _model.DefaultChecked = defaultChecked.IsChecked == true;
        });
    }

    private void BuildDropDownSection()
    {
        var item = new TextBox();
        var items = new ListBox { Height = 120, MinWidth = 190 };

        void Refresh(int selectedIndex)
        {
            items.ItemsSource = _model.Items.ToArray();
            items.SelectedIndex = Math.Min(selectedIndex, _model.Items.Count - 1);
        }

        var add = MakeButton("Options_Add", () =>
        {
            if (!_model.TryAddItem(item.Text))
                return;
            item.Clear();
            Refresh(_model.Items.Count - 1);
        });
        var remove = MakeButton("Options_Remove", () =>
        {
            var index = items.SelectedIndex;
            if (index < 0)
                return;
            item.Text = _model.Items[index];
            _model.Items.RemoveAt(index);
            Refresh(index);
        });
        var up = MakeButton("Options_MoveUp", () =>
        {
            var index = items.SelectedIndex;
            if (_model.MoveItem(index, -1))
                Refresh(index - 1);
        });
        var down = MakeButton("Options_MoveDown", () =>
        {
            var index = items.SelectedIndex;
            if (_model.MoveItem(index, 1))
                Refresh(index + 1);
        });

        AddRow("Options_ItemText", item);
        var itemActions = AvaloniaCompactDialogChrome.CreateActionRow(
            [add, remove, up, down],
            new Thickness(0, 4, 0, 4),
            AvaloniaCompactDialogChrome.WindowsStyle with { ActionSpacing = 4 });
        AddFullRow(itemActions);
        AddRow("Options_Items", items);
        Refresh(0);
    }

    private void BuildCommonSection()
    {
        var entry = new TextBox { Text = _model.EntryMacro };
        var exit = new TextBox { Text = _model.ExitMacro };
        var bookmark = new TextBox { Text = _model.Name, MaxLength = LegacyFormField.MaxNameLength };
        var enabled = new CheckBox
        {
            Content = LegacyFormFieldOptionsModel.Text("Options_Enabled"),
            IsChecked = _model.Enabled,
        };
        var calculate = new CheckBox
        {
            Content = LegacyFormFieldOptionsModel.Text("Options_CalculateOnExit"),
            IsChecked = _model.CalculateOnExit,
        };
        var status = new TextBox { Text = _model.StatusText };
        var help = new TextBox { Text = _model.HelpText };

        AddRow("Options_EntryMacro", entry);
        AddRow("Options_ExitMacro", exit);
        AddRow("Options_Bookmark", bookmark);
        AddFullRow(enabled);
        AddFullRow(calculate);
        AddRow("Options_StatusText", status);
        AddRow("Options_HelpText", help);
        _commit.Add(() =>
        {
            _model.EntryMacro = entry.Text;
            _model.ExitMacro = exit.Text;
            _model.Name = bookmark.Text;
            _model.Enabled = enabled.IsChecked == true;
            _model.CalculateOnExit = calculate.IsChecked == true;
            _model.StatusText = status.Text;
            _model.HelpText = help.Text;
        });
    }

    private void AddRow(string labelKey, Control editor)
    {
        var label = LegacyFormFieldOptionsModel.Text(labelKey);
        var text = new TextBlock
        {
            Text = label,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 8, 5),
        };
        AutomationProperties.SetName(editor, label.TrimEnd(':'));
        ApplyControlChrome(editor);
        AddRowControls(text, editor, spanEditor: false);
    }

    private void AddFullRow(Control control)
    {
        ApplyControlChrome(control);
        AddRowControls(null, control, spanEditor: true);
    }

    private void AddRowControls(Control? label, Control editor, bool spanEditor)
    {
        _fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (label is not null)
        {
            Grid.SetRow(label, _row);
            Grid.SetColumn(label, 0);
            _fields.Children.Add(label);
        }

        Grid.SetRow(editor, _row);
        Grid.SetColumn(editor, label is null ? 0 : 1);
        if (spanEditor)
            Grid.SetColumnSpan(editor, 2);
        editor.Margin = new Thickness(0, 4, 0, 4);
        _fields.Children.Add(editor);
        _row++;
    }

    private static void ApplyControlChrome(Control control)
    {
        switch (control)
        {
            case TextBox textBox:
                AvaloniaCompactDialogChrome.ApplyTextBox(textBox, InsertDialogLayout.ChromeStyle);
                break;
            case ComboBox comboBox:
                AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, InsertDialogLayout.ChromeStyle);
                break;
            case CheckBox checkBox:
                AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, InsertDialogLayout.ChromeStyle);
                break;
            case RadioButton radioButton:
                AvaloniaCompactDialogChrome.ApplyRadioButton(radioButton, InsertDialogLayout.ChromeStyle);
                break;
            case Button button:
                AvaloniaCompactDialogChrome.ApplyButton(button, InsertDialogLayout.ChromeStyle, minWidth: 72);
                break;
        }
    }

    private static Button MakeButton(string labelKey, Action click)
    {
        var button = InsertDialogLayout.MakeButton(LegacyFormFieldOptionsModel.Text(labelKey), (_, _) => click());
        AvaloniaCompactDialogChrome.ApplyButton(button, InsertDialogLayout.ChromeStyle, minWidth: 72);
        return button;
    }

    private void Accept()
    {
        foreach (var commit in _commit)
            commit();

        if (!_model.TryBuild(out var field, out var error))
        {
            _validation.Text = error;
            _validation.IsVisible = true;
            return;
        }

        Close(field);
    }
}
