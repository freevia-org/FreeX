using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public sealed class PasteSpecialDialog : Window
{
    private readonly RadioButton _rbAll;
    private readonly RadioButton _rbValues;
    private readonly RadioButton _rbFormats;
    private readonly RadioButton _rbFormulas;
    private readonly RadioButton _rbPicture;
    private readonly CheckBox _pasteLink;
    private readonly CheckBox _transpose;
    private readonly CheckBox _keepColumnWidths;
    private readonly ComboBox _operation;

    public bool PasteValues    => _rbValues.IsChecked == true;
    public bool PasteFormats   => _rbFormats.IsChecked == true;
    public bool PasteFormulas  => _rbFormulas.IsChecked == true;
    public bool PastePicture   => _rbPicture.IsChecked == true;
    public bool PasteLink      => _pasteLink.IsChecked == true;
    public bool Transpose      => _transpose.IsChecked == true;
    public bool KeepColumnWidths => _keepColumnWidths.IsChecked == true;
    public string Operation    => (_operation.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";

    public PasteSpecialDialog()
    {
        Title = "Paste Special";
        Width = 310; Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(16) };

        _rbAll      = new RadioButton { Content = "All",            IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
        _rbValues   = new RadioButton { Content = "Values only",    Margin = new Thickness(0, 0, 0, 6) };
        _rbFormulas = new RadioButton { Content = "Formulas only",  Margin = new Thickness(0, 0, 0, 6) };
        _rbFormats  = new RadioButton { Content = "Formats only",   Margin = new Thickness(0, 0, 0, 6) };
        _rbPicture  = new RadioButton { Content = "Picture",        Margin = new Thickness(0, 0, 0, 12) };
        _pasteLink  = new CheckBox { Content = "Paste Link", Margin = new Thickness(0, 0, 0, 8) };
        _transpose  = new CheckBox { Content = "Transpose", Margin = new Thickness(0, 4, 0, 8) };
        _keepColumnWidths = new CheckBox { Content = "Keep source column widths", Margin = new Thickness(0, 0, 0, 8) };
        _operation  = new ComboBox { Margin = new Thickness(0, 0, 0, 12), Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var op in new[] { "None", "Add", "Subtract", "Multiply", "Divide" })
            _operation.Items.Add(new ComboBoxItem { Content = op });
        _operation.SelectedIndex = 0;

        stack.Children.Add(_rbAll);
        stack.Children.Add(_rbValues);
        stack.Children.Add(_rbFormulas);
        stack.Children.Add(_rbFormats);
        stack.Children.Add(_rbPicture);
        stack.Children.Add(_pasteLink);
        stack.Children.Add(_transpose);
        stack.Children.Add(_keepColumnWidths);
        stack.Children.Add(new TextBlock { Text = "Operation", Margin = new Thickness(0, 0, 0, 3) });
        stack.Children.Add(_operation);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => { DialogResult = true; };
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        stack.Children.Add(btnRow);

        Content = stack;
    }
}
