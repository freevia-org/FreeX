using System.Reflection;
using System.Windows;
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
/// R123 (round 123): R121 taught the Delete key (ExecuteClearSelection, see
/// R121_DeleteDrawingObjectTests) to remove a genuinely-selected picture/text box/shape/chart
/// instead of clearing whatever cell happened to be active underneath it, but the parallel Backspace
/// shortcut (KeyboardCommandShortcut.ClearSelectionAndEdit, MainWindow.KeyboardCommands.cs) still
/// unconditionally called ExecuteClearActiveCell()+EnterEditMode() with no check of
/// SheetGrid.SelectedObjectId/-Kind. Clicking a drawing object never touches SheetGrid.ActiveCell
/// (only SelectedObjectId/-Kind -- see GridView.Input.cs), so Backspace with an object selected was
/// silently clearing and opening for edit whatever cell was active BEFORE the object was clicked --
/// while the object stayed selected and untouched on screen. In real Excel, Backspace with an
/// object selected is a total no-op: no object deletion, no cell mutation, no edit mode.
///
/// These tests drive the REAL WPF entry point: MainWindow.KeyboardFocus.cs's
/// ExecuteCommandShortcut(shortcut, sender, e), the same private method
/// MainWindow_PreviewKeyDown/KeyDown route a real Backspace keypress to via
/// KeyboardShortcutMatcher.TryGetCommandShortcut + _keyboardCommandDispatcher.TryExecute -- not a
/// hand-built model or a directly-named private method, mirroring R121's reflection convention.
/// </summary>
public sealed class R123_BackspaceDrawingObjectTests
{
    [Fact]
    public void BackspaceKey_WithPictureSelected_DoesNotClearCellOrEnterEditMode()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                // The cell that was active BEFORE the drawing object was clicked (e.g. the default
                // A1 on a freshly opened sheet) -- clicking the picture never moves it.
                var activeCell = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(activeCell, new NumberValue(42));
                var picture = new PictureModel
                {
                    Anchor = new CellAddress(sheet.Id, 5, 5),
                    Kind = PictureKind.Image,
                    ImageBytes = [1, 2, 3],
                    ContentType = "image/png",
                    Name = "Picture 1"
                };
                sheet.Pictures.Add(picture);

                var grid = (GridView)window.FindName("SheetGrid");
                // Drive the real selection path (SetActiveCell) so MainWindow's internally-tracked
                // _selectionAnchor -- which EnterEditMode actually reads, NOT SheetGrid.ActiveCell
                // directly -- is set exactly as it would be by the user's prior cell click, before
                // clicking the picture only overwrites SelectedObjectId/-Kind (GridView.Input.cs).
                InvokeSetActiveCell(window, activeCell);
                grid.SelectedObjectId = picture.Id;
                grid.SelectedObjectKind = ObjectKind.Picture;

                InvokeClearSelectionAndEditShortcut(window);
                PumpDispatcher();

                sheet.GetCell(activeCell)!.Value.Should().Be(new NumberValue(42),
                    "Backspace with a drawing object selected must not touch the unrelated active cell");
                grid.EditingCell.Should().BeNull(
                    "Backspace with a drawing object selected must not open the in-cell editor");
                sheet.Pictures.Should().ContainSingle(
                    "Backspace must never delete a selected object -- only Delete does that");
                grid.SelectedObjectKind.Should().Be(ObjectKind.Picture,
                    "the object must remain selected after a no-op Backspace");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void BackspaceKey_WithNoObjectSelected_StillClearsActiveCellAndEntersEditMode()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var activeCell = new CellAddress(sheet.Id, 2, 2);
                sheet.SetCell(activeCell, new NumberValue(7));

                var grid = (GridView)window.FindName("SheetGrid");
                InvokeSetActiveCell(window, activeCell);
                grid.SelectedObjectId = Guid.Empty;
                grid.SelectedObjectKind = ObjectKind.None;

                InvokeClearSelectionAndEditShortcut(window);
                PumpDispatcher();

                sheet.GetCell(activeCell)?.Value.Should().Be(BlankValue.Instance,
                    "with no drawing object selected, Backspace must still clear the active cell as before");
                grid.EditingCell.Should().Be(activeCell,
                    "with no drawing object selected, Backspace must still enter edit mode on the active cell");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    private static void InvokeClearSelectionAndEditShortcut(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod(
            "ExecuteCommandShortcut",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteCommandShortcut");
        method.Invoke(window, [KeyboardCommandShortcut.ClearSelectionAndEdit, window, new RoutedEventArgs()]);
    }

    private static void InvokeSetActiveCell(MainWindow window, CellAddress addr)
    {
        var method = typeof(MainWindow).GetMethod(
            "SetActiveCell",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "SetActiveCell");
        method.Invoke(window, [addr]);
    }
}
