using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class Wave158AvaloniaQuickAccessKeyTipParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ConfiguredQat_UsesWpfVisibleIndexKeytipsAndInvokesTenthCommand()
    {
        await WithConfiguredToolbar(
            [
                QuickAccessToolbarCommandIds.Save,
                QuickAccessToolbarCommandIds.Undo,
                QuickAccessToolbarCommandIds.Redo,
                QuickAccessToolbarCommandIds.Bold,
                QuickAccessToolbarCommandIds.Italic,
                QuickAccessToolbarCommandIds.Underline,
                QuickAccessToolbarCommandIds.Print,
                QuickAccessToolbarCommandIds.Open,
                QuickAccessToolbarCommandIds.InsertFunction,
                QuickAccessToolbarCommandIds.CalculateSheet,
            ],
            async window =>
            {
                window.Show();
                window.Session.SelectRange(new GridRange(
                    new CellAddress(window.Session.ActiveSheet.Id, 1, 1),
                    new CellAddress(window.Session.ActiveSheet.Id, 1, 1)));

                window.AvaloniaQuickAccessKeyTipForTest(QuickAccessToolbarCommandIds.Save)
                    .Should().Be("1");
                window.AvaloniaQuickAccessKeyTipForTest(QuickAccessToolbarCommandIds.Undo)
                    .Should().Be("2");
                window.AvaloniaQuickAccessKeyTipForTest(QuickAccessToolbarCommandIds.CalculateSheet)
                    .Should().Be("01");
                await Press(window, Key.LeftAlt);
                window.RibbonKeyTipsVisibleForTest.Should().BeTrue();
                window.AvaloniaQuickAccessKeyTipVisibleForTest(QuickAccessToolbarCommandIds.CalculateSheet)
                    .Should().BeTrue("enabled configured QAT commands must expose their keytip badge");

                await PressHandled(window, Key.D0);
                window.QuickAccessKeyTipInputForTest.Should().Be("0");
                await PressHandled(window, Key.D1);

                window.QuickAccessKeyTipInputForTest.Should().BeEmpty();
                window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
                window.StatusTextForTest.Text.Should().Be(
                    UiText.Get("ShellLoc_RecalculatedAllFormulas"));
            });
    }

    [Fact]
    public async Task ConfiguredQat_InvalidAndEscapeContinuationsResetWithoutInvokingCommand()
    {
        await WithConfiguredToolbar(
            QuickAccessToolbarCatalog.Commands
                .Take(10)
                .Select(command => command.Id)
                .ToArray(),
            async window =>
            {
                window.Show();

                await PressHandled(window, Key.D0, KeyModifiers.Alt);
                window.QuickAccessKeyTipInputForTest.Should().Be("0");
                await PressHandled(window, Key.X);
                window.QuickAccessKeyTipInputForTest.Should().BeEmpty();
                window.RibbonKeyTipsVisibleForTest.Should().BeFalse();

                await PressHandled(window, Key.D0, KeyModifiers.Alt);
                await PressHandled(window, Key.Escape);
                window.QuickAccessKeyTipInputForTest.Should().BeEmpty();
                window.RibbonKeyTipsVisibleForTest.Should().BeFalse();

                await PressHandled(window, Key.D0, KeyModifiers.Alt);
                await PressHandled(window, Key.LeftAlt);
                window.QuickAccessKeyTipInputForTest.Should().BeEmpty();
                window.RibbonKeyTipsVisibleForTest.Should().BeTrue(
                    "Alt while paused in a multi-digit QAT keytip must restart the visible keytip scope");
                await PressHandled(window, Key.Escape);
            });
    }

    [Fact]
    public async Task ConfiguredQat_IsExcludedFromFormulaEditingAndBackstage()
    {
        await WithConfiguredToolbar(
            QuickAccessToolbarCatalog.Commands
                .Take(10)
                .Select(command => command.Id)
                .ToArray(),
            async window =>
            {
                var address = new CellAddress(window.Session.ActiveSheet.Id, 1, 1);
                window.BeginFormulaEditForTest(address, "=1");

                var editing = await Press(window, Key.D0, KeyModifiers.Alt);
                editing.Handled.Should().BeFalse();
                window.QuickAccessKeyTipInputForTest.Should().BeEmpty();
                window.Session.CancelFormulaEdit();

                window.ShowBackstageOverlayForTest();
                var backstage = await Press(window, Key.D0, KeyModifiers.Alt);
                backstage.Handled.Should().BeFalse();
                window.QuickAccessKeyTipInputForTest.Should().BeEmpty();
                window.IsBackstageOverlayVisibleForTest.Should().BeTrue();
            });
    }

    private static async Task WithConfiguredToolbar(
        IReadOnlyList<string> commandIds,
        Func<MainWindow, Task> test)
    {
        var priorOptionsPath = Environment.GetEnvironmentVariable(
            AppOptionsStore.OptionsPathEnvironmentVariable);
        var optionsPath = Path.Combine(
            Path.GetTempPath(),
            $"freex-wave158-qat-{Guid.NewGuid():N}.json");
        try
        {
            AppOptionsStore.SaveToPath(
                new AppOptions { QuickAccessToolbarCommands = commandIds.ToList() },
                optionsPath).Should().BeTrue();
            Environment.SetEnvironmentVariable(
                AppOptionsStore.OptionsPathEnvironmentVariable,
                optionsPath);

            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                var sheet = window.Session.Workbook.AddSheet("Wave158 QAT");
                window.Session.SelectSheet(sheet.Id);
                try
                {
                    await test(window);
                }
                finally
                {
                    window.AllowCloseWithoutDirtyPromptForParityCapture();
                    window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AppOptionsStore.OptionsPathEnvironmentVariable,
                priorOptionsPath);
            if (File.Exists(optionsPath))
                File.Delete(optionsPath);
        }
    }

    private static async Task PressHandled(
        MainWindow window,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = await Press(window, key, modifiers);
        args.Handled.Should().BeTrue($"{modifiers}+{key} should be consumed");
    }

    private static async Task<KeyEventArgs> Press(
        MainWindow window,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers };
        await window.RaiseKeyDownForTest(args);
        return args;
    }
}
