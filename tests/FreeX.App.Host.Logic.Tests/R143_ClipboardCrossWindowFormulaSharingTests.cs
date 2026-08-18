using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Free.Shared.AppServices;
using FreeX.App.Presentation.Editing;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R143 clip-2: <c>MainWindow.ClipboardCommands.cs</c>'s internal
/// formula-preserving clipboard (<c>_workbookClipboardSession</c>) used to be re-created fresh
/// (<c>= new()</c>) for every <see cref="MainWindow"/>, so copying a formula in one open FreeX
/// window and pasting into a DIFFERENT window open in the SAME running instance always fell back
/// to the OS clipboard's plain display text -- losing the formula, unlike real Excel. The fix
/// threads an optional <see cref="WorkbookClipboardSession"/> constructor parameter through
/// <c>MainWindow</c> that the production DI factory (<c>App.xaml.cs</c> <c>ConfigureServices</c>)
/// resolves from a singleton registration, so every window opened via
/// <c>Services.GetRequiredService&lt;MainWindow&gt;()</c> (or <c>ActivatorUtilities.CreateInstance</c>
/// against the same provider, as <c>ViewNewWindowBtn_Click</c> does) shares one process-wide session.
/// </summary>
public sealed class R143_ClipboardCrossWindowFormulaSharingTests
{
    [Fact]
    public void ExecutePaste_AcrossTwoWindows_WithSharedClipboardSession_RelocatesFormula()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            // Models exactly what the production DI singleton registration does: hand both windows
            // the SAME WorkbookClipboardSession instance, as if both were resolved through
            // Services.GetRequiredService<MainWindow>() in one running FreeX.exe process.
            var sharedClipboard = new WorkbookClipboardSession();
            using var windowA = new Harness(sharedClipboard);
            using var windowB = new Harness(sharedClipboard);

            var sheetA = windowA.Workbook.GetSheetAt(0);
            var a1 = new CellAddress(sheetA.Id, 1, 1); // A1
            var b1 = new CellAddress(sheetA.Id, 1, 2); // B1
            sheetA.SetCell(a1, new NumberValue(5));
            sheetA.SetFormula(b1, "A1");

            windowA.SetSelectedRange(new GridRange(b1, b1));
            windowA.InvokeClickHandler("CopyBtn_Click");
            windowA.HasInternalClipboard.Should().BeTrue(
                "Copy must populate the shared internal clipboard session");

            var sheetB = windowB.Workbook.GetSheetAt(0);
            var d1 = new CellAddress(sheetB.Id, 1, 4); // D1, in a DIFFERENT window's workbook
            windowB.SetSelectedRange(new GridRange(d1, d1));
            windowB.InvokeClickHandler("PasteBtn_Click");

            sheetB.GetCell(d1)!.FormulaText.Should().Be(
                "C1",
                "pasting into a different open window's workbook in the same running instance must " +
                "relocate the copied formula (B1's \"A1\" offset to D1 becomes \"C1\"), exactly like " +
                "real Excel preserves formulas pasted between two workbooks open in one instance -- " +
                "not degrade to the OS clipboard's flattened display-text value");
        });
    }

    [Fact]
    public void ExecutePaste_AcrossTwoWindows_WithoutASharedSession_StaysIsolatedPerWindow()
    {
        // Sibling no-regression check: every OTHER existing clipboard test constructs a MainWindow
        // without passing the new optional parameter (exactly like every call site outside the DI
        // container's factory). Two such windows must NOT accidentally share clipboard state -- the
        // fix must not have turned _workbookClipboardSession into a bare process-wide static.
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var windowA = new Harness(sharedClipboard: null);
            using var windowB = new Harness(sharedClipboard: null);

            var sheetA = windowA.Workbook.GetSheetAt(0);
            var a1 = new CellAddress(sheetA.Id, 1, 1);
            var b1 = new CellAddress(sheetA.Id, 1, 2);
            sheetA.SetCell(a1, new NumberValue(5));
            sheetA.SetFormula(b1, "A1");

            windowA.SetSelectedRange(new GridRange(b1, b1));
            windowA.InvokeClickHandler("CopyBtn_Click");
            windowA.HasInternalClipboard.Should().BeTrue();

            windowB.HasInternalClipboard.Should().BeFalse(
                "a window that was never handed the shared session must not observe another " +
                "window's copy -- only the DI-resolved production path shares one instance");

            var sheetB = windowB.Workbook.GetSheetAt(0);
            var d1 = new CellAddress(sheetB.Id, 1, 4);
            windowB.SetSelectedRange(new GridRange(d1, d1));
            windowB.InvokeClickHandler("PasteBtn_Click");

            sheetB.GetCell(d1).Should().BeNull(
                "with no shared session and nothing else on the OS/internal clipboard for this " +
                "window, Paste must remain a no-op, matching every other pre-existing clipboard test");
        });
    }

    private sealed class Harness : IDisposable
    {
        private readonly FieldInfo _clipboardSessionField;

        public MainWindow Window { get; }
        public Workbook Workbook { get; }
        public GridView Grid => (GridView)Window.FindName("SheetGrid");

        public Harness(WorkbookClipboardSession? sharedClipboard)
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
                new RecordingUserMessageService(),
                workbookClipboardSession: sharedClipboard);

            Window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied
            // workbook with a fresh one unless adopting a shared document -- capture the *live*
            // workbook afterward, mirroring every other MainWindow-construction test harness.
            Workbook = workbookRef.Current;

            _clipboardSessionField = typeof(MainWindow)
                .GetField("_workbookClipboardSession", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_workbookClipboardSession");
        }

        public bool HasInternalClipboard =>
            ((WorkbookClipboardSession)_clipboardSessionField.GetValue(Window)!).HasContent;

        public void SetSelectedRange(GridRange range)
        {
            Grid.SelectedRanges = null;
            Grid.SelectedRange = range;
        }

        public void InvokeClickHandler(string methodName)
        {
            var method = typeof(MainWindow).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic,
                [typeof(object), typeof(RoutedEventArgs)]);
            method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
            method!.Invoke(Window, [Window, new RoutedEventArgs()]);
            PumpDispatcher();
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in Window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            Window.SuppressNextClosePrompt();
            Window.Close();
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>No-op message recorder -- these tests don't assert on user-facing messages.</summary>
    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => false;
        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Ok;
    }
}
