using System.Diagnostics.CodeAnalysis;

namespace Free.Shared.AppServices;

public enum UserMessageButtons
{
    Ok,
    OkCancel,
    YesNo,
    YesNoCancel
}

public enum UserMessageIcon
{
    None,
    Information,
    Warning,
    Error,
    Question
}

public enum UserMessageResult
{
    None,
    Ok,
    Cancel,
    Yes,
    No
}

/// <summary>
/// Renderer-neutral ownership token for a modal user message. A default token asks the
/// toolkit adapter to use its configured application owner; an explicit token carries the
/// native owner without exposing that toolkit type from AppServices.
/// </summary>
public readonly record struct UserMessageOwner
{
    private UserMessageOwner(object nativeOwner)
    {
        NativeOwner = nativeOwner;
    }

    public object? NativeOwner { get; }

    public bool IsDefault => NativeOwner is null;

    public static UserMessageOwner FromNative(object nativeOwner) =>
        new(nativeOwner ?? throw new ArgumentNullException(nameof(nativeOwner)));

    public bool TryGetNativeOwner<TNativeOwner>([NotNullWhen(true)] out TNativeOwner? owner)
        where TNativeOwner : class
    {
        owner = NativeOwner as TNativeOwner;
        return owner is not null;
    }
}

/// <summary>Portable request consumed by synchronous or asynchronous message realizers.</summary>
public sealed record UserMessageRequest
{
    public UserMessageRequest(
        string? message,
        string title,
        UserMessageButtons buttons,
        UserMessageIcon kind,
        UserMessageOwner owner = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (!Enum.IsDefined(buttons))
            throw new ArgumentOutOfRangeException(nameof(buttons), buttons, null);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);

        Message = message ?? string.Empty;
        Title = title;
        Buttons = buttons;
        Kind = kind;
        Owner = owner;
    }

    public string Message { get; }
    public string Title { get; }
    public UserMessageButtons Buttons { get; }
    public UserMessageIcon Kind { get; }
    public UserMessageOwner Owner { get; }
}
