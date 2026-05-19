using System.Windows;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class KeyboardCommandDispatcherTests
{
    [Fact]
    public void Execute_InvokesRegisteredShortcutWithOriginalEventContext()
    {
        var dispatcher = new KeyboardCommandDispatcher();
        var sender = new object();
        var args = new RoutedEventArgs();
        object? observedSender = null;
        RoutedEventArgs? observedArgs = null;

        dispatcher.Register(KeyboardCommandShortcut.SaveWorkbook, (s, e) =>
        {
            observedSender = s;
            observedArgs = e;
        });

        var executed = dispatcher.TryExecute(KeyboardCommandShortcut.SaveWorkbook, sender, args);

        executed.Should().BeTrue();
        observedSender.Should().BeSameAs(sender);
        observedArgs.Should().BeSameAs(args);
    }

    [Fact]
    public void TryExecute_ReturnsFalseWhenShortcutIsNotRegistered()
    {
        var dispatcher = new KeyboardCommandDispatcher();

        var executed = dispatcher.TryExecute(KeyboardCommandShortcut.SaveWorkbook, new object(), new RoutedEventArgs());

        executed.Should().BeFalse();
    }

    [Fact]
    public void Register_RejectsDuplicateShortcutRoutes()
    {
        var dispatcher = new KeyboardCommandDispatcher();
        dispatcher.Register(KeyboardCommandShortcut.SaveWorkbook, (_, _) => { });

        var act = () => dispatcher.Register(KeyboardCommandShortcut.SaveWorkbook, (_, _) => { });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SaveWorkbook*");
    }

    [Fact]
    public void RegisteredShortcuts_ReturnsRegisteredShortcutSnapshot()
    {
        var dispatcher = new KeyboardCommandDispatcher();

        dispatcher.Register(KeyboardCommandShortcut.SaveWorkbook, (_, _) => { });
        dispatcher.Register(KeyboardCommandShortcut.OpenWorkbook, (_, _) => { });

        dispatcher.RegisteredShortcuts.Should().BeEquivalentTo(
            [
                KeyboardCommandShortcut.SaveWorkbook,
                KeyboardCommandShortcut.OpenWorkbook
            ]);
    }
}
