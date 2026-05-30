using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

public sealed class SortOptionsDialog : Window
{
    private const string NormalFirstKeySortOrder = "Normal";

    private sealed record FirstKeySortOrderChoice(string Label, string Value);

    private readonly CheckBox _caseSensitiveBox;
    private readonly ComboBox _firstKeySortOrderBox;
    private readonly RadioButton _topToBottomButton;
    private readonly RadioButton _leftToRightButton;

    public SortDialogOptions Result { get; private set; }

    public SortOptionsDialog(SortDialogOptions? current = null)
    {
        current ??= new SortDialogOptions();
        Result = current;
        Title = UiText.Get("SortOptions_SortOptions");
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
            Content = UiText.Get("SortOptions_CaseSensitive"),
            IsChecked = current.CaseSensitive,
            Margin = new Thickness(0, 0, 0, 10)
        };
        body.Children.Add(_caseSensitiveBox);

        _firstKeySortOrderBox = new ComboBox
        {
            ItemsSource = CreateFirstKeySortOrders(),
            DisplayMemberPath = nameof(FirstKeySortOrderChoice.Label),
            SelectedValuePath = nameof(FirstKeySortOrderChoice.Value),
            SelectedValue = NormalizeFirstKeySortOrder(current.FirstKeySortOrder),
            Margin = new Thickness(0, 0, 0, 10)
        };
        body.Children.Add(new Label
        {
            Content = UiText.Get("SortOptions_FirstKeySortOrderLabel"),
            Target = _firstKeySortOrderBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 3)
        });
        body.Children.Add(_firstKeySortOrderBox);

        _topToBottomButton = new RadioButton { Content = UiText.Get("SortOptions_SortTopToBottom"), IsChecked = !current.LeftToRight };
        _leftToRightButton = new RadioButton { Content = UiText.Get("SortOptions_SortLeftToRight"), IsChecked = current.LeftToRight };

        var orientation = new GroupBox
        {
            Header = UiText.Get("SortOptions_Orientation"),
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
                FirstKeySortOrder: _firstKeySortOrderBox.SelectedValue as string ?? NormalFirstKeySortOrder);
            DialogResult = true;
        }, buttonWidth: 72));
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private static IReadOnlyList<FirstKeySortOrderChoice> CreateFirstKeySortOrders() =>
        [
            new(UiText.Get("SortOptions_FirstKeyNormal"), NormalFirstKeySortOrder),
            new(UiText.Get("SortOptions_FirstKeySunToSatShort"), "Sun, Mon, Tue, Wed, Thu, Fri, Sat"),
            new(UiText.Get("SortOptions_FirstKeySundayToSaturday"), "Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday"),
            new(UiText.Get("SortOptions_FirstKeyJanToDecShort"), "Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec"),
            new(UiText.Get("SortOptions_FirstKeyJanuaryToDecember"), "January, February, March, April, May, June, July, August, September, October, November, December")
        ];

    private static string NormalizeFirstKeySortOrder(string? value) =>
        CreateFirstKeySortOrders()
            .FirstOrDefault(order =>
                string.Equals(order.Value, value, StringComparison.Ordinal) ||
                string.Equals(order.Label, value, StringComparison.Ordinal))
            ?.Value ?? NormalFirstKeySortOrder;

    private void FocusInitialKeyboardTarget()
    {
        _caseSensitiveBox.Focus();
        Keyboard.Focus(_caseSensitiveBox);
    }
}
