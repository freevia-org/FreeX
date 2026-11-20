using System.Reflection;
using System.Threading;

using Avalonia.Headless;

using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class Wave201WorksheetInsertRoutingParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task InsertWorksheetCommand_InsertsBeforeActiveForKeyboard_AndAppendsForQuickAccess()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Session.Workbook.AddSheet("InsertBefore");
                var active = window.Session.Workbook.AddSheet("InsertActive");
                window.Session.Workbook.AddSheet("InsertAfter");
                window.Session.SelectSheet(active.Id);

                var bindings = GetApplicationCommands(window);
                WorkbookApplicationCommandRouter.TryRouteShortcut(
                    WorkbookShortcutRoute.InsertWorksheet,
                    out var keyboardRoute).Should().BeTrue();
                var beforeKeyboardInsert = window.Session.Workbook.Sheets.Select(sheet => sheet.Id).ToList();

                var keyboardResult = await bindings.TryExecuteAsync(keyboardRoute);

                keyboardResult.IsBound.Should().BeTrue();
                keyboardResult.Handled.Should().BeTrue();
                var afterKeyboardInsert = window.Session.Workbook.Sheets.Select(sheet => sheet.Id).ToList();
                var keyboardInsertedId = afterKeyboardInsert.Except(beforeKeyboardInsert).Single();
                afterKeyboardInsert.IndexOf(keyboardInsertedId)
                    .Should().Be(beforeKeyboardInsert.IndexOf(active.Id));
                afterKeyboardInsert.IndexOf(active.Id)
                    .Should().Be(beforeKeyboardInsert.IndexOf(active.Id) + 1);

                WorkbookApplicationCommandRouter.TryRouteQuickAccess("InsertSheet", out var quickAccessRoute)
                    .Should().BeTrue();
                var beforeQuickAccessInsert = window.Session.Workbook.Sheets.Select(sheet => sheet.Id).ToList();

                var quickAccessResult = await bindings.TryExecuteAsync(quickAccessRoute);

                quickAccessResult.IsBound.Should().BeTrue();
                quickAccessResult.Handled.Should().BeTrue();
                var afterQuickAccessInsert = window.Session.Workbook.Sheets.Select(sheet => sheet.Id).ToList();
                var quickAccessInsertedId = afterQuickAccessInsert.Except(beforeQuickAccessInsert).Single();
                afterQuickAccessInsert.IndexOf(quickAccessInsertedId).Should().Be(afterQuickAccessInsert.Count - 1);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static WorkbookApplicationCommandBindings GetApplicationCommands(MainWindow window) =>
        (WorkbookApplicationCommandBindings)typeof(MainWindow)
            .GetProperty("WorkbookApplicationCommands", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
}
