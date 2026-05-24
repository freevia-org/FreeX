using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        var group = new GroupBox { Header = "Create names from", Margin = new Thickness(0, 0, 0, 10) };
        var options = new StackPanel { Margin = new Thickness(8, 4, 8, 8) };
        options.Children.Add(_topRow);
        options.Children.Add(_leftColumn);
        options.Children.Add(_bottomRow);
        options.Children.Add(_rightColumn);
        group.Content = options;
        root.Children.Add(group);
        root.Children.Add(new TextBlock
        {
            Text = "Excel creates named ranges from the selected row or column labels.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 0, 0, 10)
        });
        root.Children.Add(DialogButtonRowFactory.Create(() => DialogResult = true, buttonWidth: 76));

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _topRow.Focus();
        Keyboard.Focus(_topRow);
    }
}
