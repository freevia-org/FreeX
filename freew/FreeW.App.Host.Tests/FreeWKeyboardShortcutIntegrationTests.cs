using System.Windows.Input;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Host.Tests;

public sealed class FreeWKeyboardShortcutIntegrationTests
{
    [StaFact]
    public void WpfWindowInstallsEverySharedCatalogGesture()
    {
        var window = new MainWindow(new FreeWOptions());
        try
        {
            var actual = window.InputBindings
                .OfType<KeyBinding>()
                .Where(binding => binding.Command is RoutedUICommand command &&
                                  command.Name.StartsWith("FreeW", StringComparison.Ordinal))
                .Select(binding => (KeyGesture)binding.Gesture)
                .Select(gesture => (gesture.Key, gesture.Modifiers))
                .ToArray();

            var expected = FreeWKeyboardShortcutCatalog.All
                .Select(shortcut => (ToWpfKey(shortcut.Key), ToWpfModifiers(shortcut.Modifiers)))
                .ToArray();

            actual.Should().HaveCount(expected.Length);
            actual.Should().BeEquivalentTo(expected);
        }
        finally
        {
            window.Close();
        }
    }

    private static Key ToWpfKey(FreeWKeyboardKey key) =>
        Enum.Parse<Key>(key.ToString());

    private static ModifierKeys ToWpfModifiers(FreeWKeyboardModifiers modifiers)
    {
        var result = ModifierKeys.None;
        if ((modifiers & FreeWKeyboardModifiers.Control) != 0)
            result |= ModifierKeys.Control;
        if ((modifiers & FreeWKeyboardModifiers.Shift) != 0)
            result |= ModifierKeys.Shift;
        if ((modifiers & FreeWKeyboardModifiers.Alt) != 0)
            result |= ModifierKeys.Alt;
        return result;
    }
}
