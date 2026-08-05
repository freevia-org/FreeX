using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;

namespace FreeP.App.Avalonia;

internal static class ChartOptionsDialogChrome
{
    private static readonly AvaloniaCompactDialogChromeStyle Style = new(FontFamily.Default);

    public static Grid CreateRow(string label, Control control, double labelWidth = 150)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(labelWidth, GridUnitType.Pixel),
                new ColumnDefinition(1, GridUnitType.Star),
            },
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    public static Grid CreateValueModeRow(
        string label,
        Control value,
        Control mode,
        double labelWidth,
        double valueWidth)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(labelWidth, GridUnitType.Pixel),
                new ColumnDefinition(valueWidth, GridUnitType.Pixel),
                new ColumnDefinition(1, GridUnitType.Star),
            },
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        Grid.SetColumn(mode, 2);
        row.Children.Add(mode);
        return row;
    }

    public static StackPanel CreateActionRow(
        string acceptLabel,
        Action accept,
        string cancelLabel,
        Action cancel)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
        };
        row.Children.Add(CreateButton(acceptLabel, isDefault: true, accept));
        row.Children.Add(CreateButton(cancelLabel, isDefault: false, cancel));
        return row;
    }

    private static Button CreateButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, Style, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
