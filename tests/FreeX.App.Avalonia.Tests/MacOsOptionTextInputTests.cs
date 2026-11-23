using System.Threading;
using Avalonia.Headless;
using Avalonia.Input;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class MacOsOptionTextInputTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(KeyModifiers.Alt, "é", true)]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Shift, "É", true)]
    [InlineData(KeyModifiers.Alt, "´", true)]
    [InlineData(KeyModifiers.Alt, "£", true)]
    [InlineData(KeyModifiers.Alt, "\u00a0", true)]
    [InlineData(KeyModifiers.Alt, null, false)]
    [InlineData(KeyModifiers.Alt, "\t", false)]
    [InlineData(KeyModifiers.Alt, "\uf702", false)]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Meta, "é", false)]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Control, "é", false)]
    [InlineData(KeyModifiers.None, "é", false)]
    public void OptionTextPolicy_PreservesCompositionAndNavigation(
        KeyModifiers modifiers, string? symbol, bool expected)
    {
        MainWindow.ShouldPassOptionTextToInput(true, modifiers, symbol, false).Should().Be(expected);
        MainWindow.ShouldPassOptionTextToInput(false, modifiers, symbol, false).Should().BeFalse(
            "Windows and Linux retain their Alt accelerators");
        MainWindow.ShouldPassOptionTextToInput(true, modifiers, symbol, true).Should().BeFalse(
            "an explicitly entered keytip mode retains its routing");
    }

    [Theory]
    [InlineData(Key.E, "´")]
    [InlineData(Key.D3, "£")]
    [InlineData(Key.H, "˙")]
    [InlineData(Key.OemPlus, "≠")]
    public Task NativeOptionText_DoesNotEnterRibbonOrQuickAccessKeyTips(Key key, string symbol) =>
        Run(async window =>
        {
            await Press(window, Key.LeftAlt, KeyModifiers.Alt);
            var option = await Press(window, key, KeyModifiers.Alt, symbol);
            option.Handled.Should().BeFalse("the native input method must receive the Option key chord");
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
            window.QuickAccessKeyTipInputForTest.Should().BeEmpty();
            window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
            window.OwnedWindows.Should().BeEmpty();
            window.RaiseKeyUpForTest(new KeyEventArgs { Key = Key.LeftAlt });
            window.RibbonKeyTipsVisibleForTest.Should().BeFalse(
                "typing an Option chord cancels the standalone Option tap candidate");
        });

    [Fact]
    public Task OptionDeadKeyComposition_CanStartAWorksheetEdit() =>
        Run(async window =>
        {
            var address = window.Session.ActiveCell;
            (await Press(window, Key.E, KeyModifiers.Alt, "´")).Handled.Should().BeFalse();
            (await Press(window, Key.E, KeyModifiers.None, "e")).Handled.Should().BeFalse();

            // AppKit owns dead-key composition; deliver the resulting text as the native backend
            // does after unhandled KeyDown, then observe the actual worksheet editing path.
            window.SheetGridHostForTest.RaiseEvent(new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Source = window.SheetGridHostForTest,
                Text = "é",
            });

            window.Session.FormulaEditAddress.Should().Be(address);
            window.FormulaBoxTextForTest.Should().Be("é");
        });

    [Fact]
    public Task F10AndStandaloneOption_StillEnterKeyTipMode() =>
        Run(async window =>
        {
            (await Press(window, Key.F10, KeyModifiers.None)).Handled.Should().BeTrue();
            window.RibbonKeyTipsVisibleForTest.Should().BeTrue();
            (await Press(window, Key.H, KeyModifiers.None, "h")).Handled.Should().BeTrue();
            window.RibbonKeyTipInputForTest.Should().Be("H");
            await Press(window, Key.Escape, KeyModifiers.None);

            await Press(window, Key.LeftAlt, KeyModifiers.Alt);
            window.RaiseKeyUpForTest(new KeyEventArgs { Key = Key.LeftAlt });
            window.RibbonKeyTipsVisibleForTest.Should().BeTrue();
            (await Press(window, Key.H, KeyModifiers.Alt, "˙")).Handled.Should().BeTrue();
            window.RibbonKeyTipInputForTest.Should().Be("H");
        });

    private static async Task<KeyEventArgs> Press(
        MainWindow window, Key key, KeyModifiers modifiers, string? symbol = null)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers, KeySymbol = symbol };
        await window.RaiseKeyDownForTest(args);
        return args;
    }

    private static Task Run(Func<MainWindow, Task> action)
    {
        if (!OperatingSystem.IsMacOS())
            return Task.CompletedTask;

        return Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                await action(window);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }
}
