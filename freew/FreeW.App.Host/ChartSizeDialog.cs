using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FreeW.App.Host;

/// <summary>
/// Modal prompt for a chart's width and height in points.
/// Returns a record on OK, or null on cancel.
/// </summary>
internal sealed class ChartSizeDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;
    private (double WidthPt, double HeightPt)? _result;

    private ChartSizeDialog(Window? owner, double widthPt, double heightPt)
    {
        Owner = owner;
        Title = "Chart Size";
        Width = 300;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _widthBox = new TextBox
        {
            Text = widthPt.ToString("0.##", CultureInfo.CurrentCulture),
            MinWidth = 120
        };
        _heightBox = new TextBox
        {
            Text = heightPt.ToString("0.##", CultureInfo.CurrentCulture),
            MinWidth = 120
        };

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Width (pt):", _widthBox);
        AddRow(grid, 1, "Height (pt):", _heightBox);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 2); Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        DialogFocus.FocusAndSelect(_widthBox);
    }

    private static void AddRow(Grid grid, int row, string label, TextBox box)
    {
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) };
        Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);
        Grid.SetRow(box, row); Grid.SetColumn(box, 1);
        box.Margin = new Thickness(0, 0, 0, 6);
        grid.Children.Add(box);
    }

    private void Accept()
    {
        if (!double.TryParse(_widthBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var w) || w <= 0)
        {
            DialogMessageHelper.ShowWarning(this, "Enter a positive width in points.");
            return;
        }
        if (!double.TryParse(_heightBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var h) || h <= 0)
        {
            DialogMessageHelper.ShowWarning(this, "Enter a positive height in points.");
            return;
        }
        _result = (w, h);
        Close();
    }

    public static (double WidthPt, double HeightPt)? Prompt(Window? owner, double widthPt, double heightPt)
    {
        var dialog = new ChartSizeDialog(owner, widthPt, heightPt);
        dialog.ShowDialog();
        return dialog._result;
    }
}
