using System.Windows;

namespace Freexcel.App.Host;

public sealed class KeyboardCommandDispatcher
{
    private readonly Dictionary<KeyboardCommandShortcut, Action<object, RoutedEventArgs>> _handlers = [];

    public IReadOnlyCollection<KeyboardCommandShortcut> RegisteredShortcuts => _handlers.Keys.ToArray();

    public void Register(KeyboardCommandShortcut shortcut, Action<object, RoutedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!_handlers.TryAdd(shortcut, handler))
            throw new InvalidOperationException($"A keyboard command route is already registered for {shortcut}.");
    }

    public bool TryExecute(KeyboardCommandShortcut shortcut, object sender, RoutedEventArgs args)
    {
        if (!_handlers.TryGetValue(shortcut, out var handler))
            return false;

        handler(sender, args);
        return true;
    }
}
