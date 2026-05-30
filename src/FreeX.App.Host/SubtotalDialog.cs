using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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
    private const string DefaultSubtotalFunction = "Sum";

    private sealed record SubtotalFunctionChoice(string Label, string FunctionText);

    private readonly ComboBox _groupColumnBox = new() { DisplayMemberPath = nameof(SubtotalColumnChoice.Header), SelectedValuePath = nameof(SubtotalColumnChoice.Offset) };
    private readonly List<CheckBox> _subtotalColumnBoxes = [];
    private readonly StackPanel _subtotalColumnPanel = new();
    private readonly ComboBox _functionBox = new()
    {
        ItemsSource = CreateSubtotalFunctionChoices(),
        DisplayMemberPath = nameof(SubtotalFunctionChoice.Label),
        SelectedValuePath = nameof(SubtotalFunctionChoice.FunctionText),
        SelectedValue = DefaultSubtotalFunction
    };
    private readonly CheckBox _replaceBox = new() { IsChecked = true };
    private readonly CheckBox _pageBreakBox = new();
    private readonly CheckBox _summaryBelowBox = new() { IsChecked = true };

    public SubtotalDialogResult? Result { get; private set; }

    public SubtotalDialog(IEnumerable<SubtotalColumnChoice>? columns = null)
    {
        var columnChoices = NormalizeColumnChoices(columns);

        Title = UiText.Get("Subtotal_Subtotal");
        Width = 380;
        Height = 390;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ApplyAutomationMetadata();

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new Label { Content = UiText.Get("Subtotal_AtEachChangeIn"), Target = _groupColumnBox, Padding = new Thickness(0) });
        _groupColumnBox.ItemsSource = columnChoices;
        _groupColumnBox.SelectedValue = columnChoices[0].Offset;
        root.Children.Add(_groupColumnBox);
        root.Children.Add(new Label { Content = UiText.Get("Subtotal_UseFunction"), Target = _functionBox, Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(_functionBox);
        root.Children.Add(new Label { Content = UiText.Get("Subtotal_AddSubtotalTo"), Target = _subtotalColumnPanel, Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) });
        _subtotalColumnPanel.Focusable = true;
        _subtotalColumnPanel.GotKeyboardFocus += (_, _) => FocusSubtotalColumnChoices();
        foreach (var column in columnChoices)
        {
            var box = new CheckBox
            {
                Content = column.Header,
                Tag = column.Offset,
                IsChecked = column.IsSelected,
                Margin = new Thickness(0, 0, 0, 4)
            };
            AutomationProperties.SetName(box, $"{column.Header} subtotal column");
            AutomationProperties.SetAutomationId(box, $"SubtotalColumn{column.Offset}Box");
            AutomationProperties.SetHelpText(box, "Select to add a subtotal calculation to this column.");
            _subtotalColumnBoxes.Add(box);
            _subtotalColumnPanel.Children.Add(box);
        }
        root.Children.Add(new GroupBox { Content = _subtotalColumnPanel });
        _replaceBox.Content = UiText.Get("Subtotal_ReplaceCurrentSubtotals");
        _pageBreakBox.Content = UiText.Get("Subtotal_PageBreakBetweenGroups");
        _summaryBelowBox.Content = UiText.Get("Subtotal_SummaryBelowData");
        root.Children.Add(_replaceBox);
        root.Children.Add(_pageBreakBox);
        root.Children.Add(_summaryBelowBox);
        root.Children.Add(CreateSubtotalButtonRow(Accept, RemoveAll));
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void ApplyAutomationMetadata()
    {
        AutomationProperties.SetName(_groupColumnBox, "At each change in");
        AutomationProperties.SetAutomationId(_groupColumnBox, "SubtotalGroupColumnBox");
        AutomationProperties.SetHelpText(_groupColumnBox, "Choose the column that defines each subtotal group.");

        AutomationProperties.SetName(_functionBox, "Use function");
        AutomationProperties.SetAutomationId(_functionBox, "SubtotalFunctionBox");
        AutomationProperties.SetHelpText(_functionBox, "Choose the function used to calculate each subtotal.");

        AutomationProperties.SetName(_subtotalColumnPanel, "Add subtotal to");
        AutomationProperties.SetAutomationId(_subtotalColumnPanel, "SubtotalColumnsPanel");
        AutomationProperties.SetHelpText(_subtotalColumnPanel, "Choose columns that receive subtotal calculations.");

        AutomationProperties.SetName(_replaceBox, "Replace current subtotals");
        AutomationProperties.SetAutomationId(_replaceBox, "SubtotalReplaceCurrentBox");
        AutomationProperties.SetHelpText(_replaceBox, "Replace existing subtotals with the new subtotal settings.");

        AutomationProperties.SetName(_pageBreakBox, "Page break between groups");
        AutomationProperties.SetAutomationId(_pageBreakBox, "SubtotalPageBreakBox");
        AutomationProperties.SetHelpText(_pageBreakBox, "Insert a page break after each subtotal group.");

        AutomationProperties.SetName(_summaryBelowBox, "Summary below data");
        AutomationProperties.SetAutomationId(_summaryBelowBox, "SubtotalSummaryBelowBox");
        AutomationProperties.SetHelpText(_summaryBelowBox, "Place subtotal rows below each group.");
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
            throw new ArgumentException(UiText.Get("Subtotal_UnsupportedSubtotalFunction"), nameof(functionText));

        var offsets = subtotalColumnOffsets.Distinct().ToList();
        if (offsets.Count == 0)
            throw new ArgumentException(UiText.Get("Subtotal_AtLeastOneSubtotalColumnIsRequired"), nameof(subtotalColumnOffsets));

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
                header = UiText.Format("Subtotal_ColumnLabel", CellAddress.NumberToColumnName(absoluteColumn));

            choices.Add(new SubtotalColumnChoice(offset, header, offset != 0));
        }

        return choices.Count == 0 ? [new SubtotalColumnChoice(0, UiText.Format("Subtotal_ColumnLabel", "A"), false)] : choices;
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
                _functionBox.SelectedValue?.ToString() ?? DefaultSubtotalFunction,
                _replaceBox.IsChecked == true,
                _pageBreakBox.IsChecked == true,
                _summaryBelowBox.IsChecked == true);
        }
        catch (ArgumentException ex)
        {
            DialogMessageHelper.ShowWarning(this, ex.Message, Title);
            FocusInvalidInput(ex.Message);
            return;
        }

        DialogResult = true;
    }

    private void FocusInvalidInput(string message)
    {
        if (string.Equals(message, UiText.Get("Subtotal_UnsupportedSubtotalFunction"), StringComparison.Ordinal))
        {
            FocusFunctionChoice();
            return;
        }

        FocusSubtotalColumnChoices();
    }

    private void FocusFunctionChoice()
    {
        _functionBox.Focus();
        Keyboard.Focus(_functionBox);
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
            Content = UiText.Get("Subtotal_RemoveAll"),
            Width = 92,
            Margin = new Thickness(0, 0, 8, 0)
        };
        AutomationProperties.SetName(removeButton, "Remove all subtotals");
        AutomationProperties.SetAutomationId(removeButton, "SubtotalRemoveAllButton");
        AutomationProperties.SetHelpText(removeButton, "Remove all subtotal rows from the selected data.");
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
            ? [new SubtotalColumnChoice(0, UiText.Format("Subtotal_ColumnLabel", 1), false), new SubtotalColumnChoice(1, UiText.Format("Subtotal_ColumnLabel", 2), true)]
            : normalized;
    }

    private static IReadOnlyList<SubtotalFunctionChoice> CreateSubtotalFunctionChoices() =>
        [
            new(UiText.Get("Subtotal_FunctionSum"), "Sum"),
            new(UiText.Get("Subtotal_FunctionCount"), "Count"),
            new(UiText.Get("Subtotal_FunctionAverage"), "Average"),
            new(UiText.Get("Subtotal_FunctionMax"), "Max"),
            new(UiText.Get("Subtotal_FunctionMin"), "Min"),
            new(UiText.Get("Subtotal_FunctionProduct"), "Product"),
            new(UiText.Get("Subtotal_FunctionCountNumbers"), "Count Numbers"),
            new(UiText.Get("Subtotal_FunctionStdDev"), "StdDev"),
            new(UiText.Get("Subtotal_FunctionStdDevp"), "StdDevp"),
            new(UiText.Get("Subtotal_FunctionVar"), "Var"),
            new(UiText.Get("Subtotal_FunctionVarp"), "Varp")
        ];
}
