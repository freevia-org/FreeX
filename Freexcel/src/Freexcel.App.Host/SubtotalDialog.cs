using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public sealed record SubtotalDialogResult(
    uint GroupColumnOffset,
    IReadOnlyList<uint> SubtotalColumnOffsets,
    int FunctionNumber,
    bool ReplaceCurrentSubtotals,
    bool PageBreakBetweenGroups,
    bool SummaryBelowData);

public sealed class SubtotalDialog : Window
{
    private readonly TextBox _groupColumnBox = new() { Text = "0" };
    private readonly TextBox _subtotalColumnsBox = new() { Text = "1" };
    private readonly TextBox _functionBox = new() { Text = "sum" };
    private readonly CheckBox _replaceBox = new() { Content = "Replace current subtotals", IsChecked = true };
    private readonly CheckBox _pageBreakBox = new() { Content = "Page break between groups" };
    private readonly CheckBox _summaryBelowBox = new() { Content = "Summary below data", IsChecked = true };

    public SubtotalDialogResult? Result { get; private set; }

    public SubtotalDialog()
    {
        Title = "Subtotal";
        Width = 360;
        Height = 300;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = "At each change in column offset:" });
        root.Children.Add(_groupColumnBox);
        root.Children.Add(new TextBlock { Text = "Add subtotal to column offsets (comma separated):", Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(_subtotalColumnsBox);
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

    private void Accept()
    {
        if (!SubtotalDialogInputParser.TryParse(
                _groupColumnBox.Text,
                _subtotalColumnsBox.Text,
                _functionBox.Text,
                _replaceBox.IsChecked == true,
                _pageBreakBox.IsChecked == true,
                _summaryBelowBox.IsChecked == true,
                out var result,
                out var error))
        {
            MessageBox.Show(this, error ?? "Enter valid subtotal options.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
