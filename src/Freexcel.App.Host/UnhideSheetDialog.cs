using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

public sealed record UnhideSheetDialogResult(string SheetName);

public sealed class UnhideSheetDialog : Window
{
    private readonly ListBox _sheetBox = new();

    public UnhideSheetDialogResult Result { get; private set; }

    public UnhideSheetDialog(IEnumerable<string> hiddenSheetNames)
    {
        var names = hiddenSheetNames.ToList();
        var selected = names.FirstOrDefault() ?? "";
        Result = CreateResult(selected);
        Title = "Unhide Sheet";
        Width = 340;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _sheetBox.ItemsSource = names;
        _sheetBox.SelectedItem = selected;
        _sheetBox.SelectionMode = SelectionMode.Single;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = "_Sheet:", Target = _sheetBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _sheetBox.Margin = new Thickness(0, 0, 0, 12);
        _sheetBox.MinHeight = 64;
        stack.Children.Add(_sheetBox);
        stack.Children.Add(DialogButtonRowFactory.Create(Accept, 72));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static UnhideSheetDialogResult CreateResult(string sheetName) => new(sheetName.Trim());

    private void FocusInitialKeyboardTarget()
    {
        _sheetBox.Focus();
        Keyboard.Focus(_sheetBox);
    }

    private void Accept()
    {
        if (_sheetBox.SelectedItem is not string sheetName)
            return;

        Result = CreateResult(sheetName);
        DialogResult = true;
    }
}
