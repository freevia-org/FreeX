using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record AdvancedFilterDialogResult(
    GridRange ListRange,
    GridRange CriteriaRange,
    CellAddress? CopyToCell,
    bool UniqueRecordsOnly,
    GridRange? CopyToRange = null);

public sealed class AdvancedFilterDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly Func<string, SheetId?> _resolveSheetId;
    private readonly TextBox _listRangeBox = new();
    private readonly TextBox _criteriaRangeBox = new();
    private readonly TextBox _copyToBox = new();
    private readonly RadioButton _filterInPlaceButton = new() { Content = "_Filter the list, in-place", IsChecked = true };
    private readonly RadioButton _copyToAnotherLocationButton = new() { Content = "_Copy to another location" };
    private readonly CheckBox _uniqueBox = new() { Content = "_Unique records only" };
    private readonly TextBlock _copyToHint = new()
    {
        Text = "Copy to is available when Copy to another location is selected.",
        TextWrapping = TextWrapping.Wrap,
        Foreground = SystemColors.GrayTextBrush,
        Margin = new Thickness(0, 2, 0, 0)
    };

    public AdvancedFilterDialogResult? Result { get; private set; }

    public AdvancedFilterDialog(SheetId sheetId, string defaultListRange, Func<string, SheetId?>? resolveSheetId = null)
    {
        _sheetId = sheetId;
        _resolveSheetId = resolveSheetId ?? (_ => null);
        Title = "Advanced Filter";
        Width = 420;
        Height = 340;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _listRangeBox.Text = defaultListRange;
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(root, Dock.Top);

        var content = new StackPanel();
        root.Children.Add(content);

        content.Children.Add(new TextBlock
        {
            Text = "Action",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var actionGroup = new GroupBox { Header = "Action", Margin = new Thickness(0, 0, 0, 10) };
        var actionPanel = new StackPanel { Margin = new Thickness(8, 6, 8, 8) };
        _filterInPlaceButton.Margin = new Thickness(0, 0, 0, 4);
        _copyToAnotherLocationButton.Margin = new Thickness(0, 0, 0, 0);
        _filterInPlaceButton.Checked += (_, _) => UpdateCopyToState();
        _copyToAnotherLocationButton.Checked += (_, _) => UpdateCopyToState();
        actionPanel.Children.Add(_filterInPlaceButton);
        actionPanel.Children.Add(_copyToAnotherLocationButton);
        actionGroup.Content = actionPanel;
        content.Children.Add(actionGroup);

        var rangesGrid = new Grid();
        rangesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rangesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddReferenceRow(rangesGrid, 0, "_List range:", _listRangeBox, "Select list range");
        AddReferenceRow(rangesGrid, 1, "_Criteria range:", _criteriaRangeBox, "Select criteria range");
        AddReferenceRow(rangesGrid, 2, "Copy _to:", _copyToBox, "Select copy-to cell");
        content.Children.Add(rangesGrid);
        content.Children.Add(_copyToHint);

        _uniqueBox.Margin = new Thickness(0, 10, 0, 0);
        content.Children.Add(_uniqueBox);
        content.Children.Add(new TextBlock
        {
            Text = "Criteria should include column labels in the first row, matching Excel Advanced Filter.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 10, 0, 0)
        });
        content.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 76, rowMargin: new Thickness(0, 14, 0, 0)));
        Content = root;
        UpdateCopyToState();
    }

    public static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        bool uniqueRecordsOnly,
        Func<string, SheetId?>? resolveSheetId,
        out AdvancedFilterDialogResult result,
        out string? error)
    {
        result = default!;
        error = null;
        resolveSheetId ??= _ => null;

        if (!AdvancedFilterInputParser.TryParseRange(currentSheetId, listRangeText, resolveSheetId, out var listRange))
        {
            error = "Enter a valid list range.";
            return false;
        }

        if (!AdvancedFilterInputParser.TryParseRange(currentSheetId, criteriaRangeText, resolveSheetId, out var criteriaRange))
        {
            error = "Enter a valid criteria range.";
            return false;
        }

        if (!AdvancedFilterInputParser.TryParseCopyDestinationRange(copyToCellText ?? "", currentSheetId, out var copyToRange))
        {
            error = "Enter a valid copy-to cell or one-row header range.";
            return false;
        }

        result = new AdvancedFilterDialogResult(listRange, criteriaRange, copyToRange?.Start, uniqueRecordsOnly, copyToRange);
        return true;
    }

    public static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        bool uniqueRecordsOnly,
        out AdvancedFilterDialogResult result,
        out string? error) =>
        TryParse(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToCellText,
            uniqueRecordsOnly,
            resolveSheetId: null,
            out result,
            out error);

    public static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        bool copyToAnotherLocation,
        bool uniqueRecordsOnly,
        Func<string, SheetId?>? resolveSheetId,
        out AdvancedFilterDialogResult result,
        out string? error) =>
        TryParse(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToAnotherLocation ? copyToCellText : "",
            uniqueRecordsOnly,
            resolveSheetId,
            out result,
            out error);

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
            resolveSheetId: null,
            out result,
            out error);

    private static DockPanel CreateReferenceEditor(TextBox textBox, string automationName) =>
        DialogReferencePicker.CreateEditor(textBox, automationName);

    private static void AddReferenceRow(Grid grid, int row, string label, TextBox textBox, string automationName)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelBlock = new Label
        {
            Content = label,
            Target = textBox,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(0, row == 0 ? 0 : 8, 8, 0)
        };
        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);

        var editor = CreateReferenceEditor(textBox, automationName);
        editor.Margin = new Thickness(0, row == 0 ? 0 : 8, 0, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
    }

    private void UpdateCopyToState()
    {
        _copyToBox.IsEnabled = _copyToAnotherLocationButton.IsChecked == true;
        _copyToHint.Visibility = _copyToAnotherLocationButton.IsChecked == true
            ? Visibility.Collapsed
            : Visibility.Visible;
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
                _resolveSheetId,
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
