using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public sealed class CreateNamesFromSelectionDialog : Window
{
    private readonly CheckBox _topRow = new() { Content = "_Top row", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _leftColumn = new() { Content = "_Left column", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _bottomRow = new() { Content = "_Bottom row", Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _rightColumn = new() { Content = "_Right column", Margin = new Thickness(0, 4, 0, 0) };

    public bool UseTopRow => _topRow.IsChecked == true;
    public bool UseLeftColumn => _leftColumn.IsChecked == true;
    public bool UseBottomRow => _bottomRow.IsChecked == true;
    public bool UseRightColumn => _rightColumn.IsChecked == true;

    public CreateNamesFromSelectionDialog()
    {
        Title = "Create Names from Selection";
        Width = 280;
        Height = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Create names from values in the:",
            Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(_topRow);
        root.Children.Add(_leftColumn);
        root.Children.Add(_bottomRow);
        root.Children.Add(_rightColumn);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var ok = new Button { Content = "_OK", IsDefault = true, Width = 76, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "_Cancel", IsCancel = true, Width = 76 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
    }
}
