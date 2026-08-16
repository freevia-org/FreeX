using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Services;

namespace FreeX.App.Host;

public sealed class SortOptionsDialog : Window
{
    private readonly CheckBox _caseSensitiveBox;
    private readonly ComboBox _firstKeySortOrderBox;
    private readonly RadioButton _topToBottomButton;
    private readonly RadioButton _leftToRightButton;

    public SortDialogOptions Result { get; private set; }

    public SortOptionsDialog(SortDialogOptions? current = null)
    {
        current ??= new SortDialogOptions();
        Result = current;
        var presentation = SortOptionsDialogCatalog.Create(UiText.Get);
        Title = presentation.Title;
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
            Content = presentation.CaseSensitive,
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
            ItemsSource = presentation.FirstKeySortOrders,
            DisplayMemberPath = nameof(SortOptionsFirstKeyOrderChoice.Label),
            SelectedValuePath = nameof(SortOptionsFirstKeyOrderChoice.Value),
            IsEditable = true,
            IsTextSearchEnabled = true,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var firstKeySelection = SortOptionsPolicy.ResolveFirstKeyOrderSelection(
            current.FirstKeySortOrder,
            presentation.FirstKeySortOrders,
            preserveUnlistedEditorText: true);
        _firstKeySortOrderBox.SelectedItem = firstKeySelection.SelectedChoice;
        if (firstKeySelection.SelectedChoice is null)
        {
            // Not one of the 4 built-ins (and not "Normal") -- a previously-authored custom list
            // (e.g. round-tripped from WorksheetSortStateModel.CustomList). Show its literal text
            // directly in the editable box instead of silently reverting the choice to "Normal".
            _firstKeySortOrderBox.Text = firstKeySelection.EditorText;
        }
        body.Children.Add(new Label
        {
            Content = presentation.FirstKeySortOrderLabel,
            Target = _firstKeySortOrderBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 3)
        });
        body.Children.Add(_firstKeySortOrderBox);

        _topToBottomButton = new RadioButton { Content = presentation.SortTopToBottom, IsChecked = !current.LeftToRight };
        _leftToRightButton = new RadioButton { Content = presentation.SortLeftToRight, IsChecked = current.LeftToRight };

        var orientation = new GroupBox
        {
            Header = presentation.Orientation,
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
            // SelectedItem is a catalog choice only for Normal or a built-in day/month list. WPF
            // clears it when editable text no longer matches an item, leaving the custom list in Text.
            Result = SortOptionsPolicy.CreateResult(
                _caseSensitiveBox.IsChecked == true,
                _leftToRightButton.IsChecked == true,
                _firstKeySortOrderBox.SelectedItem as SortOptionsFirstKeyOrderChoice,
                _firstKeySortOrderBox.Text);
            DialogResult = true;
        }, buttonWidth: 72);
        buttons.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
            ApplyAutomationNames();
    }

    private void FocusInitialKeyboardTarget()
    {
        _caseSensitiveBox.Focus();
        Keyboard.Focus(_caseSensitiveBox);
    }

    /// <summary>
    /// Screen-reader names for this dialog's controls. Ported from the abandoned
    /// codex/dialog-parity-loop branch, whose paths predate the Freexcel -> FreeX rename.
    /// </summary>
    private void ApplyAutomationNames()
    {
        AutomationProperties.SetName(_caseSensitiveBox, "Case sensitive");
        AutomationProperties.SetName(_topToBottomButton, "Sort top to bottom");
        AutomationProperties.SetName(_leftToRightButton, "Sort left to right");
    }
}
