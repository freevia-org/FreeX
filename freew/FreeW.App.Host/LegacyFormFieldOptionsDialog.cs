using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's Text / Check Box / Drop-Down Form Field Options dialog. A thin WPF surface over
/// <see cref="LegacyFormFieldOptionsModel"/>, which owns the state, the validation and the resulting field.
/// </summary>
internal sealed class LegacyFormFieldOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly LegacyFormFieldOptionsModel _model;
    private readonly StackPanel _panel = new() { Margin = new Thickness(14) };
    private readonly List<Action> _commit = [];
    private LegacyFormField? _result;

    private LegacyFormFieldOptionsDialog(Window? owner, LegacyFormField field)
    {
        _model = new LegacyFormFieldOptionsModel(field);
        Owner = owner;
        Title = _model.Title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        switch (_model.Kind)
        {
            case LegacyFormFieldKind.CheckBox:
                BuildCheckBoxSection();
                break;
            case LegacyFormFieldKind.DropDown:
                BuildDropDownSection();
                break;
            default:
                BuildTextSection();
                break;
        }

        BuildCommonSection();
        _panel.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 80, rowMargin: new Thickness(0, 12, 0, 0)));
        Content = _panel;
    }

    /// <summary>Shows the dialog; returns the edited field on OK, or null on Cancel.</summary>
    public static LegacyFormField? Prompt(Window? owner, LegacyFormField field)
    {
        var dialog = new LegacyFormFieldOptionsDialog(owner, field);
        dialog.ShowDialog();
        return dialog._result;
    }

    private void BuildTextSection()
    {
        var type = new ComboBox { MinWidth = 200 };
        foreach (var textType in LegacyFormFieldOptionsModel.TextTypes)
            type.Items.Add(new ComboBoxItem { Content = LegacyFormFieldOptionsModel.TextTypeLabel(textType), Tag = textType });
        type.SelectedIndex = Math.Max(0, LegacyFormFieldOptionsModel.TextTypes.ToList().IndexOf(_model.TextType));

        var defaultText = new TextBox { Text = _model.DefaultText };
        var maxLength = new TextBox { Text = _model.MaxLengthText };
        var format = new ComboBox { IsEditable = true, Text = _model.TextFormat };

        void RefreshFormats()
        {
            var current = format.Text;
            format.Items.Clear();
            foreach (var suggestion in _model.TextFormatSuggestions)
                format.Items.Add(suggestion);
            format.Text = current;
        }

        type.SelectionChanged += (_, _) =>
        {
            if (type.SelectedItem is ComboBoxItem { Tag: LegacyFormTextType selected })
                _model.TextType = selected;
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
        var auto = new RadioButton { Content = LegacyFormFieldOptionsModel.Text("Options_SizeAuto"), IsChecked = _model.CheckBoxAutoSize, GroupName = "size" };
        var exactly = new RadioButton { Content = LegacyFormFieldOptionsModel.Text("Options_SizeExactly"), IsChecked = !_model.CheckBoxAutoSize, GroupName = "size" };
        var size = new TextBox { Text = _model.CheckBoxSizeText, MinWidth = 60, Margin = new Thickness(8, 0, 0, 0) };
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        sizeRow.Children.Add(exactly);
        sizeRow.Children.Add(size);
        _panel.Children.Add(auto);
        _panel.Children.Add(sizeRow);

        var defaultChecked = new CheckBox { Content = LegacyFormFieldOptionsModel.Text("Options_DefaultChecked"), IsChecked = _model.DefaultChecked, Margin = new Thickness(0, 4, 0, 4) };
        _panel.Children.Add(defaultChecked);
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
        var items = new ListBox { Height = 120 };

        void Refresh(int select)
        {
            items.Items.Clear();
            foreach (var entry in _model.Items)
                items.Items.Add(entry);
            items.SelectedIndex = Math.Min(select, items.Items.Count - 1);
        }

        var add = Button("Options_Add", () =>
        {
            if (_model.TryAddItem(item.Text))
            {
                item.Clear();
                Refresh(_model.Items.Count - 1);
            }
        });
        var remove = Button("Options_Remove", () =>
        {
            var index = items.SelectedIndex;
            if (index < 0)
                return;
            item.Text = _model.Items[index];
            _model.Items.RemoveAt(index);
            Refresh(index);
        });
        var up = Button("Options_MoveUp", () =>
        {
            var index = items.SelectedIndex;
            if (_model.MoveItem(index, -1))
                Refresh(index - 1);
        });
        var down = Button("Options_MoveDown", () =>
        {
            var index = items.SelectedIndex;
            if (_model.MoveItem(index, 1))
                Refresh(index + 1);
        });

        AddRow("Options_ItemText", item);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        buttons.Children.Add(up);
        buttons.Children.Add(down);
        _panel.Children.Add(buttons);
        AddRow("Options_Items", items);
        Refresh(0);
    }

    private void BuildCommonSection()
    {
        var entry = new TextBox { Text = _model.EntryMacro };
        var exit = new TextBox { Text = _model.ExitMacro };
        var bookmark = new TextBox { Text = _model.Name, MaxLength = LegacyFormField.MaxNameLength };
        var enabled = new CheckBox { Content = LegacyFormFieldOptionsModel.Text("Options_Enabled"), IsChecked = _model.Enabled, Margin = new Thickness(0, 4, 0, 0) };
        var calculate = new CheckBox { Content = LegacyFormFieldOptionsModel.Text("Options_CalculateOnExit"), IsChecked = _model.CalculateOnExit, Margin = new Thickness(0, 4, 0, 0) };
        var status = new TextBox { Text = _model.StatusText };
        var help = new TextBox { Text = _model.HelpText };

        AddRow("Options_EntryMacro", entry);
        AddRow("Options_ExitMacro", exit);
        AddRow("Options_Bookmark", bookmark);
        _panel.Children.Add(enabled);
        _panel.Children.Add(calculate);
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

    private void AddRow(string labelKey, FrameworkElement editor)
    {
        var label = new Label { Content = LegacyFormFieldOptionsModel.Text(labelKey), Target = editor, Padding = new Thickness(0, 6, 0, 2) };
        System.Windows.Automation.AutomationProperties.SetName(editor, LegacyFormFieldOptionsModel.Text(labelKey).TrimEnd(':'));
        _panel.Children.Add(label);
        _panel.Children.Add(editor);
    }

    private static Button Button(string labelKey, Action click)
    {
        var button = new Button { Content = LegacyFormFieldOptionsModel.Text(labelKey), MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    private void Accept()
    {
        foreach (var commit in _commit)
            commit();

        if (!_model.TryBuild(out var field, out var error))
        {
            MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _result = field;
        DialogResult = true;
    }
}
