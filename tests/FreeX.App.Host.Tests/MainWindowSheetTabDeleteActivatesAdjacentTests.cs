using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.AppServices;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

// Regression coverage for R42-io-sheet-tab-order-activetab-3-2: the WPF host's Delete Sheet
// handler (SheetCtxDelete_Click, MainWindow.SheetTabs.cs) used to unconditionally jump
// `_currentSheetId` to `_workbook.Sheets[0].Id` after deleting a sheet, discarding the
// Excel-correct adjacent-surviving-sheet index that `Workbook.RemoveSheet` (via
// AdjustSheetIndexAfterRemoval) already computes. Real Excel activates the sheet immediately to
// the right of the one just deleted (or the new last sheet, if the deleted sheet was at the end)
// -- never unconditionally the workbook's first tab.
public sealed class MainWindowSheetTabDeleteActivatesAdjacentTests
{
    [Fact]
    public void Delete_MiddleSheet_ActivatesTheSheetToItsRight_NotTheWorkbooksFirstSheet()
    {
        StaTestRunner.Run(() =>
        {
            var messageService = new AlwaysYesUserMessageService();
            using var harness = new MainWindowHarness(messageService);
            var workbook = harness.Workbook;
            var sheet1 = workbook.GetSheetAt(0);
            var sheet2 = workbook.AddSheet("Sheet2");
            var sheet3 = workbook.AddSheet("Sheet3");
            var sheet4 = workbook.AddSheet("Sheet4");

            // The user is ON Sheet3 (matching the finding's failure scenario) when they delete it
            // -- in the real app this happens via SheetTab_MouseRightButtonDown selecting the
            // clicked tab (SelectSingleSheetTab + UpdateViewport, which syncs
            // workbook.ActiveSheetIndex) before the context menu's Delete item is invoked.
            SelectSheetTab(harness.Window, sheet3.Id);

            InvokeSheetTabContextMenuClick(harness.Window, "SheetCtxDelete_Click", sheet3.Id);
            PumpDispatcher();

            workbook.Sheets.Should().NotContain(s => s.Id == sheet3.Id);
            var currentSheetId = GetField<SheetId>(harness.Window, "_currentSheetId");
            currentSheetId.Should().Be(
                sheet4.Id,
                "Excel activates the sheet immediately to the right of the one just deleted, " +
                "not the workbook's first sheet");
            workbook.ActiveSheetIndex.Should().Be(
                workbook.Sheets.ToList().FindIndex(s => s.Id == sheet4.Id),
                "the model's ActiveSheetIndex must stay in sync with the newly-activated sheet");
            currentSheetId.Should().NotBe(sheet1.Id);
        });
    }

    [Fact]
    public void Delete_LastSheet_ActivatesThePrecedingSheet()
    {
        // Sibling/no-regression case: deleting the LAST tab has no sheet to its right, so Excel
        // falls back to the sheet immediately to its left (now the new last tab).
        StaTestRunner.Run(() =>
        {
            var messageService = new AlwaysYesUserMessageService();
            using var harness = new MainWindowHarness(messageService);
            var workbook = harness.Workbook;
            var sheet1 = workbook.GetSheetAt(0);
            var sheet2 = workbook.AddSheet("Sheet2");
            var sheet3 = workbook.AddSheet("Sheet3");

            SelectSheetTab(harness.Window, sheet3.Id);

            InvokeSheetTabContextMenuClick(harness.Window, "SheetCtxDelete_Click", sheet3.Id);
            PumpDispatcher();

            workbook.Sheets.Should().NotContain(s => s.Id == sheet3.Id);
            var currentSheetId = GetField<SheetId>(harness.Window, "_currentSheetId");
            currentSheetId.Should().Be(sheet2.Id);
            currentSheetId.Should().NotBe(sheet1.Id);
        });
    }

    private static void SelectSheetTab(MainWindow window, SheetId sheetId)
    {
        window.SelectSingleSheetTab(sheetId);
        window.UpdateViewport();
    }

    private static void InvokeSheetTabContextMenuClick(MainWindow window, string methodName, SheetId clickedSheetId)
    {
        var tab = new SheetTabViewModel(clickedSheetId, "irrelevant", null);
        var placementTarget = new Border { DataContext = tab };
        var menuItem = new MenuItem();
        var contextMenu = new ContextMenu { PlacementTarget = placementTarget };
        contextMenu.Items.Add(menuItem);

        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(object), typeof(RoutedEventArgs)]);
        method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
        method!.Invoke(window, [menuItem, new RoutedEventArgs()]);
    }

    private static T GetField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().Name, fieldName);
        return (T)field.GetValue(instance)!;
    }

    private sealed class MainWindowHarness : IDisposable
    {
        public MainWindow Window { get; }
        public Workbook Workbook { get; }

        public MainWindowHarness(IUserMessageService? messageService = null)
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            Window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                messageService ?? NullUserMessageService.Instance);

            Window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied
            // workbook with a fresh one via CreateNewWorkbook() (there is no window registry
            // here, so ShouldAdoptSharedWorkbookOnLoad is false) -- capture the *live* workbook
            // afterward so this test interacts with the same Workbook instance MainWindow's
            // sheet-tab handlers actually operate on.
            Workbook = workbookRef.Current;
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in Window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(Window);
            PumpDispatcher();
        }
    }

    private sealed class AlwaysYesUserMessageService : IUserMessageService
    {
        public int AskYesNoCallCount { get; private set; }

        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }

        public bool AskYesNo(string message, string title = "Confirm")
        {
            AskYesNoCallCount++;
            return true;
        }

        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Yes;
    }
}
