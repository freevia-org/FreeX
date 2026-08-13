using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Free.Shared.Shell.Avalonia;

public static class AvaloniaLabeledFormRow
{
    public static Grid CreateCompactGrid(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < rows; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    public static void AddCompact(Grid grid, string label, Control field, int row)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(field);

        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, row == 0 ? 0 : 4, 8, 0),
        };
        Place(grid, labelText, row, 0);
        Place(grid, field, row, 1);
    }

    public static void Place(Grid grid, Control control, int row, int column)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(control);

        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    public static void Add(Grid grid, int row, string label, Control field, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(field);

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var labelText = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(labelText, row);
        Grid.SetColumn(labelText, 0);

        field.Margin = new Thickness(0, 4, 0, 4);

        Control value = field;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            value = new StackPanel
            {
                Children =
                {
                    field,
                    new TextBlock
                    {
                        Text = hint,
                        FontSize = 11,
                        Foreground = Brushes.Gray,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 4),
                    },
                },
            };
        }

        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);

        grid.Children.Add(labelText);
        grid.Children.Add(value);
    }
}
