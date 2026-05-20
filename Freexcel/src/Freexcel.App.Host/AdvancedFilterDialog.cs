using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record AdvancedFilterDialogResult(
    GridRange ListRange,
    GridRange CriteriaRange,
    CellAddress? CopyToCell,
    bool UniqueRecordsOnly);

public sealed class AdvancedFilterDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _listRangeBox = new();
    private readonly TextBox _criteriaRangeBox = new() { Text = "E1:F2" };
    private readonly TextBox _copyToBox = new();
    private readonly RadioButton _filterInPlaceButton = new() { Content = "Filter the list, in-place", IsChecked = true };
    private readonly RadioButton _copyToAnotherLocationButton = new() { Content = "Copy to another location" };
    private readonly CheckBox _uniqueBox = new() { Content = "Unique records only" };

    public AdvancedFilterDialogResult? Result { get; private set; }

    public AdvancedFilterDialog(SheetId sheetId, string defaultListRange)
    {
        _sheetId = sheetId;
        Title = "Advanced Filter";
        Width = 360;
        Height = 260;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _listRangeBox.Text = defaultListRange;
        var root = new StackPanel { Margin = new Thickness(12) };
        _filterInPlaceButton.Checked += (_, _) => UpdateCopyToState();
        _copyToAnotherLocationButton.Checked += (_, _) => UpdateCopyToState();
        root.Children.Add(_filterInPlaceButton);
        root.Children.Add(_copyToAnotherLocationButton);
        root.Children.Add(new TextBlock { Text = "List range:" });
        root.Children.Add(CreateReferenceEditor(_listRangeBox, "Select list range"));
        root.Children.Add(new TextBlock { Text = "Criteria range:", Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(CreateReferenceEditor(_criteriaRangeBox, "Select criteria range"));
        root.Children.Add(new TextBlock { Text = "Copy to cell (optional):", Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(CreateReferenceEditor(_copyToBox, "Select copy-to cell"));
        root.Children.Add(_uniqueBox);
        root.Children.Add(TextToColumnsDialog.CreateButtonRow(Accept));
        Content = root;
        UpdateCopyToState();
    }

    public static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        bool uniqueRecordsOnly,
        out AdvancedFilterDialogResult result,
        out string? error)
    {
        result = default!;
        error = null;

        if (!AdvancedFilterInputParser.TryParseRange(currentSheetId, listRangeText, _ => null, out var listRange))
        {
            error = "Enter a valid list range.";
            return false;
        }

        if (!AdvancedFilterInputParser.TryParseRange(currentSheetId, criteriaRangeText, _ => null, out var criteriaRange))
        {
            error = "Enter a valid criteria range.";
            return false;
        }

        if (!AdvancedFilterInputParser.TryParseCopyDestination(copyToCellText ?? "", currentSheetId, out var copyToCell))
        {
            error = "Enter a valid copy-to cell.";
            return false;
        }

        result = new AdvancedFilterDialogResult(listRange, criteriaRange, copyToCell, uniqueRecordsOnly);
        return true;
    }

    public static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        bool copyToAnotherLocation,
        bool uniqueRecordsOnly,
        out AdvancedFilterDialogResult result,
        out string? error) =>
        TryParse(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToAnotherLocation ? copyToCellText : "",
            uniqueRecordsOnly,
            out result,
            out error);

    private static DockPanel CreateReferenceEditor(TextBox textBox, string automationName)
    {
        var panel = new DockPanel();
        var pickerButton = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = textBox
        };
        AutomationProperties.SetName(pickerButton, automationName);
        pickerButton.Click += ReferencePickerButton_Click;
        panel.Children.Add(pickerButton);
        panel.Children.Add(textBox);
        return panel;
    }

    private static void ReferencePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TextBox textBox })
            return;

        textBox.Focus();
        textBox.SelectAll();
    }

    private void UpdateCopyToState()
    {
        _copyToBox.IsEnabled = _copyToAnotherLocationButton.IsChecked == true;
    }

    private void Accept()
    {
        if (!TryParse(
                _sheetId,
                _listRangeBox.Text,
                _criteriaRangeBox.Text,
                _copyToBox.Text,
                _copyToAnotherLocationButton.IsChecked == true,
                _uniqueBox.IsChecked == true,
                out var result,
                out var error))
        {
            MessageBox.Show(this, error ?? "Enter valid filter ranges.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
