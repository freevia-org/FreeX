using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

/// <summary>Shared owned modal message surface for Avalonia sister applications.</summary>
public sealed class AvaloniaUserMessageDialog : AvaloniaDialogWindow
{
    private readonly Button _defaultButton;

    private AvaloniaUserMessageDialog(UserMessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        MessageIcon = request.Kind;
        MessageButtons = request.Buttons;
        Title = ResolveKnownDefaultTitle(request.Title, request.Kind);
        Width = 420;
        SizeToContent = SizeToContent.Height;
        MinHeight = 150;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var actionButtons = CreateActionButtons(request.Buttons);
        _defaultButton = actionButtons.Single(button => button.IsDefault);

        var severityIcon = CreateSeverityIcon(request.Kind);
        var messageText = new TextBlock
        {
            Text = request.Message,
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

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16),
            Spacing = 8,
        };
        foreach (var button in actionButtons)
            actionRow.Children.Add(button);

        Content = new StackPanel
        {
            Children =
            {
                messageRow,
                actionRow,
            },
        };

        Opened += (_, _) => _defaultButton.Focus();
    }

    public UserMessageIcon MessageIcon { get; }
    public UserMessageButtons MessageButtons { get; }

    public static async Task ShowErrorAsync(Window owner, string message, string title = "Error")
    {
        _ = await ShowMessageAsync(
            owner,
            new UserMessageRequest(message, title, UserMessageButtons.Ok, UserMessageIcon.Error));
    }

    public static async Task ShowWarningAsync(Window owner, string message, string title = "Warning")
    {
        _ = await ShowMessageAsync(
            owner,
            new UserMessageRequest(message, title, UserMessageButtons.Ok, UserMessageIcon.Warning));
    }

    public static async Task ShowAsync(
        Window owner,
        string message,
        string title,
        UserMessageIcon icon)
    {
        _ = await ShowMessageAsync(
            owner,
            new UserMessageRequest(message, title, UserMessageButtons.Ok, icon));
    }

    public static async ValueTask<UserMessageResult> ShowMessageAsync(
        Window owner,
        UserMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new AvaloniaUserMessageDialog(request);
        using var registration = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(() =>
            {
                if (dialog.IsVisible)
                    dialog.Close(UserMessageResult.Cancel);
            }));
        return await dialog.ShowDialog<UserMessageResult>(owner);
    }

    public static AvaloniaUserMessageDialog CreateForTests(
        string message,
        string title = "Error",
        UserMessageIcon icon = UserMessageIcon.Error,
        UserMessageButtons buttons = UserMessageButtons.Ok) =>
        new(new UserMessageRequest(message, title, buttons, icon));

    private IReadOnlyList<Button> CreateActionButtons(UserMessageButtons buttons)
    {
        MessageButtonPlan[] plans = buttons switch
        {
            UserMessageButtons.Ok =>
            [new MessageButtonPlan(ShellStrings.Current.Ok, UserMessageResult.Ok, true, true)],
            UserMessageButtons.OkCancel =>
            [
                new MessageButtonPlan(ShellStrings.Current.Ok, UserMessageResult.Ok, true, false),
                new MessageButtonPlan(ShellStrings.Current.Cancel, UserMessageResult.Cancel, false, true),
            ],
            UserMessageButtons.YesNo =>
            [
                new MessageButtonPlan(ShellStrings.Current.Yes, UserMessageResult.Yes, true, false),
                new MessageButtonPlan(ShellStrings.Current.No, UserMessageResult.No, false, false),
            ],
            UserMessageButtons.YesNoCancel =>
            [
                new MessageButtonPlan(ShellStrings.Current.Yes, UserMessageResult.Yes, true, false),
                new MessageButtonPlan(ShellStrings.Current.No, UserMessageResult.No, false, false),
                new MessageButtonPlan(ShellStrings.Current.Cancel, UserMessageResult.Cancel, false, true),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(buttons), buttons, null),
        };

        return plans.Select(CreateActionButton).ToArray();
    }

    private Button CreateActionButton(MessageButtonPlan plan)
    {
        var button = new Button
        {
            MinWidth = 82,
            IsDefault = plan.IsDefault,
            IsCancel = plan.IsCancel,
        };
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            AvaloniaCompactDialogChrome.WindowsStyle,
            82,
            plan.IsDefault);
        AvaloniaDialogButtonContent.Apply(button, plan.Label);
        button.Click += (_, _) => Close(plan.Result);
        return button;
    }

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
            UserMessageIcon.Information when string.Equals(title, "Information", StringComparison.Ordinal) => ShellStrings.Current.InformationTitle,
            UserMessageIcon.Question when string.Equals(title, "Confirm", StringComparison.Ordinal) => ShellStrings.Current.ConfirmTitle,
            _ => title,
        };

    private sealed record MessageButtonPlan(
        string Label,
        UserMessageResult Result,
        bool IsDefault,
        bool IsCancel);
}
