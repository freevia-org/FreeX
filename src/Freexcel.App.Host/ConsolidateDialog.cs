using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record ConsolidateDialogResult(IReadOnlyList<GridRange> SourceRanges, CellAddress DestinationCell);

public sealed class ConsolidateDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _sourcesBox = new();
    private readonly TextBox _destinationBox = new();

    public ConsolidateDialogResult? Result { get; private set; }

    public ConsolidateDialog(SheetId sheetId, string defaultSource, string defaultDestination)
    {
        _sheetId = sheetId;
        Title = "Consolidate";
        Width = 380;
        Height = 210;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _sourcesBox.Text = defaultSource;
        _destinationBox.Text = defaultDestination;
        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = "Source ranges (semicolon separated):" });
        root.Children.Add(_sourcesBox);
        root.Children.Add(new TextBlock { Text = "Destination cell:", Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(_destinationBox);
        root.Children.Add(TextToColumnsDialog.CreateButtonRow(Accept));
        Content = root;
    }

    public static ConsolidateDialogResult CreateResult(IEnumerable<GridRange> sourceRanges, CellAddress destinationCell)
    {
        var ranges = sourceRanges.ToList();
        if (ranges.Count == 0)
            throw new ArgumentException("At least one source range is required.", nameof(sourceRanges));

        return new ConsolidateDialogResult(ranges, destinationCell);
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

    private void Accept()
    {
        try
        {
            var ranges = _sourcesBox.Text
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(text => GridRange.Parse(text, _sheetId))
                .ToList();
            if (!HaveSameSize(ranges))
            {
                MessageBox.Show(this, "Source ranges must be the same size.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = CreateResult(ranges, CellAddress.Parse(_destinationBox.Text, _sheetId));
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
