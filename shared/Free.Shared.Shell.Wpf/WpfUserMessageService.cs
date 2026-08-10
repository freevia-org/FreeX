using System.Windows;
using Free.Shared.AppServices;

namespace Free.Shared.Shell;

/// <summary>
/// Production WPF implementation of <see cref="IUserMessageService"/>.
/// </summary>
public sealed class WpfUserMessageService : IUserMessageService
{
    private const string DefaultErrorTitle = "Error";
    private const string DefaultWarningTitle = "Warning";
    private const string DefaultInformationTitle = "Information";
    private const string DefaultConfirmTitle = "Confirm";

    private readonly Func<Window?> _defaultOwnerResolver;
    private readonly Func<Window?, UserMessageRequest, UserMessageResult> _showMessage;

    public WpfUserMessageService()
        : this(
            () => Application.Current?.MainWindow,
            static (owner, request) => DialogMessageHelper.ShowMessage(
                owner,
                request.Message,
                request.Title,
                request.Buttons,
                request.Kind))
    {
    }

    public WpfUserMessageService(Window owner)
        : this(
            () => owner ?? throw new ArgumentNullException(nameof(owner)),
            static (messageOwner, request) => DialogMessageHelper.ShowMessage(
                messageOwner,
                request.Message,
                request.Title,
                request.Buttons,
                request.Kind))
    {
    }

    internal WpfUserMessageService(
        Func<Window?> defaultOwnerResolver,
        Func<Window?, UserMessageRequest, UserMessageResult> showMessage)
    {
        _defaultOwnerResolver = defaultOwnerResolver
            ?? throw new ArgumentNullException(nameof(defaultOwnerResolver));
        _showMessage = showMessage ?? throw new ArgumentNullException(nameof(showMessage));
    }

    public void ShowError(string message, string title = DefaultErrorTitle)
        => ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Error);

    public void ShowWarning(string message, string title = DefaultWarningTitle)
        => ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Warning);

    public void ShowInfo(string message, string title = DefaultInformationTitle)
        => ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Information);

    public bool AskYesNo(string message, string title = DefaultConfirmTitle)
        => ShowMessage(message, title, UserMessageButtons.YesNo, UserMessageIcon.Question) == UserMessageResult.Yes;

    public UserMessageResult ShowMessage(
        string message,
        string title,
        UserMessageButtons buttons,
        UserMessageIcon icon)
        => ShowMessageCore(new UserMessageRequest(message, title, buttons, icon));

    public ValueTask<UserMessageResult> ShowMessageAsync(
        UserMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ShowMessageCore(request));
    }

    private UserMessageResult ShowMessageCore(UserMessageRequest request) =>
        _showMessage(ResolveOwner(request.Owner), request);

    private Window? ResolveOwner(UserMessageOwner owner)
    {
        if (owner.IsDefault)
            return _defaultOwnerResolver();

        if (owner.TryGetNativeOwner<Window>(out var window))
            return window;

        throw new ArgumentException(
            "The explicit user-message owner is not a WPF Window.",
            nameof(owner));
    }
}
