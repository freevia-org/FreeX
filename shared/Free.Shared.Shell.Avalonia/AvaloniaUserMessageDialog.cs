using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

/// <summary>Shared owned modal message surface for Avalonia sister applications.</summary>
public sealed class AvaloniaUserMessageDialog : Window
{
    private readonly Button _okButton;

    private AvaloniaUserMessageDialog(string message, string title, UserMessageIcon icon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        MessageIcon = icon;
        Title = ResolveKnownDefaultTitle(title, icon);
        Width = 420;
        SizeToContent = SizeToContent.Height;
        MinHeight = 150;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AvaloniaCompactDialogChrome.ApplyWindow(this);

        _okButton = new Button
        {
            MinWidth = 82,
            IsDefault = true,
            IsCancel = true,
        };
        AvaloniaCompactDialogChrome.ApplyButton(_okButton, AvaloniaCompactDialogChrome.WindowsStyle, 82, isDefault: true);
        AvaloniaDialogButtonContent.Apply(_okButton, ShellStrings.Current.Ok);
        _okButton.Click += (_, _) => Close();

        var severityIcon = CreateSeverityIcon(icon);
        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var messageRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("32,12,*"),
            Margin = new Thickness(16, 16, 16, 20),
        };
        Grid.SetColumn(severityIcon, 0);
        Grid.SetColumn(messageText, 2);
        messageRow.Children.Add(severityIcon);
        messageRow.Children.Add(messageText);

        Content = new StackPanel
        {
            Children =
            {
                messageRow,
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

    public UserMessageIcon MessageIcon { get; }

    public static Task ShowErrorAsync(Window owner, string message, string title = "Error") =>
        ShowAsync(owner, message, title, UserMessageIcon.Error);

    public static Task ShowWarningAsync(Window owner, string message, string title = "Warning") =>
        ShowAsync(owner, message, title, UserMessageIcon.Warning);

    public static async Task ShowAsync(
        Window owner,
        string message,
        string title,
        UserMessageIcon icon)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var dialog = new AvaloniaUserMessageDialog(message, title, icon);
        await dialog.ShowDialog(owner);
    }

    public static AvaloniaUserMessageDialog CreateForTests(
        string message,
        string title = "Error",
        UserMessageIcon icon = UserMessageIcon.Error) =>
        new(message, title, icon);

    private static Control CreateSeverityIcon(UserMessageIcon icon)
    {
        var glyph = new TextBlock
        {
            Text = icon switch
            {
                UserMessageIcon.Warning => "!",
                UserMessageIcon.Error => "X",
                _ => string.Empty,
            },
            Foreground = Brushes.White,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(glyph, "MessageSeverityIcon");
        AutomationProperties.SetName(glyph, icon switch
        {
            UserMessageIcon.Warning => "Warning",
            UserMessageIcon.Error => "Error",
            _ => string.Empty,
        });

        return new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = icon switch
            {
                UserMessageIcon.Warning => new SolidColorBrush(Color.FromRgb(226, 144, 0)),
                UserMessageIcon.Error => new SolidColorBrush(Color.FromRgb(196, 43, 28)),
                _ => Brushes.Transparent,
            },
            IsVisible = icon is UserMessageIcon.Warning or UserMessageIcon.Error,
            Child = glyph,
        };
    }

    private static string ResolveKnownDefaultTitle(string title, UserMessageIcon icon) =>
        icon switch
        {
            UserMessageIcon.Error when string.Equals(title, "Error", StringComparison.Ordinal) => ShellStrings.Current.ErrorTitle,
            UserMessageIcon.Warning when string.Equals(title, "Warning", StringComparison.Ordinal) => ShellStrings.Current.WarningTitle,
            _ => title,
        };
}
