using System;
using System.Windows;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void UpdateMaximizedContentInset()
    {
        if (RootGrid is null)
            return;

        RootGrid.Margin = WindowState == WindowState.Maximized
            ? GetMaximizedSafeInset()
            : new Thickness(0);
    }

    private static Thickness GetMaximizedSafeInset()
    {
        var resize = SystemParameters.WindowResizeBorderThickness;
        var inset = Math.Ceiling(Math.Max(
            MaximizedSafeInsetDip,
            Math.Max(
                Math.Max(resize.Left, resize.Right),
                Math.Max(resize.Top, resize.Bottom))));

        return new Thickness(inset);
    }

    private void UndoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteUndo();

    private void RedoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteRedo();
}
