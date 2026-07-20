using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Free.Shared.Shell.Avalonia;

/// <summary>Shared owned modal message surface for Avalonia sister applications.</summary>
public sealed class AvaloniaUserMessageDialog : Window
{
    private readonly Button _okButton;

    private AvaloniaUserMessageDialog(string message, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        MinHeight = 150;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _okButton = new Button
        {
            Content = "OK",
            MinWidth = 82,
            IsDefault = true,
            IsCancel = true,
        };
        _okButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(16, 16, 16, 20),
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(16, 0, 16, 16),
                    Children = { _okButton },
                },
            },
        };

        Opened += (_, _) => _okButton.Focus();
    }

    public static async Task ShowErrorAsync(Window owner, string message, string title = "Error")
    {
        ArgumentNullException.ThrowIfNull(owner);
        var dialog = new AvaloniaUserMessageDialog(message, title);
        await dialog.ShowDialog(owner);
    }

    public static AvaloniaUserMessageDialog CreateForTests(string message, string title = "Error") =>
        new(message, title);
}
