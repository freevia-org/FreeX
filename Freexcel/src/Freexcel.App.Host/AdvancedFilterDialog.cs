using System.Windows;
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
        root.Children.Add(new TextBlock { Text = "List range:" });
        root.Children.Add(_listRangeBox);
        root.Children.Add(new TextBlock { Text = "Criteria range:", Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(_criteriaRangeBox);
        root.Children.Add(new TextBlock { Text = "Copy to cell (optional):", Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(_copyToBox);
        root.Children.Add(_uniqueBox);
        root.Children.Add(TextToColumnsDialog.CreateButtonRow(Accept));
        Content = root;
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

    private void Accept()
    {
        if (!TryParse(_sheetId, _listRangeBox.Text, _criteriaRangeBox.Text, _copyToBox.Text, _uniqueBox.IsChecked == true, out var result, out var error))
        {
            MessageBox.Show(this, error ?? "Enter valid filter ranges.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
