using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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

// Regression coverage for review-5 findings K26/K39/K40 (sheet-tab-ops group):
//   K26 - the WPF Unhide-Sheet dialog must not list/unhide VeryHidden sheets, matching Excel
//         (very-hidden sheets are reachable only via VBA/manual XML edits, never Unhide).
//   K39 - Move-or-Copy "Create a copy" on a grouped multi-sheet tab selection must duplicate the
//         whole group, not only the right-clicked sheet.
//   K40 - right-click Delete Sheet on a grouped multi-sheet selection must delete the whole
//         group (respecting the last-visible-sheet guard), not just the clicked tab.
public sealed class MainWindowSheetTabGroupOpsTests
{
    [Fact]
    public void UnhideSheet_OmitsVeryHiddenSheetsFromDialogCandidates()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;
            var hiddenSheet = workbook.AddSheet("HiddenSheet");
            hiddenSheet.IsHidden = true;
            var veryHiddenSheet = workbook.AddSheet("VeryHiddenSheet");
            veryHiddenSheet.IsHidden = true;
            veryHiddenSheet.IsVeryHidden = true;

            List<string>? candidateNames = null;
            QueueOwnedWindowInteraction(harness.Window, dialog =>
            {
                var sheetBox = GetField<ListBox>(dialog, "_sheetBox");
                candidateNames = sheetBox.ItemsSource?.Cast<string>().ToList();
                dialog.Close();
            });

            InvokePrivateMethod(harness.Window, "UnhideSheet");
            PumpDispatcher();

            candidateNames.Should().NotBeNull();
            candidateNames!.Should().Contain("HiddenSheet");
            candidateNames.Should().NotContain("VeryHiddenSheet");
        });
    }

    [Fact]
    public void MoveOrCopy_CreateCopy_WithGroupedSelection_DuplicatesEveryGroupedSheet()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;
            var sheet1 = workbook.GetSheetAt(0);
            var sheet2 = workbook.AddSheet("Sheet2");
            var sheet3 = workbook.AddSheet("Sheet3");
            GroupSheets(harness.Window, sheet1.Id, sheet2.Id, sheet3.Id);

            var originalSheetCount = workbook.Sheets.Count;

            QueueOwnedWindowInteraction(harness.Window, dialog =>
            {
                var createCopyBox = GetField<CheckBox>(dialog, "_createCopyBox");
                var beforeSheetBox = GetField<ListBox>(dialog, "_beforeSheetBox");
                createCopyBox.IsChecked = true;
                beforeSheetBox.SelectedIndex = beforeSheetBox.Items.Count - 1; // "move to end"
                var okButton = GetField<Button>(dialog, "_okButton");
                okButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            });

            InvokeSheetTabContextMenuClick(harness.Window, "SheetCtxMoveOrCopy_Click", sheet2.Id);
            PumpDispatcher();

            // Every grouped sheet must have gained a duplicate — not only the clicked sheet.
            workbook.Sheets.Count.Should().Be(originalSheetCount + 3);
        });
    }

    [Fact]
    public void MoveOrCopy_CreateCopy_WithoutGroupedSelection_DuplicatesOnlyClickedSheet()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;
            var sheet1 = workbook.GetSheetAt(0);
            workbook.AddSheet("Sheet2");

            QueueOwnedWindowInteraction(harness.Window, dialog =>
            {
                var createCopyBox = GetField<CheckBox>(dialog, "_createCopyBox");
                var beforeSheetBox = GetField<ListBox>(dialog, "_beforeSheetBox");
                createCopyBox.IsChecked = true;
                beforeSheetBox.SelectedIndex = beforeSheetBox.Items.Count - 1; // "move to end"
                var okButton = GetField<Button>(dialog, "_okButton");
                okButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            });

            InvokeSheetTabContextMenuClick(harness.Window, "SheetCtxMoveOrCopy_Click", sheet1.Id);
            PumpDispatcher();

            workbook.Sheets.Count.Should().Be(3);
        });
    }

    [Fact]
    public void Delete_WithGroupedSheets_DeletesWholeGroupAfterOneConfirmation()
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
            GroupSheets(harness.Window, sheet1.Id, sheet2.Id, sheet3.Id);

            InvokeSheetTabContextMenuClick(harness.Window, "SheetCtxDelete_Click", sheet2.Id);
            PumpDispatcher();

            messageService.AskYesNoCallCount.Should().Be(1);
            workbook.Sheets.Should().ContainSingle(s => s.Id == sheet4.Id);
            workbook.Sheets.Should().NotContain(s => s.Id == sheet1.Id);
            workbook.Sheets.Should().NotContain(s => s.Id == sheet2.Id);
            workbook.Sheets.Should().NotContain(s => s.Id == sheet3.Id);
        });
    }

    [Fact]
    public void Delete_WithGroupedSheetsCoveringAllVisibleSheets_IsRejectedWithoutDeletingAny()
    {
        StaTestRunner.Run(() =>
        {
            var messageService = new AlwaysYesUserMessageService();
            using var harness = new MainWindowHarness(messageService);
            var workbook = harness.Workbook;
            var sheet1 = workbook.GetSheetAt(0);
            var sheet2 = workbook.AddSheet("Sheet2");
            GroupSheets(harness.Window, sheet1.Id, sheet2.Id);

            InvokeSheetTabContextMenuClick(harness.Window, "SheetCtxDelete_Click", sheet1.Id);
            PumpDispatcher();

            // Deleting every visible sheet in the group must be rejected up front (matching
            // Excel's "cannot delete every sheet" guard), with no partial deletion performed and
            // no confirmation prompt shown.
            messageService.AskYesNoCallCount.Should().Be(0);
            workbook.Sheets.Count.Should().Be(2);
        });
    }

    private static void GroupSheets(MainWindow window, params SheetId[] sheetIds)
    {
        var groupedSheetIds = GetField<HashSet<SheetId>>(window, "_groupedSheetIds");
        groupedSheetIds.Clear();
        foreach (var sheetId in sheetIds)
            groupedSheetIds.Add(sheetId);
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

    private static void InvokePrivateMethod(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, []);
        method.Should().NotBeNull($"{methodName} should exist on MainWindow");
        method!.Invoke(window, []);
    }

    /// <summary>
    /// Schedules a callback to run once the next modal window owned by <paramref name="window"/>
    /// opens (found via <see cref="Window.OwnedWindows"/> once its nested dispatcher loop is
    /// pumping), letting a test drive a synchronous <c>ShowDialog()</c> call made from a
    /// reflection-invoked private MainWindow method.
    /// </summary>
    private static void QueueOwnedWindowInteraction(MainWindow window, Action<Window> interact)
    {
        void PollForDialog()
        {
            var owned = window.OwnedWindows.Cast<Window>().FirstOrDefault();
            if (owned is null)
            {
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)PollForDialog);
                return;
            }

            interact(owned);
        }

        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)PollForDialog);
    }

    private static T GetField<T>(object instance, string fieldName) where T : class
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
            // afterward so every test interacts with the same Workbook instance MainWindow's
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
