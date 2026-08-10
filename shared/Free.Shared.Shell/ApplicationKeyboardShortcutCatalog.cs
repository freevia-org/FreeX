namespace Free.Shared.Shell;

public readonly record struct ApplicationKeyboardShortcut<TCommand, TKey, TModifiers>(
    TCommand Command,
    TKey Key,
    TModifiers Modifiers)
    where TCommand : struct, Enum
    where TKey : struct, Enum
    where TModifiers : struct, Enum;

/// <summary>
/// Resolves renderer-neutral application commands from product-owned key and modifier enums.
/// Native key conversion and product command execution remain in each application adapter.
/// </summary>
public sealed class ApplicationKeyboardShortcutCatalog<TCommand, TKey, TModifiers>
    where TCommand : struct, Enum
    where TKey : struct, Enum
    where TModifiers : struct, Enum
{
    private readonly ApplicationKeyboardShortcut<TCommand, TKey, TModifiers>[] _shortcuts;

    public ApplicationKeyboardShortcutCatalog(
        IEnumerable<ApplicationKeyboardShortcut<TCommand, TKey, TModifiers>> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        _shortcuts = shortcuts.ToArray();
    }

    public IReadOnlyList<ApplicationKeyboardShortcut<TCommand, TKey, TModifiers>> All => _shortcuts;

    public TCommand? Resolve(TKey key, TModifiers modifiers) =>
        TryResolve(key, modifiers, out var command)
            ? command
            : null;

    public bool TryResolve(TKey key, TModifiers modifiers, out TCommand command)
    {
        foreach (var shortcut in _shortcuts)
        {
            if (!EqualityComparer<TKey>.Default.Equals(shortcut.Key, key) ||
                !EqualityComparer<TModifiers>.Default.Equals(shortcut.Modifiers, modifiers))
            {
                continue;
            }

            command = shortcut.Command;
            return true;
        }

        command = default;
        return false;
    }

    public bool TryDispatch(
        TKey key,
        TModifiers modifiers,
        Action<TCommand> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        if (!TryResolve(key, modifiers, out var command))
            return false;

        dispatch(command);
        return true;
    }
}
