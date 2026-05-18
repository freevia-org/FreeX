using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed class PivotTableDialog : Window
{
    public PivotTableDialog(Workbook workbook, SheetId sourceSheetId, GridRange sourceRange)
    {
        Title = "Create PivotTable";
        Width = 380; Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(16) };

        stack.Children.Add(new TextBlock
        {
            Text = $"Source range: {sourceRange.Start.ToA1()}:{sourceRange.End.ToA1()}",
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.Bold
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Freexcel creates a worksheet-range PivotTable and opens the PivotTable Fields pane for Excel-style layout editing.\n\n" +
                   "Cloud BI/data-model PivotTables remain excluded from the local workbook feature set.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var ok = new Button { Content = "Create", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => { DialogResult = true; };
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        stack.Children.Add(btnRow);

        Content = stack;
    }
}
