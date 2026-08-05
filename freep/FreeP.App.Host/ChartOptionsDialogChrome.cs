using System.Windows;
using System.Windows.Controls;

namespace FreeP.App.Host;

internal static class ChartOptionsDialogChrome
{
    public static StackPanel CreateRow(string label, Control control, double labelWidth = 150)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        row.Children.Add(new Label
        {
            Content = label,
            Width = labelWidth,
            VerticalContentAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(control);
        return row;
    }

    public static StackPanel CreateValueModeRow(
        string valueLabel,
        Control value,
        double valueLabelWidth,
        string modeLabel,
        Control mode,
        double modeLabelWidth)
    {
        var row = CreateRow(valueLabel, value, valueLabelWidth);
        row.Children.Add(new Label
        {
            Content = modeLabel,
            Width = modeLabelWidth,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(mode);
        return row;
    }

    public static Grid CreateTrailingFieldRow(string label, Control control, double fieldWidth)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fieldWidth) });
        row.Children.Add(new Label { Content = label, Padding = new Thickness(0, 2, 8, 2) });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    public static StackPanel CreateActionRow(
        string acceptLabel,
        Action accept,
        string cancelLabel,
        Action cancel,
        Thickness rowMargin)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = rowMargin,
        };
        var ok = new Button
        {
            Content = acceptLabel,
            IsDefault = true,
            MinWidth = 80,
            Margin = new Thickness(4),
        };
        var cancelButton = new Button
        {
            Content = cancelLabel,
            IsCancel = true,
            MinWidth = 80,
            Margin = new Thickness(4),
        };
        ok.Click += (_, _) => accept();
        cancelButton.Click += (_, _) => cancel();
        row.Children.Add(ok);
        row.Children.Add(cancelButton);
        return row;
    }
}
