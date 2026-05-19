using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum PivotTableDestinationKind
{
    NewWorksheet,
    ExistingWorksheet
}

public sealed record PivotTableDialogResult(
    string SourceRangeText,
    PivotTableDestinationKind DestinationKind,
    string DestinationRangeText,
    bool OpenFieldList);

public sealed class PivotTableDialog : Window
{
    private readonly TextBox _sourceRangeBox = new();
    private readonly TextBox _destinationRangeBox = new();
    private readonly RadioButton _newWorksheetButton = new() { Content = "New worksheet" };
    private readonly RadioButton _existingWorksheetButton = new() { Content = "Existing worksheet", IsChecked = true };
    private readonly CheckBox _openFieldListBox = new() { Content = "Open PivotTable Fields pane", IsChecked = true };

    public PivotTableDialogResult Result { get; private set; }

    public PivotTableDialog(Workbook workbook, SheetId sourceSheetId, GridRange sourceRange)
    {
        var sourceRangeText = FormatRange(workbook, sourceSheetId, sourceRange);
        var destinationText = FormatDestination(workbook, sourceSheetId, sourceRange);
        Result = CreateResult(
            sourceRangeText,
            PivotTableDestinationKind.ExistingWorksheet,
            destinationText,
            openFieldList: true);

        Title = "Create PivotTable";
        Width = 430;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(16) };

        stack.Children.Add(new TextBlock { Text = "Table/Range", Margin = new Thickness(0, 0, 0, 4) });
        _sourceRangeBox.Text = Result.SourceRangeText;
        _sourceRangeBox.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(_sourceRangeBox);

        stack.Children.Add(new TextBlock { Text = "Choose where to place the PivotTable", Margin = new Thickness(0, 0, 0, 6) });
        _newWorksheetButton.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(_newWorksheetButton);
        stack.Children.Add(_existingWorksheetButton);

        _destinationRangeBox.Text = Result.DestinationRangeText;
        _destinationRangeBox.Margin = new Thickness(22, 4, 0, 12);
        stack.Children.Add(_destinationRangeBox);

        _openFieldListBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_openFieldListBox);

        stack.Children.Add(new TextBlock
        {
            Text = "Freexcel creates worksheet-range PivotTables; data-model and external OLAP sources remain excluded.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var ok = new Button { Content = "Create", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            Result = CreateResult(
                _sourceRangeBox.Text,
                _existingWorksheetButton.IsChecked == true
                    ? PivotTableDestinationKind.ExistingWorksheet
                    : PivotTableDestinationKind.NewWorksheet,
                _destinationRangeBox.Text,
                _openFieldListBox.IsChecked == true);
            DialogResult = true;
        };
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        stack.Children.Add(btnRow);

        Content = stack;
    }

    public static PivotTableDialogResult CreateResult(
        string sourceRangeText,
        PivotTableDestinationKind destinationKind,
        string destinationRangeText,
        bool openFieldList) =>
        new(
            RequireRangeText(sourceRangeText, nameof(sourceRangeText)),
            destinationKind,
            destinationKind == PivotTableDestinationKind.NewWorksheet
                ? string.Empty
                : RequireRangeText(destinationRangeText, nameof(destinationRangeText)),
            openFieldList);

    private static string FormatRange(Workbook workbook, SheetId sheetId, GridRange range)
    {
        var sheetName = workbook.GetSheet(sheetId)?.Name;
        var address = $"{range.Start.ToA1()}:{range.End.ToA1()}";
        return string.IsNullOrWhiteSpace(sheetName)
            ? address
            : $"{PivotUiPlanner.QuoteSheetNameForReference(sheetName)}!{address}";
    }

    private static string FormatDestination(Workbook workbook, SheetId sheetId, GridRange sourceRange)
    {
        var sheetName = workbook.GetSheet(sheetId)?.Name;
        var col = Math.Min(sourceRange.End.Col + 2, CellAddress.MaxCol);
        var address = new CellAddress(sheetId, sourceRange.Start.Row, col).ToA1();
        return string.IsNullOrWhiteSpace(sheetName)
            ? address
            : $"{PivotUiPlanner.QuoteSheetNameForReference(sheetName)}!{address}";
    }

    private static string RequireRangeText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Range text is required.", parameterName);

        return value.Trim();
    }
}
