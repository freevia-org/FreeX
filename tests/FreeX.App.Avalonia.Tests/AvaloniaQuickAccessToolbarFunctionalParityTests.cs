using System.Reflection;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;

using FreeX.App.Avalonia;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaQuickAccessToolbarFunctionalParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task CalculateSheetQatCommandRunsWhenItsRibbonTabIsNotSelected()
    {
        var previousPath = Environment.GetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable);
        var optionsPath = Path.Combine(Path.GetTempPath(), $"freex-qat-functional-{Guid.NewGuid():N}.json");
        try
        {
            AppOptionsStore.SaveToPath(
                new AppOptions { QuickAccessToolbarCommands = [QuickAccessToolbarCommandIds.CalculateSheet] },
                optionsPath).Should().BeTrue();
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, optionsPath);

            await Session.Dispatch(() =>
            {
                var window = new MainWindow([]);
                var sheet = window.Session.Workbook.AddSheet("QatFixture");
                window.Session.SelectSheet(sheet.Id);
                window.Session.SelectRange(new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 1, 1)));
                try
                {
                    var calculateSheetButton = window.AvaloniaQuickAccessToolbarForTest.Children
                        .OfType<Button>()
                        .Single(button => button.Tag as string == QuickAccessToolbarCommandIds.CalculateSheet);

                    calculateSheetButton.IsEnabled.Should().BeTrue();
                    QuickAccessToolbarCatalog.TryGet(
                        QuickAccessToolbarCommandIds.CalculateSheet,
                        out var command).Should().BeTrue();
                    var dispatch = typeof(MainWindow).GetMethod(
                        "ExecuteAvaloniaQuickAccessCommand",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    dispatch.Should().NotBeNull();
                    dispatch!.Invoke(
                        window,
                        [command, calculateSheetButton, new RoutedEventArgs(Button.ClickEvent)]);

                    window.StatusTextForTest.Text.Should().Be(
                        UiText.Get("ShellLoc_RecalculatedAllFormulas"),
                        "the QAT command must execute its host action directly even while the " +
                        "Formulas tab is not the selected ribbon tab");
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
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, previousPath);
            if (File.Exists(optionsPath))
                File.Delete(optionsPath);
        }
    }
}
