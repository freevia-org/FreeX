using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Free.Shared.Shell;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host;

/// <summary>
/// Thin WPF field-picker adapter over <see cref="FieldPickerDialogPlanner"/>. Category/field policy
/// and raw instructions stay shared; this class owns only WPF selection and modal realization.
/// </summary>
internal sealed class FieldPickerDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ListBox _categories = new() { MinWidth = 160, MinHeight = 210 };
    private readonly ListBox _fields = new() { MinWidth = 250, MinHeight = 210 };

    private FieldPickerDialog()
    {
        Title = FieldPickerDialogPlanner.Title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;

        _categories.ItemsSource = FieldPickerDialogPlanner.Categories;
        _categories.SelectionChanged += (_, _) => RefreshFields();
        _categories.SelectedIndex = 0;
        _fields.MouseDoubleClick += (_, _) => Accept();

        var lists = new Grid();
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_categories, 0);
        Grid.SetColumn(_fields, 2);
        lists.Children.Add(_categories);
        lists.Children.Add(_fields);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = FieldPickerDialogPlanner.Prompt,
            Margin = new Thickness(0, 0, 0, 8),
        });
        panel.Children.Add(lists);
        panel.Children.Add(DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 78,
            rowMargin: new Thickness(0, 10, 0, 0)));
        Content = panel;
        Loaded += (_, _) => _categories.Focus();
    }

    public string? Result { get; private set; }

    public static string? Ask(Window? owner)
    {
        var dialog = new FieldPickerDialog { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void RefreshFields()
    {
        var category = _categories.SelectedItem as string;
        _fields.ItemsSource = FieldPickerDialogPlanner
            .ChoicesForCategory(category)
            .Select(choice => choice.Label)
            .ToArray();
        _fields.SelectedIndex = _fields.Items.Count > 0 ? 0 : -1;
    }

    private void Accept()
    {
        if (!FieldPickerDialogPlanner.TryGetInstruction(
                _categories.SelectedItem as string,
                _fields.SelectedItem as string,
                out var instruction))
        {
            return;
        }

        Result = instruction;
        DialogResult = true;
    }
}
