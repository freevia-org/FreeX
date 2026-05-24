using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record SubtotalColumnChoice(uint Offset, string Header, bool IsSelected);

public enum SubtotalDialogAction
{
    Apply,
    RemoveAll
}

public sealed record SubtotalDialogResult(
    uint GroupColumnOffset,
    IReadOnlyList<uint> SubtotalColumnOffsets,
    int FunctionNumber,
    bool ReplaceCurrentSubtotals,
    bool PageBreakBetweenGroups,
    bool SummaryBelowData,
    SubtotalDialogAction Action = SubtotalDialogAction.Apply);

public sealed class SubtotalDialog : Window
{
    private static readonly IReadOnlyList<string> SubtotalFunctionChoices =
        ["Sum", "Count", "Average", "Max", "Min", "Product", "Count Numbers", "StdDev", "StdDevp", "Var", "Varp"];

    private readonly ComboBox _groupColumnBox = new() { DisplayMemberPath = nameof(SubtotalColumnChoice.Header), SelectedValuePath = nameof(SubtotalColumnChoice.Offset) };
    private readonly List<CheckBox> _subtotalColumnBoxes = [];
    private readonly StackPanel _subtotalColumnPanel = new();
    private readonly ComboBox _functionBox = new ComboBox { ItemsSource = SubtotalFunctionChoices, SelectedItem = "Sum" };
    private readonly CheckBox _replaceBox = new() { IsChecked = true };
    private readonly CheckBox _pageBreakBox = new();
    private readonly CheckBox _summaryBelowBox = new() { IsChecked = true };

    public SubtotalDialogResult? Result { get; private set; }

    public SubtotalDialog(IEnumerable<SubtotalColumnChoice>? columns = null)
    {
        var columnChoices = NormalizeColumnChoices(columns);

        Title = "Subtotal";
        Width = 380;
        Height = 390;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new Label { Content = "_At each change in:", Target = _groupColumnBox, Padding = new Thickness(0) });
        _groupColumnBox.ItemsSource = columnChoices;
        _groupColumnBox.SelectedValue = columnChoices[0].Offset;
        root.Children.Add(_groupColumnBox);
        root.Children.Add(new Label { Content = "_Use function:", Target = _functionBox, Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(_functionBox);
        root.Children.Add(new Label { Content = "_Add subtotal to:", Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) });
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
            _subtotalColumnPanel.Children.Add(box);
        }
        root.Children.Add(new GroupBox { Header = "Add subtotal to:", Content = _subtotalColumnPanel });
        _replaceBox.Content = "_Replace current subtotals";
        _pageBreakBox.Content = "_Page break between groups";
        _summaryBelowBox.Content = "_Summary below data";
        root.Children.Add(_replaceBox);
        root.Children.Add(_pageBreakBox);
        root.Children.Add(_summaryBelowBox);
        root.Children.Add(CreateSubtotalButtonRow(Accept, RemoveAll));
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
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

    public static SubtotalDialogResult CreateRemoveAllResult() =>
        new(
            GroupColumnOffset: 0,
            SubtotalColumnOffsets: [],
            FunctionNumber: 9,
            ReplaceCurrentSubtotals: false,
            PageBreakBetweenGroups: false,
            SummaryBelowData: true,
            Action: SubtotalDialogAction.RemoveAll);

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
                _functionBox.SelectedItem?.ToString() ?? "",
                _replaceBox.IsChecked == true,
                _pageBreakBox.IsChecked == true,
                _summaryBelowBox.IsChecked == true);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusSubtotalColumnChoices();
            return;
        }

        DialogResult = true;
    }

    private void FocusSubtotalColumnChoices()
    {
        if (_subtotalColumnBoxes.FirstOrDefault() is { } firstColumnBox)
        {
            firstColumnBox.Focus();
            Keyboard.Focus(firstColumnBox);
        }
    }

    private void RemoveAll()
    {
        Result = CreateRemoveAllResult();
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _groupColumnBox.Focus();
        Keyboard.Focus(_groupColumnBox);
    }

    private static Grid CreateSubtotalButtonRow(Action accept, Action removeAll)
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var removeButton = new Button
        {
            Content = "_Remove All",
            Width = 92,
            Margin = new Thickness(0, 0, 8, 0)
        };
        removeButton.Click += (_, _) => removeAll();
        grid.Children.Add(removeButton);

        var buttons = TextToColumnsDialog.CreateButtonRow(accept);
        buttons.Margin = new Thickness(0);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        return grid;
    }

    private static IReadOnlyList<SubtotalColumnChoice> NormalizeColumnChoices(IEnumerable<SubtotalColumnChoice>? columns)
    {
        var normalized = columns?.ToList() ?? [];
        return normalized.Count == 0
            ? [new SubtotalColumnChoice(0, "Column 1", false), new SubtotalColumnChoice(1, "Column 2", true)]
            : normalized;
    }
}
