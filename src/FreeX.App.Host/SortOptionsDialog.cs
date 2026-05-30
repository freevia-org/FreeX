using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

public sealed class SortOptionsDialog : Window
{
    private static readonly string[] FirstKeySortOrders =
    [
        "Normal",
        "Sun, Mon, Tue, Wed, Thu, Fri, Sat",
        "Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday",
        "Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec",
        "January, February, March, April, May, June, July, August, September, October, November, December"
    ];

    private readonly CheckBox _caseSensitiveBox;
    private readonly ComboBox _firstKeySortOrderBox;
    private readonly RadioButton _topToBottomButton;
    private readonly RadioButton _leftToRightButton;

    public SortDialogOptions Result { get; private set; }

    public SortOptionsDialog(SortDialogOptions? current = null)
    {
        current ??= new SortDialogOptions();
        Result = current;
        Title = "Sort Options";
        Width = 330;
        Height = 260;
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

        _firstKeySortOrderBox = new ComboBox
        {
            ItemsSource = FirstKeySortOrders,
            SelectedItem = NormalizeFirstKeySortOrder(current.FirstKeySortOrder),
            Margin = new Thickness(0, 0, 0, 10)
        };
        body.Children.Add(new Label
        {
            Content = "_First key sort order:",
            Target = _firstKeySortOrderBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 3)
        });
        body.Children.Add(_firstKeySortOrderBox);

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
                LeftToRight: _leftToRightButton.IsChecked == true,
                FirstKeySortOrder: _firstKeySortOrderBox.SelectedItem as string ?? "Normal");
            DialogResult = true;
        }, buttonWidth: 72));
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private static string NormalizeFirstKeySortOrder(string? value) =>
        FirstKeySortOrders.FirstOrDefault(order => string.Equals(order, value, StringComparison.Ordinal)) ?? "Normal";

    private void FocusInitialKeyboardTarget()
    {
        _caseSensitiveBox.Focus();
        Keyboard.Focus(_caseSensitiveBox);
    }
}
