using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

public sealed class SortOptionsDialog : Window
{
    private readonly CheckBox _caseSensitiveBox;
    private readonly RadioButton _topToBottomButton;
    private readonly RadioButton _leftToRightButton;

    public SortDialogOptions Result { get; private set; }

    public SortOptionsDialog(SortDialogOptions? current = null)
    {
        current ??= new SortDialogOptions();
        Result = current;
        Title = "Sort Options";
        Width = 330;
        Height = 210;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(12) };
        var body = new StackPanel();
        DockPanel.SetDock(body, Dock.Top);
        root.Children.Add(body);

        _caseSensitiveBox = new CheckBox
        {
            Content = "_Case sensitive",
            IsChecked = current.CaseSensitive,
            Margin = new Thickness(0, 0, 0, 10)
        };
        body.Children.Add(_caseSensitiveBox);

        _topToBottomButton = new RadioButton { Content = "Sort top to _bottom", IsChecked = !current.LeftToRight };
        _leftToRightButton = new RadioButton { Content = "Sort left to _right", IsChecked = current.LeftToRight };

        var orientation = new GroupBox
        {
            Header = "Orientation",
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 10),
            Content = new StackPanel
            {
                Children =
                {
                    _topToBottomButton,
                    _leftToRightButton
                }
            }
        };
        body.Children.Add(orientation);

        root.Children.Add(DialogButtonRowFactory.Create(() =>
        {
            Result = new SortDialogOptions(
                CaseSensitive: _caseSensitiveBox.IsChecked == true,
                LeftToRight: _leftToRightButton.IsChecked == true);
            DialogResult = true;
        }, buttonWidth: 72));
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _caseSensitiveBox.Focus();
        Keyboard.Focus(_caseSensitiveBox);
    }
}
