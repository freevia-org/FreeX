using System.Reflection;
using System.Windows;
using System.Windows.Input;
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
/// R129 (round 129): a cross-shell keyboard matrix found FOUR keys that leave a genuinely selected
/// picture/shape/text box/chart (SheetGrid.SelectedObjectId/-Kind) behaving wrong in BOTH shells,
/// none of which R123's Backspace fix (R123_BackspaceDrawingObjectTests) covered:
///   1. Arrow keys never moved the object at all (no Nudge/Move capability existed) -- they fell
///      through to ordinary cell navigation, leaving the object visually selected while the cell
///      cursor moved underneath it. Fixed via DrawingObjectCommandPlanner.BuildNudgeCommand +
///      MainWindow.Drawing.cs's NudgeSelectedDrawingObject, wired ahead of the arrow-key navigation
///      block in MainWindow.Selection.cs's MainWindow_KeyDown.
///   2. Escape never cleared SheetGrid.SelectedObjectId/-Kind (CancelCopyAndTransientModes never
///      touched it), so the object stayed visibly selected after pressing it.
///   3. F2 opened the underlying (unrelated) active cell for edit instead of no-op'ing.
///   4. Ctrl+D/Ctrl+R (fill down/right) filled the underlying active cell/range instead of no-op'ing.
/// Items 2-4 are the same family Backspace already answered for one key -- this class covers the
/// rest so the guard isn't patched one key at a time again.
/// </summary>
public sealed class R129_DrawingObjectKeyboardFamilyTests
{
    [Fact]
    public void ArrowKey_WithShapeSelected_NudgesObjectInstead_OfMovingCellCursor()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = DrawingObjectKeyboardHarness.Create();
            var sheet = harness.Sheet;
            var activeCell = new CellAddress(sheet.Id, 0, 0);
            harness.SetActiveCell(activeCell);

            var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 5, 5), Width = 100, Height = 60 };
            sheet.DrawingShapes.Add(shape);
            harness.Grid.SelectedObjectId = shape.Id;
            harness.Grid.SelectedObjectKind = ObjectKind.Shape;

            var offsetXBefore = shape.AnchorOffsetX;

            harness.PressKey(Key.Right);

            shape.AnchorOffsetX.Should().BeGreaterThan(offsetXBefore,
                "the arrow key must nudge the selected shape's pixel offset");
            shape.Anchor.Should().Be(new CellAddress(sheet.Id, 5, 5),
                "nudging must never re-anchor the shape to a different cell");
            harness.ActiveCellAddress.Should().Be(activeCell,
                "the cell cursor underneath the selected object must not move");
            harness.Grid.SelectedObjectId.Should().Be(shape.Id,
                "the object must remain selected after nudging");
        });
    }

    [Fact]
    public void ArrowKey_WithNoObjectSelected_StillNavigatesCellAsBefore()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = DrawingObjectKeyboardHarness.Create();
            var sheet = harness.Sheet;
            var start = new CellAddress(sheet.Id, 4, 4);
            harness.SetActiveCell(start);
            harness.Grid.SelectedObjectId = Guid.Empty;
            harness.Grid.SelectedObjectKind = ObjectKind.None;

            harness.PressKey(Key.Right);

            harness.ActiveCellAddress.Should().Be(new CellAddress(sheet.Id, 4, 5),
                "with no drawing object selected, arrow keys must still move the cell cursor as before");
        });
    }

    [Fact]
    public void NudgeSelectedDrawingObject_CtrlHeld_MovesBySmallerIncrementThanPlainArrow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = DrawingObjectKeyboardHarness.Create();
            var sheet = harness.Sheet;
            var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 5, 5), Width = 100, Height = 60 };
            sheet.DrawingShapes.Add(shape);
            harness.Grid.SelectedObjectId = shape.Id;
            harness.Grid.SelectedObjectKind = ObjectKind.Shape;

            harness.InvokeNudge(Key.Right, fine: false);
            var coarseOffset = shape.AnchorOffsetX;

            harness.InvokeNudge(Key.Right, fine: true);
            var fineDelta = shape.AnchorOffsetX - coarseOffset;

            fineDelta.Should().BeGreaterThan(0).And.BeLessThan(coarseOffset,
                "Ctrl+arrow must nudge by a smaller increment than a plain arrow, matching Excel");
        });
    }

    [Fact]
    public void EscapeKey_WithPictureSelected_DeselectsObject()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = DrawingObjectKeyboardHarness.Create();
            var sheet = harness.Sheet;
            var picture = new PictureModel
            {
                Anchor = new CellAddress(sheet.Id, 5, 5),
                Kind = PictureKind.Image,
                ImageBytes = [1, 2, 3],
                ContentType = "image/png",
                Name = "Picture 1"
            };
            sheet.Pictures.Add(picture);
            harness.Grid.SelectedObjectId = picture.Id;
            harness.Grid.SelectedObjectKind = ObjectKind.Picture;

            harness.PressKey(Key.Escape);

            harness.Grid.SelectedObjectId.Should().Be(Guid.Empty,
                "Escape with a drawing object selected must deselect it, matching Excel");
            harness.Grid.SelectedObjectKind.Should().Be(ObjectKind.None);
            sheet.Pictures.Should().ContainSingle("Escape must never delete the object, only deselect it");
        });
    }

    [Fact]
    public void F2Key_WithShapeSelected_DoesNotEnterEditMode()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = DrawingObjectKeyboardHarness.Create();
            var sheet = harness.Sheet;
            var activeCell = new CellAddress(sheet.Id, 2, 2);
            sheet.SetCell(activeCell, new NumberValue(42));
            harness.SetActiveCell(activeCell);

            var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 5, 5), Width = 100, Height = 60 };
            sheet.DrawingShapes.Add(shape);
            harness.Grid.SelectedObjectId = shape.Id;
            harness.Grid.SelectedObjectKind = ObjectKind.Shape;

            harness.InvokeEditCellShortcut();

            harness.Grid.EditingCell.Should().BeNull(
                "F2 with a drawing object selected must not open the underlying active cell for edit");
            harness.Grid.SelectedObjectKind.Should().Be(ObjectKind.Shape,
                "the object must remain selected after a no-op F2");
        });
    }

    [Fact]
    public void F2Key_WithNoObjectSelected_StillEntersEditModeAsBefore()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = DrawingObjectKeyboardHarness.Create();
            var sheet = harness.Sheet;
            var activeCell = new CellAddress(sheet.Id, 2, 2);
            sheet.SetCell(activeCell, new NumberValue(7));
            harness.SetActiveCell(activeCell);
            harness.Grid.SelectedObjectId = Guid.Empty;
            harness.Grid.SelectedObjectKind = ObjectKind.None;

            harness.InvokeEditCellShortcut();

            harness.Grid.EditingCell.Should().Be(activeCell,
                "with no drawing object selected, F2 must still enter edit mode on the active cell");
        });
    }

    [Fact]
    public void CtrlD_WithShapeSelected_DoesNotFill()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = DrawingObjectKeyboardHarness.Create();
            var sheet = harness.Sheet;
            var sourceCell = new CellAddress(sheet.Id, 1, 1);
            var targetCell = new CellAddress(sheet.Id, 2, 1);
            sheet.SetCell(sourceCell, new NumberValue(99));
            var range = new GridRange(sourceCell, targetCell);
            harness.Grid.SelectedRange = range;
            harness.Grid.SelectedRanges = null;

            var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 5, 5), Width = 100, Height = 60 };
            sheet.DrawingShapes.Add(shape);
            harness.Grid.SelectedObjectId = shape.Id;
            harness.Grid.SelectedObjectKind = ObjectKind.Shape;

            harness.InvokeFillDownShortcut();

            (sheet.GetCell(targetCell)?.Value ?? BlankValue.Instance).Should().Be(BlankValue.Instance,
                "Ctrl+D with a drawing object selected must not fill the underlying selection");
        });
    }

    [Fact]
    public void CtrlD_WithNoObjectSelected_StillFillsAsBefore()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = DrawingObjectKeyboardHarness.Create();
            var sheet = harness.Sheet;
            var sourceCell = new CellAddress(sheet.Id, 1, 1);
            var targetCell = new CellAddress(sheet.Id, 2, 1);
            sheet.SetCell(sourceCell, new NumberValue(99));
            var range = new GridRange(sourceCell, targetCell);
            harness.Grid.SelectedRange = range;
            harness.Grid.SelectedRanges = null;
            harness.Grid.SelectedObjectId = Guid.Empty;
            harness.Grid.SelectedObjectKind = ObjectKind.None;

            harness.InvokeFillDownShortcut();

            sheet.GetCell(targetCell)?.Value.Should().Be(new NumberValue(99),
                "with no drawing object selected, Ctrl+D must still fill down as before");
        });
    }

    private sealed class DrawingObjectKeyboardHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _setActiveCell;
        private readonly MethodInfo _mainWindowKeyDown;
        private readonly MethodInfo _executeCommandShortcut;
        private readonly MethodInfo _nudgeSelectedDrawingObject;
        private readonly FieldInfo _selectionAnchorField;
        private readonly Type _keyboardCommandShortcutType;

        private DrawingObjectKeyboardHarness(MainWindow window)
        {
            _window = window;
            _setActiveCell = typeof(MainWindow)
                .GetMethod("SetActiveCell", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetActiveCell");
            _mainWindowKeyDown = typeof(MainWindow)
                .GetMethod("MainWindow_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "MainWindow_KeyDown");
            _executeCommandShortcut = typeof(MainWindow)
                .GetMethod("ExecuteCommandShortcut", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteCommandShortcut");
            _nudgeSelectedDrawingObject = typeof(MainWindow)
                .GetMethod("NudgeSelectedDrawingObject", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "NudgeSelectedDrawingObject");
            _selectionAnchorField = typeof(MainWindow)
                .GetField("_selectionAnchorField", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_selectionAnchorField");
            _keyboardCommandShortcutType = typeof(KeyboardCommandShortcut);
        }

        public static DrawingObjectKeyboardHarness Create()
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
            window.Show();
            PumpDispatcher();
            return new DrawingObjectKeyboardHarness(window);
        }

        private Workbook LiveWorkbook => _window.Session.Workbook;

        public Sheet Sheet => LiveWorkbook.Sheets[0];

        public GridView Grid => (GridView)_window.FindName("SheetGrid");

        public CellAddress ActiveCellAddress =>
            (CellAddress)(_selectionAnchorField.GetValue(_window)
                ?? throw new InvalidOperationException("No active cell is set."));

        public void SetActiveCell(CellAddress address)
        {
            _setActiveCell.Invoke(_window, [address]);
            PumpDispatcher();
        }

        public void PressKey(Key key)
        {
            var source = PresentationSource.FromVisual(_window)
                ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            _mainWindowKeyDown.Invoke(_window, [_window, args]);
            PumpDispatcher();
        }

        public void InvokeNudge(Key key, bool fine)
        {
            _nudgeSelectedDrawingObject.Invoke(_window, [key, fine]);
            PumpDispatcher();
        }

        public void InvokeEditCellShortcut()
        {
            var shortcut = Enum.Parse(_keyboardCommandShortcutType, "EditCell");
            _executeCommandShortcut.Invoke(_window, [shortcut, _window, new RoutedEventArgs()]);
            PumpDispatcher();
        }

        public void InvokeFillDownShortcut()
        {
            var shortcut = Enum.Parse(_keyboardCommandShortcutType, "FillDown");
            _executeCommandShortcut.Invoke(_window, [shortcut, _window, new RoutedEventArgs()]);
            PumpDispatcher();
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }
}
