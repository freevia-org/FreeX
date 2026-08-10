using Avalonia.Controls;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia;

/// <summary>Asynchronous Avalonia adapter for the renderer-neutral user-message contract.</summary>
public sealed class AvaloniaUserMessageService : IUserMessageService
{
    private readonly Func<Window?> _defaultOwnerResolver;
    private readonly Func<Window, UserMessageRequest, CancellationToken, ValueTask<UserMessageResult>>
        _showMessageAsync;

    public AvaloniaUserMessageService(Window owner)
        : this(() => owner ?? throw new ArgumentNullException(nameof(owner)))
    {
    }

    public AvaloniaUserMessageService(Func<Window?> defaultOwnerResolver)
        : this(
            defaultOwnerResolver,
            static (owner, request, cancellationToken) =>
                AvaloniaUserMessageDialog.ShowMessageAsync(
                    owner,
                    request,
                    cancellationToken))
    {
    }

    internal AvaloniaUserMessageService(
        Func<Window?> defaultOwnerResolver,
        Func<Window, UserMessageRequest, CancellationToken, ValueTask<UserMessageResult>>
            showMessageAsync)
    {
        _defaultOwnerResolver = defaultOwnerResolver
            ?? throw new ArgumentNullException(nameof(defaultOwnerResolver));
        _showMessageAsync = showMessageAsync ?? throw new ArgumentNullException(nameof(showMessageAsync));
    }

    public ValueTask<UserMessageResult> ShowMessageAsync(
        UserMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return _showMessageAsync(ResolveOwner(request.Owner), request, cancellationToken);
    }

    private Window ResolveOwner(UserMessageOwner owner)
    {
        if (owner.IsDefault)
        {
            return _defaultOwnerResolver()
                ?? throw new InvalidOperationException(
                    "An Avalonia owner is required to show a modal user message.");
        }

        if (owner.TryGetNativeOwner<Window>(out var window) && window is not null)
            return window;

        throw new ArgumentException(
            "The explicit user-message owner is not an Avalonia Window.",
            nameof(owner));
    }
}
