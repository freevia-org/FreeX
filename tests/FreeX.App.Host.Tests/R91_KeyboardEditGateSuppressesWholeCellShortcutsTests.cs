using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Free.Shared.AppServices;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R91-app-keyboard-routing-5-1: Ctrl+B/I/U/5 (and the other whole-cell/
/// whole-selection shortcuts routed alongside them) must not fire while the in-place cell editor
/// TextBox has focus -- Excel never lets these escape the cell-edit gate to silently mutate the
/// whole cell's style while the user is still mid-edit. Drives the real (now-extracted)
/// <c>TryHandleWholeCellKeyboardShortcuts</c> choke point via reflection with an explicit
/// <see cref="ModifierKeys"/> value (rather than depending on real OS-level keyboard state, which
/// unit tests cannot reliably fake) while <see cref="Keyboard.FocusedElement"/> -- ordinary WPF
/// logical focus, fully controllable here -- is genuinely set to the real inline editor TextBox.
/// </summary>
public sealed class R91_KeyboardEditGateSuppressesWholeCellShortcutsTests
{
    private sealed class DocumentPlaceholderWindow(WorkbookId documentId) : IWorkbookWindow
    {
        public WorkbookId DocumentId { get; } = documentId;
        public void ApplyWindowTitleSuffix(string suffix) { }
        public void RefreshFromSharedWorkbook() { }
        public void RefreshTitleBar() { }
        public void ActivateWindow() { }
        public void SetWindowVisible(bool visible) { }
        public WorkbookScrollOffset GetScrollOffset() => default;
        public void SetScrollOffset(WorkbookScrollOffset offset) { }
        public void TileToWorkArea(Rect bounds) { }
        public void ApplyFormulaBarVisibility(bool visible) { }
        public void ApplySaveInProgress(bool inProgress) { }
    }

    private static (MainWindow Window, Workbook Workbook, Sheet Sheet) CreateAdoptedWindow()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var workbookRef = new WorkbookRef { Current = workbook };
        var registry = new WorkbookWindowRegistry();
        registry.Register(new DocumentPlaceholderWindow(workbook.Id));

        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
        var window = new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            commandBus,
            new RecalcEngine(graph, evaluator),
            [],
            workbookRef,
            workbookRef.Current,
            NullUserMessageService.Instance,
            new WorkbookDocumentState(),
            windowRegistry: registry)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };

        window.Show();
        window.Activate();
        window.UpdateLayout();
        PumpDispatcher();

        return (window, workbook, sheet);
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static void SetActiveCell(MainWindow window, CellAddress cell)
    {
        var method = typeof(MainWindow).GetMethod("SetActiveCell", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(window, [cell]);
        PumpDispatcher();
    }

    private static void ShowInlineEditor(MainWindow window, CellAddress cell)
    {
        var method = typeof(MainWindow).GetMethod(
            "ShowInlineEditor",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(CellAddress), typeof(double?)],
            modifiers: null);
        method.Should().NotBeNull();
        method!.Invoke(window, [cell, null]);
        PumpDispatcher();
    }

    private static bool? InvokeGate(MainWindow window, Key key, ModifierKeys modifiers, out bool handled)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };

        var method = typeof(MainWindow).GetMethod(
            "TryHandleWholeCellKeyboardShortcuts", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (bool)method!.Invoke(window, [window, args, modifiers])!;
        handled = args.Handled;
        PumpDispatcher();
        return result;
    }

    private static bool IsCellBold(Workbook workbook, Sheet sheet, CellAddress cell)
    {
        var styleId = sheet.GetCell(cell)?.StyleId ?? StyleId.Default;
        return workbook.GetStyle(styleId).Bold;
    }

    [Fact]
    public void CtrlB_WhileInlineEditorFocused_DoesNotMutateTheWholeCellStyle() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, Cell.FromValue(new TextValue("Hello")));

            SetActiveCell(window, a1);
            ShowInlineEditor(window, a1);

            var inlineEditorField = typeof(MainWindow).GetField("_inlineEditor", BindingFlags.Instance | BindingFlags.NonPublic);
            var inlineEditor = (TextBox?)inlineEditorField!.GetValue(window);
            inlineEditor.Should().NotBeNull("ShowInlineEditor must create and focus the in-place cell editor");
            Keyboard.FocusedElement.Should().Be(inlineEditor, "the in-place editor must hold real keyboard focus for this test to be meaningful");

            var handledResult = InvokeGate(window, Key.B, ModifierKeys.Control, out var handled);

            handledResult.Should().BeFalse(
                "Ctrl+B while the in-place cell editor has focus must not be treated as a whole-cell shortcut");
            handled.Should().BeFalse("the key must fall through unhandled to the TextBox's own key handling");
            IsCellBold(workbook, sheet, a1).Should().BeFalse(
                "Ctrl+B must not silently mutate the whole cell's style while the cell is still being edited");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    /// <summary>No-regression sibling: with the grid (not the in-place editor) focused -- ordinary
    /// navigation-mode Ctrl+B -- the shortcut must still apply Bold to the selected cell as before.</summary>
    [Fact]
    public void CtrlB_WhileGridFocused_StillTogglesBoldOnTheSelectedCell() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, Cell.FromValue(new TextValue("Hello")));

            SetActiveCell(window, a1);
            var grid = (SheetGridView)window.FindName("SheetGrid");
            grid.Focus();
            Keyboard.Focus(grid);
            PumpDispatcher();

            IsCellBold(workbook, sheet, a1).Should().BeFalse("sanity: the cell starts out not bold");

            var handledResult = InvokeGate(window, Key.B, ModifierKeys.Control, out var handled);

            handledResult.Should().BeTrue("Ctrl+B in navigation mode (grid focused) must be handled as the Bold toggle");
            handled.Should().BeTrue();
            IsCellBold(workbook, sheet, a1).Should().BeTrue(
                "Ctrl+B in navigation mode must still toggle Bold on the active cell as before this fix");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });
}
