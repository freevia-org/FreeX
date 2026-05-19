using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

internal static class DialogButtonRowFactory
{
    public static StackPanel Create(Action accept, double buttonWidth, Thickness rowMargin = default)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = rowMargin
        };
        var ok = new Button
        {
            Content = "OK",
            Width = buttonWidth,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        ok.Click += (_, _) => accept();
        row.Children.Add(ok);
        row.Children.Add(new Button
        {
            Content = "Cancel",
            Width = buttonWidth,
            IsCancel = true
        });
        return row;
    }
}
