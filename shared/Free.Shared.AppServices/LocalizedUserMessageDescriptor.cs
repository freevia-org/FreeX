using Free.Shared.Localization;

namespace Free.Shared.AppServices;

/// <summary>
/// Renderer-neutral message semantics whose text is resolved by the consuming app.
/// </summary>
public sealed record LocalizedUserMessageDescriptor
{
    public LocalizedUserMessageDescriptor(
        LocalizedTextDescriptor message,
        LocalizedTextDescriptor title,
        UserMessageButtons buttons,
        UserMessageIcon icon)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(title);
        if (!Enum.IsDefined(buttons))
            throw new ArgumentOutOfRangeException(nameof(buttons), buttons, null);
        if (!Enum.IsDefined(icon))
            throw new ArgumentOutOfRangeException(nameof(icon), icon, null);

        Message = message;
        Title = title;
        Buttons = buttons;
        Icon = icon;
    }

    public LocalizedTextDescriptor Message { get; }
    public LocalizedTextDescriptor Title { get; }
    public UserMessageButtons Buttons { get; }
    public UserMessageIcon Icon { get; }

    public UserMessageRequest Resolve(
        Func<string, string> getText,
        Func<string, object?[], string> formatText,
        UserMessageOwner owner = default) =>
        new(
            Message.Resolve(getText, formatText),
            Title.Resolve(getText, formatText),
            Buttons,
            Icon,
            owner);
}
