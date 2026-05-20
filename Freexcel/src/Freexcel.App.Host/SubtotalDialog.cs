using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record SubtotalColumnChoice(uint Offset, string Header, bool IsSelected);

public sealed record SubtotalDialogResult(
    uint GroupColumnOffset,
    IReadOnlyList<uint> SubtotalColumnOffsets,
    int FunctionNumber,
    bool ReplaceCurrentSubtotals,
    bool PageBreakBetweenGroups,
    bool SummaryBelowData);

public sealed class SubtotalDialog : Window
{
    private readonly ComboBox _groupColumnBox = new() { DisplayMemberPath = nameof(SubtotalColumnChoice.Header), SelectedValuePath = nameof(SubtotalColumnChoice.Offset) };
    private readonly List<CheckBox> _subtotalColumnBoxes = [];
    private readonly TextBox _functionBox = new() { Text = "sum" };
    private readonly CheckBox _replaceBox = new() { Content = "Replace current subtotals", IsChecked = true };
    private readonly CheckBox _pageBreakBox = new() { Content = "Page break between groups" };
    private readonly CheckBox _summaryBelowBox = new() { Content = "Summary below data", IsChecked = true };

    public SubtotalDialogResult? Result { get; private set; }

    public SubtotalDialog(IEnumerable<SubtotalColumnChoice>? columns = null)
    {
        var columnChoices = NormalizeColumnChoices(columns);

        Title = "Subtotal";
        Width = 380;
        Height = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = "At each change in:" });
        _groupColumnBox.ItemsSource = columnChoices;
        _groupColumnBox.SelectedValue = columnChoices[0].Offset;
        root.Children.Add(_groupColumnBox);
        root.Children.Add(new TextBlock { Text = "Add subtotal to:", Margin = new Thickness(0, 8, 0, 0) });
        foreach (var column in columnChoices)
        {
            var box = new CheckBox
            {
                Content = column.Header,
                Tag = column.Offset,
                IsChecked = column.IsSelected,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _subtotalColumnBoxes.Add(box);
            root.Children.Add(box);
        }
        root.Children.Add(new TextBlock { Text = "Use function:", Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(_functionBox);
        root.Children.Add(_replaceBox);
        root.Children.Add(_pageBreakBox);
        root.Children.Add(_summaryBelowBox);
        root.Children.Add(TextToColumnsDialog.CreateButtonRow(Accept));
        Content = root;
    }

    public static SubtotalDialogResult CreateResult(
        uint groupColumnOffset,
        IEnumerable<uint> subtotalColumnOffsets,
        string functionText,
        bool replaceCurrentSubtotals,
        bool pageBreakBetweenGroups,
        bool summaryBelowData)
    {
        if (!SubtotalFunctionService.TryParse(functionText, out var functionNumber))
            throw new ArgumentException("Unsupported SUBTOTAL function.", nameof(functionText));

        var offsets = subtotalColumnOffsets.Distinct().ToList();
        if (offsets.Count == 0)
            throw new ArgumentException("At least one subtotal column is required.", nameof(subtotalColumnOffsets));

        return new SubtotalDialogResult(
            groupColumnOffset,
            offsets,
            functionNumber,
            replaceCurrentSubtotals,
            pageBreakBetweenGroups,
            summaryBelowData);
    }

    public static IReadOnlyList<SubtotalColumnChoice> BuildColumnChoices(Sheet sheet, GridRange range)
    {
        var choices = new List<SubtotalColumnChoice>();
        for (uint offset = 0; offset < range.ColCount; offset++)
        {
            var absoluteColumn = range.Start.Col + offset;
            var header = SpreadsheetDisplayFormatter.FormatCellValue(sheet.GetCell(range.Start.Row, absoluteColumn)?.Value);
            if (string.IsNullOrWhiteSpace(header))
                header = $"Column {CellAddress.NumberToColumnName(absoluteColumn)}";

            choices.Add(new SubtotalColumnChoice(offset, header, offset != 0));
        }

        return choices.Count == 0 ? [new SubtotalColumnChoice(0, "Column A", false)] : choices;
    }

    private void Accept()
    {
        var groupColumnOffset = _groupColumnBox.SelectedValue is uint offset ? offset : 0;
        var subtotalColumnOffsets = _subtotalColumnBoxes
            .Where(box => box.IsChecked == true)
            .Select(box => (uint)box.Tag)
            .ToList();

        try
        {
            Result = CreateResult(
                groupColumnOffset,
                subtotalColumnOffsets,
                _functionBox.Text,
                _replaceBox.IsChecked == true,
                _pageBreakBox.IsChecked == true,
                _summaryBelowBox.IsChecked == true);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private static IReadOnlyList<SubtotalColumnChoice> NormalizeColumnChoices(IEnumerable<SubtotalColumnChoice>? columns)
    {
        var normalized = columns?.ToList() ?? [];
        return normalized.Count == 0
            ? [new SubtotalColumnChoice(0, "Column 1", false), new SubtotalColumnChoice(1, "Column 2", true)]
            : normalized;
    }
}
