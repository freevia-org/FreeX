using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record ConsolidateDialogResult(
    IReadOnlyList<GridRange> SourceRanges,
    CellAddress DestinationCell,
    ConsolidateFunction Function,
    bool UseTopRowLabels = false,
    bool UseLeftColumnLabels = false,
    bool CreateLinksToSourceData = false);

public enum ConsolidateRangeSelectionTarget
{
    Reference,
    DestinationCell
}

public sealed record ConsolidateRangeSelectionRequest(
    ConsolidateRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);

public sealed class ConsolidateDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly ComboBox _functionBox = new();
    private readonly TextBox _referenceBox = new();
    private readonly ListBox _referencesList = new() { Height = 72 };
    private readonly Button _deleteReferenceButton = new() { Content = "_Delete", Width = 76, IsEnabled = false };
    private readonly TextBox _destinationBox = new();
    private readonly CheckBox _topRowBox = new() { Content = "_Top row" };
    private readonly CheckBox _leftColumnBox = new() { Content = "_Left column" };
    private readonly CheckBox _createLinksBox = new() { Content = "Create _links to source data" };
    private readonly Action<ConsolidateRangeSelectionRequest>? _requestRangeSelection;

    public ConsolidateDialogResult? Result { get; private set; }
    public ConsolidateRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public ConsolidateDialog(
        SheetId sheetId,
        string defaultSource,
        string defaultDestination,
        Action<ConsolidateRangeSelectionRequest>? requestRangeSelection = null)
    {
        _sheetId = sheetId;
        _requestRangeSelection = requestRangeSelection;
        Title = "Consolidate";
        Width = 380;
        Height = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _referenceBox.Text = defaultSource;
        foreach (var sourceRange in SplitSourceRangeText(defaultSource))
            _referencesList.Items.Add(sourceRange);
        _referencesList.SelectionChanged += (_, _) => UpdateReferenceButtons();

        _destinationBox.Text = defaultDestination;
        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new Label { Content = "_Function:", Target = _functionBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 2) });
        foreach (var function in Enum.GetValues<ConsolidateFunction>())
            _functionBox.Items.Add(new ComboBoxItem { Content = FunctionLabel(function), Tag = function });
        _functionBox.SelectedIndex = 0;
        _functionBox.Margin = new Thickness(0, 0, 0, 8);
        root.Children.Add(_functionBox);
        root.Children.Add(new Label { Content = "_Reference:", Target = _referenceBox, Padding = new Thickness(0) });
        root.Children.Add(CreateReferenceEditor(_referenceBox, "Select reference range", ConsolidateRangeSelectionTarget.Reference));
        var referenceButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 8)
        };
        var addReferenceButton = new Button { Content = "_Add", Width = 76, Margin = new Thickness(0, 0, 8, 0) };
        addReferenceButton.Click += AddReferenceButton_Click;
        _deleteReferenceButton.Click += DeleteReferenceButton_Click;
        referenceButtons.Children.Add(addReferenceButton);
        referenceButtons.Children.Add(_deleteReferenceButton);
        root.Children.Add(referenceButtons);
        root.Children.Add(new Label { Content = "_All references:", Target = _referencesList, Padding = new Thickness(0) });
        root.Children.Add(_referencesList);
        root.Children.Add(new Label { Content = "_Destination cell:", Target = _destinationBox, Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(CreateReferenceEditor(_destinationBox, "Select destination cell", ConsolidateRangeSelectionTarget.DestinationCell));
        root.Children.Add(new Label { Content = "Use _labels in:", Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 2) });
        var labelOptions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _topRowBox.Margin = new Thickness(0, 0, 16, 0);
        labelOptions.Children.Add(_topRowBox);
        labelOptions.Children.Add(_leftColumnBox);
        root.Children.Add(labelOptions);
        _createLinksBox.Margin = new Thickness(0, 0, 0, 12);
        _createLinksBox.ToolTip = "Write formulas that reference the source cells while keeping the consolidated result value.";
        root.Children.Add(_createLinksBox);
        root.Children.Add(TextToColumnsDialog.CreateButtonRow(Accept));
        Content = root;
        UpdateReferenceButtons();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static IReadOnlyList<string> SplitSourceRangeText(string sourceRangesText) =>
        sourceRangesText
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

    public static string JoinSourceRanges(IEnumerable<string> sourceRanges) =>
        string.Join("; ", sourceRanges.Select(item => item.Trim()).Where(item => item.Length > 0));

    public static ConsolidateDialogResult CreateResult(
        IEnumerable<GridRange> sourceRanges,
        CellAddress destinationCell,
        ConsolidateFunction function,
        bool useTopRowLabels = false,
        bool useLeftColumnLabels = false,
        bool createLinksToSourceData = false)
    {
        var ranges = sourceRanges.ToList();
        if (ranges.Count == 0)
            throw new ArgumentException("At least one source range is required.", nameof(sourceRanges));

        return new ConsolidateDialogResult(
            ranges,
            destinationCell,
            function,
            useTopRowLabels,
            useLeftColumnLabels,
            createLinksToSourceData);
    }

    public static bool HaveSameSize(IEnumerable<GridRange> sourceRanges)
    {
        var ranges = sourceRanges.ToList();
        if (ranges.Count < 2)
            return true;

        var rowCount = ranges[0].RowCount;
        var colCount = ranges[0].ColCount;
        return ranges.All(range => range.RowCount == rowCount && range.ColCount == colCount);
    }

    public static bool TryParse(
        SheetId sheetId,
        string sourceRangesText,
        string destinationCellText,
        out ConsolidateDialogResult result,
        out string? error) =>
        TryParse(
            sheetId,
            sourceRangesText,
            destinationCellText,
            ConsolidateFunction.Sum,
            useTopRowLabels: false,
            useLeftColumnLabels: false,
            createLinksToSourceData: false,
            out result,
            out error);

    public static bool TryParse(
        SheetId sheetId,
        string sourceRangesText,
        string destinationCellText,
        ConsolidateFunction function,
        bool useTopRowLabels,
        bool useLeftColumnLabels,
        bool createLinksToSourceData,
        out ConsolidateDialogResult result,
        out string? error)
    {
        result = default!;
        error = null;

        if (!ConsolidateInputParser.TryParseSourceRanges(sourceRangesText, sheetId, out var ranges, out var invalidPart))
        {
            error = string.IsNullOrWhiteSpace(invalidPart)
                ? "Enter at least one valid source range."
                : $"Enter a valid source range: {invalidPart}.";
            return false;
        }

        if (!HaveSameSize(ranges))
        {
            error = "Source ranges must be the same size.";
            return false;
        }

        if (!ConsolidateInputParser.TryParseDestination(destinationCellText, sheetId, out var destination))
        {
            error = "Enter a valid destination cell.";
            return false;
        }

        result = CreateResult(
            ranges,
            destination,
            function,
            useTopRowLabels,
            useLeftColumnLabels,
            createLinksToSourceData);
        return true;
    }

    public static ConsolidateRangeSelectionRequest CreateRangeSelectionRequest(
        ConsolidateRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    private DockPanel CreateReferenceEditor(
        TextBox textBox,
        string automationName,
        ConsolidateRangeSelectionTarget target) =>
        DialogReferencePicker.CreateEditor(
            textBox,
            automationName,
            requestSelection: request => RequestRangeSelection(target, request));

    private void RequestRangeSelection(ConsolidateRangeSelectionTarget target, DialogReferencePickerRequest request)
    {
        RangeSelectionRequest = CreateRangeSelectionRequest(target, request.CurrentText);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
    }

    private void FocusInitialKeyboardTarget()
    {
        _functionBox.Focus();
        Keyboard.Focus(_functionBox);
    }

    private void AddReferenceButton_Click(object sender, RoutedEventArgs e)
    {
        var reference = _referenceBox.Text.Trim();
        if (reference.Length == 0)
            return;

        _referencesList.Items.Add(reference);
        _referenceBox.Clear();
    }

    private void DeleteReferenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_referencesList.SelectedItem is { } selected)
            _referencesList.Items.Remove(selected);
        UpdateReferenceButtons();
    }

    private void UpdateReferenceButtons() =>
        _deleteReferenceButton.IsEnabled = _referencesList.SelectedItem is not null;

    private void Accept()
    {
        var sourceRangesText = JoinSourceRanges(_referencesList.Items.Cast<string>());
        if (!TryParse(
                _sheetId,
                sourceRangesText,
                _destinationBox.Text,
                SelectedFunction(),
                _topRowBox.IsChecked == true,
                _leftColumnBox.IsChecked == true,
                _createLinksBox.IsChecked == true,
                out var result,
                out var error))
        {
            MessageBox.Show(this, error ?? "Enter valid consolidation ranges.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private ConsolidateFunction SelectedFunction() =>
        _functionBox.SelectedItem is ComboBoxItem { Tag: ConsolidateFunction function }
            ? function
            : ConsolidateFunction.Sum;

    private static string FunctionLabel(ConsolidateFunction function) =>
        function switch
        {
            ConsolidateFunction.CountNumbers => "Count Numbers",
            ConsolidateFunction.StdDev => "StdDev",
            ConsolidateFunction.StdDevp => "StdDevp",
            _ => function.ToString()
        };
}
