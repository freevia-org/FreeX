using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public sealed class PasteSpecialDialog : Window
{
    private readonly RadioButton _rbAll;
    private readonly RadioButton _rbValues;
    private readonly RadioButton _rbFormats;
    private readonly RadioButton _rbFormulas;

    public bool PasteValues    => _rbValues.IsChecked == true;
    public bool PasteFormats   => _rbFormats.IsChecked == true;
    public bool PasteFormulas  => _rbFormulas.IsChecked == true;

    public PasteSpecialDialog()
    {
        Title = "Paste Special";
        Width = 280; Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(16) };

        _rbAll      = new RadioButton { Content = "All",            IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
        _rbValues   = new RadioButton { Content = "Values only",    Margin = new Thickness(0, 0, 0, 6) };
        _rbFormulas = new RadioButton { Content = "Formulas only",  Margin = new Thickness(0, 0, 0, 6) };
        _rbFormats  = new RadioButton { Content = "Formats only",   Margin = new Thickness(0, 0, 0, 12) };

        stack.Children.Add(_rbAll);
        stack.Children.Add(_rbValues);
        stack.Children.Add(_rbFormulas);
        stack.Children.Add(_rbFormats);

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
