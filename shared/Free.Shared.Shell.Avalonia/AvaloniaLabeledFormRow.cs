using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Free.Shared.Shell.Avalonia;

public static class AvaloniaLabeledFormRow
{
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
