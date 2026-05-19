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

        if (!TryParseRange(listRangeText, currentSheetId, out var listRange))
        {
            error = "Enter a valid list range.";
            return false;
        }

        if (!TryParseRange(criteriaRangeText, currentSheetId, out var criteriaRange))
        {
            error = "Enter a valid criteria range.";
            return false;
        }

        CellAddress? copyToCell = null;
        if (!string.IsNullOrWhiteSpace(copyToCellText))
        {
            if (!CellAddress.TryParse(copyToCellText, currentSheetId, out var parsedCopyToCell))
            {
                error = "Enter a valid copy-to cell.";
                return false;
            }

            copyToCell = parsedCopyToCell;
        }

        result = new AdvancedFilterDialogResult(listRange, criteriaRange, copyToCell, uniqueRecordsOnly);
        return true;
    }

    private static bool TryParseRange(string text, SheetId sheetId, out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            range = text.Contains(':', StringComparison.Ordinal)
                ? GridRange.Parse(text, sheetId)
                : new GridRange(CellAddress.Parse(text, sheetId), CellAddress.Parse(text, sheetId));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
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
