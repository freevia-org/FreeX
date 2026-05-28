using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

public sealed class AddWatchDialog : Window
{
    private readonly TextBox _rangeBox = new();

    public AddWatchDialog(string selectedRangeText)
    {
        Title = "Add Watch";
        Width = 360;
        Height = 170;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(12) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var add = new Button { Content = "_Add", Width = 76, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(add);
        buttons.Children.Add(new Button { Content = "_Cancel", Width = 76, IsCancel = true });

        var body = new StackPanel();
        root.Children.Add(body);
        _rangeBox.Text = selectedRangeText;
        _rangeBox.IsReadOnly = true;
        _rangeBox.Margin = new Thickness(0, 0, 0, 8);
        AutomationProperties.SetName(_rangeBox, "Selected range");
        body.Children.Add(new Label
        {
            Content = "Selected _range:",
            Target = _rangeBox,
            Padding = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        body.Children.Add(_rangeBox);
        body.Children.Add(new TextBlock
        {
            Text = "The selected cells will be added to the Watch Window.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush
        });

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _rangeBox.Focus();
        _rangeBox.SelectAll();
        Keyboard.Focus(_rangeBox);
    }
}
