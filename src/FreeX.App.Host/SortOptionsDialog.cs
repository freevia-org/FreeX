using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Services;

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

        // R91-commands-sort-customlist-5-3: real Excel's "First key sort order" combo also accepts an
        // arbitrary user-authored Custom List (Data > Sort > Options > Custom List..., or File >
        // Options > Advanced > Edit Custom Lists), not just the 4 built-in day/month lists below --
        // and CustomSortOrder.TryParse/Compare already fully supports an arbitrary comma-separated
        // list, it was only ever unreachable because nothing in the UI could produce one. FreeX has
        // no persisted named-custom-lists registry to populate a full "Custom List..." picker from
        // (grepped repo-wide -- confirmed absent), so short of building that whole subsystem, making
        // this combo directly editable is the smallest correct fix that actually unblocks the everyday
        // workflow: the user types their own list (e.g. "Low, Medium, High, Critical") right into the
        // box and it flows through unchanged to SortDialogOptions.FirstKeySortOrder -> SortCommand ->
        // CustomSortOrder exactly like a built-in choice does.
        _firstKeySortOrderBox = new ComboBox
        {
            ItemsSource = CreateFirstKeySortOrders(),
            DisplayMemberPath = nameof(FirstKeySortOrderChoice.Label),
            SelectedValuePath = nameof(FirstKeySortOrderChoice.Value),
            IsEditable = true,
            IsTextSearchEnabled = true,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var initialFirstKeySortOrder = NormalizeFirstKeySortOrder(current.FirstKeySortOrder);
        _firstKeySortOrderBox.SelectedValue = initialFirstKeySortOrder;
        if (_firstKeySortOrderBox.SelectedValue is null)
        {
            // Not one of the 4 built-ins (and not "Normal") -- a previously-authored custom list
            // (e.g. round-tripped from WorksheetSortStateModel.CustomList). Show its literal text
            // directly in the editable box instead of silently reverting the choice to "Normal".
            _firstKeySortOrderBox.Text = initialFirstKeySortOrder;
        }
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

        var buttons = DialogButtonRowFactory.Create(() =>
        {
            // SelectedValue is non-null only when one of the predefined items (Normal or a built-in
            // day/month list) is actually chosen; a typed custom list clears SelectedValue (WPF
            // editable-combo behavior once the text no longer matches any item), so fall back to the
            // raw typed Text in that case -- the user's own custom list.
            var firstKeySortOrder = _firstKeySortOrderBox.SelectedValue as string
                ?? (string.IsNullOrWhiteSpace(_firstKeySortOrderBox.Text)
                    ? NormalFirstKeySortOrder
                    : _firstKeySortOrderBox.Text.Trim());
            Result = new SortDialogOptions(
                CaseSensitive: _caseSensitiveBox.IsChecked == true,
                LeftToRight: _leftToRightButton.IsChecked == true,
                FirstKeySortOrder: firstKeySortOrder);
            DialogResult = true;
        }, buttonWidth: 72);
        buttons.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
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

    private static string NormalizeFirstKeySortOrder(string? value)
    {
        foreach (var order in CreateFirstKeySortOrders())
        {
            if (string.Equals(order.Value, value, StringComparison.Ordinal) ||
                string.Equals(order.Label, value, StringComparison.Ordinal))
            {
                return order.Value;
            }
        }

        // R91-commands-sort-customlist-5-3: a value that isn't one of the 4 built-ins is now a
        // legitimate user-authored custom list (e.g. round-tripped from
        // WorksheetSortStateModel.CustomList, or re-opening this dialog after typing one in) --
        // preserve it verbatim instead of silently discarding the user's choice back to "Normal".
        return string.IsNullOrWhiteSpace(value) ? NormalFirstKeySortOrder : value;
    }

    private void FocusInitialKeyboardTarget()
    {
        _caseSensitiveBox.Focus();
        Keyboard.Focus(_caseSensitiveBox);
    }
}
