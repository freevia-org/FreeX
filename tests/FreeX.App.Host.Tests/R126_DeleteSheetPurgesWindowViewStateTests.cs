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

/// <summary>
/// R126-viewstate-delete-purge-1: <see cref="WorksheetViewStateStore.Remove"/> is documented "Drops
/// a sheet's remembered view state (e.g. when the sheet is deleted)" but neither Delete Sheet entry
/// point (<c>SheetCtxDelete_Click</c>, MainWindow.SheetTabs.cs; <c>DeleteSheetMenuItem_Click</c>,
/// MainWindow.CellsCommands.cs) ever called it -- only <c>_worksheetSelections.Remove(sheetId)</c>
/// ran, so this window's own remembered view mode/zoom/gridlines/headings/formulas/freeze/split for
/// a deleted sheet stayed cached in <c>_worksheetViewStates</c> (and <c>_splitPaneViewportOffsets</c>)
/// for the rest of the window's lifetime, until a full New/Open workbook <c>Clear()</c>'d them.
/// </summary>
public sealed class R126_DeleteSheetPurgesWindowViewStateTests
{
    [Fact]
    public void SheetCtxDelete_PurgesTheDeletedSheetsWindowViewStateAndSplitPaneOffsets()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;
            var sheet1 = workbook.GetSheetAt(0);
            var sheet2 = workbook.AddSheet("Sheet2");

            // This window's own view for Sheet2 diverges from the workbook default (Zoom 100) via
            // the SAME real command + SyncWindowViewState call every View-tab handler performs
            // (mirrors R88_CustomViewWindowStateInvalidationTests), seeding _worksheetViewStates.
            SelectSheetTab(harness.Window, sheet2.Id);
            harness.ExecuteCommand(new SetWorksheetZoomCommand(sheet2.Id, 150)).Success.Should().BeTrue();
            harness.SyncWindowViewState([sheet2.Id]);

            // _splitPaneViewportOffsets has no real write path in the WPF host today (only
            // Remove/Clear call sites exist -- see MainWindow.ViewCommands.cs), so seed it directly
            // to prove the delete handler's own Remove call actually fires for this dictionary too.
            harness.SeedSplitPaneViewportOffset(sheet2.Id);

            harness.GetWorksheetViewStateSnapshots().Should().ContainKey(sheet2.Id, "sanity: the window's view state must be seeded before delete");
            harness.GetSplitPaneViewportOffsetKeys().Should().Contain(sheet2.Id, "sanity: the split-pane offsets must be seeded before delete");

            InvokeSheetTabContextMenuClick(harness.Window, "SheetCtxDelete_Click", sheet2.Id);
            PumpDispatcher();

            workbook.Sheets.Should().NotContain(s => s.Id == sheet2.Id);
            harness.GetWorksheetViewStateSnapshots().Should().NotContainKey(
                sheet2.Id,
                "the deleted sheet's remembered view state must not leak for the rest of the window's lifetime");
            harness.GetSplitPaneViewportOffsetKeys().Should().NotContain(
                sheet2.Id,
                "the deleted sheet's split-pane viewport offsets must not leak for the rest of the window's lifetime");

            // No-regression: Sheet1's own view state (seeded at defaults when the window first
            // rendered it) must survive Sheet2's deletion unchanged -- see the dedicated
            // SheetCtxDelete_LeavesSurvivingSheetsOwnWindowViewStateIntact test below for the case
            // where Sheet1's view state actually diverges from the workbook default.
            harness.GetWorksheetViewStateSnapshots().Should().ContainKey(sheet1.Id)
                .WhoseValue.ZoomPercent.Should().Be(100);
        });
    }

    [Fact]
    public void DeleteSheetMenuItem_PurgesTheDeletedSheetsWindowViewStateAndSplitPaneOffsets()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;
            var sheet2 = workbook.AddSheet("Sheet2");

            SelectSheetTab(harness.Window, sheet2.Id);
            harness.ExecuteCommand(new SetWorksheetZoomCommand(sheet2.Id, 200)).Success.Should().BeTrue();
            harness.SyncWindowViewState([sheet2.Id]);
            harness.SeedSplitPaneViewportOffset(sheet2.Id);

            harness.GetWorksheetViewStateSnapshots().Should().ContainKey(sheet2.Id);
            harness.GetSplitPaneViewportOffsetKeys().Should().Contain(sheet2.Id);

            InvokeParameterlessClick(harness.Window, "DeleteSheetMenuItem_Click");
            PumpDispatcher();

            workbook.Sheets.Should().NotContain(s => s.Id == sheet2.Id);
            harness.GetWorksheetViewStateSnapshots().Should().NotContainKey(sheet2.Id);
            harness.GetSplitPaneViewportOffsetKeys().Should().NotContain(sheet2.Id);
        });
    }

    /// <summary>
    /// No-regression sibling: deleting a sheet whose view was never diverged from the workbook
    /// default must not disturb a SURVIVING sheet's own already-seeded view state.
    /// </summary>
    [Fact]
    public void SheetCtxDelete_LeavesSurvivingSheetsOwnWindowViewStateIntact()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;
            var sheet1 = workbook.GetSheetAt(0);
            var sheet2 = workbook.AddSheet("Sheet2");

            // Sheet1 (the surviving sheet) gets its own diverged view state first.
            SelectSheetTab(harness.Window, sheet1.Id);
            harness.ExecuteCommand(new SetWorksheetZoomCommand(sheet1.Id, 175)).Success.Should().BeTrue();
            harness.SyncWindowViewState([sheet1.Id]);

            SelectSheetTab(harness.Window, sheet2.Id);

            InvokeSheetTabContextMenuClick(harness.Window, "SheetCtxDelete_Click", sheet2.Id);
            PumpDispatcher();

            workbook.Sheets.Should().ContainSingle().Which.Id.Should().Be(sheet1.Id);
            harness.GetWorksheetViewStateSnapshots().Should().ContainKey(sheet1.Id)
                .WhoseValue.ZoomPercent.Should().Be(175);
        });
    }

    private static void SelectSheetTab(MainWindow window, SheetId sheetId)
    {
        var selectSingleSheetTab = typeof(MainWindow)
            .GetMethod("SelectSingleSheetTab", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "SelectSingleSheetTab");
        var updateViewport = typeof(MainWindow)
            .GetMethod("UpdateViewport", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "UpdateViewport");

        selectSingleSheetTab.Invoke(window, [sheetId]);
        updateViewport.Invoke(window, []);
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

    private static void InvokeParameterlessClick(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(object), typeof(RoutedEventArgs)]);
        method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
        method!.Invoke(window, [new object(), new RoutedEventArgs()]);
    }

    private sealed class MainWindowHarness : IDisposable
    {
        public MainWindow Window { get; }
        public Workbook Workbook { get; }

        private readonly FieldInfo _commandBusField;
        private readonly FieldInfo _worksheetViewStatesField;
        private readonly FieldInfo _splitPaneViewportOffsetsField;
        private readonly MethodInfo _syncWindowViewState;

        public MainWindowHarness()
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
                new AlwaysYesUserMessageService());

            Window.Show();
            PumpDispatcher();

            Workbook = workbookRef.Current;

            _commandBusField = typeof(MainWindow)
                .GetField("_commandBus", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_commandBus");
            _worksheetViewStatesField = typeof(MainWindow)
                .GetField("_worksheetViewStates", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_worksheetViewStates");
            _splitPaneViewportOffsetsField = typeof(MainWindow)
                .GetField("_splitPaneViewportOffsets", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_splitPaneViewportOffsets");
            _syncWindowViewState = typeof(MainWindow)
                .GetMethod("SyncWindowViewState", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SyncWindowViewState");
        }

        public CommandOutcome ExecuteCommand(IWorkbookCommand command)
        {
            var bus = (ICommandBus)(_commandBusField.GetValue(Window)
                ?? throw new InvalidOperationException("MainWindow command bus is not initialized."));
            var outcome = bus.Execute(Workbook.Id, command);
            PumpDispatcher();
            return outcome;
        }

        public void SyncWindowViewState(IReadOnlyList<SheetId> targetSheetIds) =>
            _syncWindowViewState.Invoke(Window, [targetSheetIds]);

        public IReadOnlyDictionary<SheetId, WorksheetViewStateSnapshot> GetWorksheetViewStateSnapshots()
        {
            var store = _worksheetViewStatesField.GetValue(Window)
                ?? throw new InvalidOperationException("_worksheetViewStates was null.");
            var snapshotsProperty = store.GetType().GetProperty("Snapshots")
                ?? throw new InvalidOperationException("WorksheetViewStateStore has no Snapshots property.");
            return (IReadOnlyDictionary<SheetId, WorksheetViewStateSnapshot>)snapshotsProperty.GetValue(store)!;
        }

        public void SeedSplitPaneViewportOffset(SheetId sheetId)
        {
            var dict = (Dictionary<SheetId, SplitPaneViewportOffsets>)(_splitPaneViewportOffsetsField.GetValue(Window)
                ?? throw new InvalidOperationException("_splitPaneViewportOffsets was null."));
            dict[sheetId] = new SplitPaneViewportOffsets(3u, null);
        }

        public IEnumerable<SheetId> GetSplitPaneViewportOffsetKeys()
        {
            var dict = (Dictionary<SheetId, SplitPaneViewportOffsets>)(_splitPaneViewportOffsetsField.GetValue(Window)
                ?? throw new InvalidOperationException("_splitPaneViewportOffsets was null."));
            return dict.Keys.ToList();
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
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }

        public bool AskYesNo(string message, string title = "Confirm") => true;

        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Yes;
    }
}
