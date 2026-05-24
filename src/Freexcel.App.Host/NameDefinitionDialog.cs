using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

public sealed record NameDefinitionDialogResult(string Name, string Scope, string Comment, string RefersTo);

internal sealed class NameDefinitionDialog : Window
{
    private readonly TextBox _nameBox = new();
    private readonly ComboBox _scopeBox = new();
    private readonly TextBox _commentBox = new();
    private readonly TextBox _refersToBox = new();
    private readonly Button _rangePickerButton = new() { Content = "...", Width = 26 };
    private readonly IReadOnlyList<string> _scopeOptions;
    private readonly Action<NamedRangeSelectionRequest>? _requestRangeSelection;
    private readonly Func<string, bool> _isValidRange;

    public NameDefinitionDialogResult Result { get; private set; }
    public NamedRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public NameDefinitionDialog(
        NameDefinitionDialogResult initial,
        IReadOnlyList<string> scopeOptions,
        Action<NamedRangeSelectionRequest>? requestRangeSelection = null,
        Func<string, bool>? isValidRange = null)
    {
        Result = initial;
        _scopeOptions = scopeOptions.Count > 0 ? scopeOptions : ["Workbook"];
        _requestRangeSelection = requestRangeSelection;
        _isValidRange = isValidRange ?? (rangeText => !string.IsNullOrWhiteSpace(rangeText));
        Title = string.IsNullOrWhiteSpace(initial.Name) ? "New Name" : "Edit Name";
        Width = 460;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _nameBox.Text = initial.Name;
        foreach (var scope in _scopeOptions)
            _scopeBox.Items.Add(scope);
        _scopeBox.SelectedItem = _scopeOptions.FirstOrDefault(scope =>
            string.Equals(scope, initial.Scope, StringComparison.OrdinalIgnoreCase)) ?? _scopeOptions[0];
        _commentBox.Text = initial.Comment;
        _refersToBox.Text = initial.RefersTo;
        _rangePickerButton.ToolTip = "Collapse dialog and select the referenced range from the worksheet";
        _rangePickerButton.Click += (_, _) =>
        {
            RangeSelectionRequest = NamedRangeDialog.CreateRangeSelectionRequest(
                NamedRangeSelectionTarget.DefinitionRefersTo,
                _refersToBox.Text);
            _requestRangeSelection?.Invoke(RangeSelectionRequest);
            _refersToBox.Focus();
            _refersToBox.SelectAll();
        };

        Content = CreateContent();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private Grid CreateContent()
    {
        var grid = new Grid { Margin = new Thickness(16) };
        for (var row = 0; row < 5; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddTextRow(grid, 0, "_Name:", _nameBox);
        AddComboRow(grid, 1, "_Scope:", _scopeBox);
        AddTextRow(grid, 2, "_Comment:", _commentBox);
        AddRefersToRow(grid, 3);

        var buttons = DialogButtonRowFactory.Create(Accept, 72);
        buttons.Margin = new Thickness(0, 8, 0, 0);
        grid.Children.Add(buttons);
        Grid.SetRow(buttons, 4);
        Grid.SetColumnSpan(buttons, 3);
        return grid;
    }

    private static void AddTextRow(Grid grid, int row, string label, TextBox box)
    {
        grid.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);
        box.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        Grid.SetColumnSpan(box, 2);
    }

    private static void AddComboRow(Grid grid, int row, string label, ComboBox box)
    {
        grid.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);
        box.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        Grid.SetColumnSpan(box, 2);
    }

    private void AddRefersToRow(Grid grid, int row)
    {
        grid.Children.Add(new Label { Content = "_Refers to:", Target = _refersToBox, Padding = new Thickness(0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);
        _refersToBox.Margin = new Thickness(0, 0, 4, 8);
        grid.Children.Add(_refersToBox);
        Grid.SetRow(_refersToBox, row);
        Grid.SetColumn(_refersToBox, 1);
        _rangePickerButton.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(_rangePickerButton);
        Grid.SetRow(_rangePickerButton, row);
        Grid.SetColumn(_rangePickerButton, 2);
    }

    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show(this, "Please enter a name.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusNameInput();
            return;
        }

        if (!_isValidRange(_refersToBox.Text.Trim()))
        {
            MessageBox.Show(this, "Invalid range format. Use: SheetName!A1:B10 or A1:B10", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusRefersToInput();
            return;
        }

        Result = new NameDefinitionDialogResult(
            _nameBox.Text.Trim(),
            (_scopeBox.SelectedItem as string)?.Trim() ?? "Workbook",
            _commentBox.Text.Trim(),
            _refersToBox.Text.Trim());
        DialogResult = true;
    }

    private void FocusNameInput()
    {
        _nameBox.Focus();
        _nameBox.SelectAll();
        Keyboard.Focus(_nameBox);
    }

    private void FocusRefersToInput()
    {
        _refersToBox.Focus();
        _refersToBox.SelectAll();
        Keyboard.Focus(_refersToBox);
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusNameInput();
    }
}
