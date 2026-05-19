using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record CreateTableDialogResult(GridRange Range, bool FirstRowHasHeaders, string TableStyleName);

public sealed class CreateTableDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _rangeBox = new();
    private readonly CheckBox _headersBox = new() { Content = "My table has headers", IsChecked = true };
    private readonly string _tableStyleName;

    public CreateTableDialogResult? Result { get; private set; }

    public CreateTableDialog(SheetId sheetId, string defaultRangeText, string tableStyleName)
    {
        _sheetId = sheetId;
        _tableStyleName = tableStyleName;
        Title = "Create Table";
        Width = 360;
        Height = 190;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _rangeBox.Text = defaultRangeText;
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Where is the data for your table?", Margin = new Thickness(0, 0, 0, 4) });
        _rangeBox.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(_rangeBox);
        _headersBox.Margin = new Thickness(0, 0, 0, 16);
        root.Children.Add(_headersBox);
        root.Children.Add(TextToColumnsDialog.CreateButtonRow(Accept));
        Content = root;
    }

    public static bool TryParse(
        SheetId sheetId,
        string rangeText,
        bool firstRowHasHeaders,
        string tableStyleName,
        out CreateTableDialogResult result,
        out string? error)
    {
        result = default!;
        error = null;
        if (string.IsNullOrWhiteSpace(rangeText))
        {
            error = "Enter a table range.";
            return false;
        }

        try
        {
            var range = rangeText.Contains(':', StringComparison.Ordinal)
                ? GridRange.Parse(rangeText.Trim(), sheetId)
                : new GridRange(CellAddress.Parse(rangeText.Trim(), sheetId), CellAddress.Parse(rangeText.Trim(), sheetId));

            if (range.End.Row <= range.Start.Row)
            {
                error = "Table range must include at least two rows.";
                return false;
            }

            result = new CreateTableDialogResult(range, firstRowHasHeaders, tableStyleName.Trim());
            return true;
        }
        catch (FormatException)
        {
            error = "Enter a valid table range.";
            return false;
        }
    }

    private void Accept()
    {
        if (!TryParse(_sheetId, _rangeBox.Text, _headersBox.IsChecked == true, _tableStyleName, out var result, out var error))
        {
            MessageBox.Show(this, error ?? "Enter a valid table range.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
