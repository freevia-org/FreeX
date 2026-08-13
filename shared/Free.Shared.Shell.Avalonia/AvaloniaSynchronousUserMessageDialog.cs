using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Realizes the uncommon synchronous user-message contract required by renderer-neutral callers.
/// Avalonia has no blocking owned-dialog API, so this helper owns the single nested dispatcher pump
/// used by sister applications that cannot change a synchronous resolver to an asynchronous one.
/// </summary>
public sealed class AvaloniaSynchronousUserMessageDialog : Window
{
    private readonly Button _defaultButton;
    private bool _completed;

    private AvaloniaSynchronousUserMessageDialog(
        UserMessageRequest request,
        UserMessageResult dismissedResult)
    {
        ArgumentNullException.ThrowIfNull(request);

        MessageButtons = request.Buttons;
        MessageIcon = request.Kind;
        DismissedResult = dismissedResult;
        Result = dismissedResult;

        Title = request.Title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        MinHeight = 150;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var messageText = new TextBlock
        {
            Text = request.Message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 20),
        };

        var actionButtons = CreateActionButtons(request.Buttons);
        _defaultButton = actionButtons.Single(button => button.IsDefault);

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16),
        };
        foreach (var button in actionButtons)
            actionRow.Children.Add(button);

        Content = new StackPanel { Children = { messageText, actionRow } };
        Opened += (_, _) => _defaultButton.Focus();
        Closed += (_, _) => _completed = true;
    }

    public UserMessageButtons MessageButtons { get; }

    /// <summary>
    /// Typed severity retained for renderer-neutral parity. The four legacy synchronous Avalonia
    /// surfaces were text-only, so this exact-compatibility realizer intentionally draws no icon.
    /// </summary>
    public UserMessageIcon MessageIcon { get; }

    public UserMessageResult DismissedResult { get; }

    internal UserMessageResult Result { get; private set; }

    /// <summary>Shows an owned message and returns only after the synchronous resolver is satisfied.</summary>
    public static UserMessageResult ShowMessage(
        Window owner,
        UserMessageRequest request,
        UserMessageResult dismissedResult)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);

        var dialog = new AvaloniaSynchronousUserMessageDialog(request, dismissedResult);
        AvaloniaSynchronousDialogHost.Show(owner, dialog, () => dialog._completed);
        return dialog.Result;
    }

    internal static AvaloniaSynchronousUserMessageDialog CreateForTests(
        UserMessageRequest request,
        UserMessageResult dismissedResult) =>
        new(request, dismissedResult);

    private IReadOnlyList<Button> CreateActionButtons(UserMessageButtons buttons)
    {
        MessageButtonPlan[] plans = buttons switch
        {
            UserMessageButtons.Ok =>
            [new(ShellStrings.Current.Ok, UserMessageResult.Ok, IsDefault: true)],
            UserMessageButtons.OkCancel =>
            [
                new(ShellStrings.Current.Ok, UserMessageResult.Ok, IsDefault: true),
                new(ShellStrings.Current.Cancel, UserMessageResult.Cancel),
            ],
            UserMessageButtons.YesNo =>
            [
                new(ShellStrings.Current.Yes, UserMessageResult.Yes, IsDefault: true),
                new(ShellStrings.Current.No, UserMessageResult.No),
            ],
            UserMessageButtons.YesNoCancel =>
            [
                new(ShellStrings.Current.Yes, UserMessageResult.Yes, IsDefault: true),
                new(ShellStrings.Current.No, UserMessageResult.No),
                new(ShellStrings.Current.Cancel, UserMessageResult.Cancel),
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
            IsCancel = plan.Result == DismissedResult,
            Margin = new Thickness(8, 0, 0, 0),
        };
        AvaloniaDialogButtonContent.Apply(button, plan.Label);
        button.Click += (_, _) => Complete(plan.Result);
        return button;
    }

    private void Complete(UserMessageResult result)
    {
        Result = result;
        _completed = true;
        Close();
    }

    private sealed record MessageButtonPlan(
        string Label,
        UserMessageResult Result,
        bool IsDefault = false);
}
