using System.Reflection;
using System.Windows;
using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R143-remediation (clip-2-regression): App.xaml.cs's DI registration makes
/// <see cref="WorkbookClipboardSession"/> a process-wide singleton so a Ctrl+C in one open workbook
/// window can be Ctrl+V'd in another (matching real Excel: copy in one open workbook, paste into a
/// different one open in the same instance, and the formula survives). That sharing meant a purely
/// LOCAL, no-clipboard-intent gesture in window B -- Escape (<c>CancelCopyAndTransientModes</c>),
/// Delete/Clear Contents (<c>ExecuteClearSelection</c>), or Backspace (<c>ExecuteClearActiveCell</c>)
/// -- silently destroyed content window A copied and was still showing marching ants around, with
/// window A's marquee staying visible (now lying) and a subsequent Paste there producing nothing or
/// falling back to plain text with no indication why.
///
/// <para>
/// These tests construct two REAL <see cref="MainWindow"/> instances over two SEPARATE workbooks,
/// wire them to the SAME <see cref="WorkbookClipboardSession"/> instance -- exactly how the DI
/// container wires every window in the running app -- and drive the real private entry points via
/// reflection (Copy/Paste/Escape/Delete/Backspace), never constructing a
/// <see cref="WorkbookClipboardSnapshot"/> by hand. <see cref="StaTestRunner.RunClipboardIsolated"/>
/// is used because Copy/Paste here round-trip through the REAL OS clipboard, exactly like
/// FreeXCleanupMED2Tests's existing ExecutePaste-via-reflection tests.
/// </para>
/// </summary>
public sealed class R143_CrossWindowClipboardOwnershipTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("CancelCopyAndTransientModes")] // Escape
    [InlineData("ExecuteClearSelection")]        // Delete
    [InlineData("ExecuteClearActiveCell")]       // Backspace
    public void NoClipboardIntentActionInWindowB_LeavesWindowAsClipboardAndMarqueeIntact(
        string windowBLocalActionMethodName)
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = TwoWindowClipboardHarness.Create();

            // Window A: A1=5, B1="=A1*2", then Copy B1 -- populates the SHARED internal clipboard
            // session AND window A's own per-window marching-ants marquee.
            var a1 = new CellAddress(harness.A.Sheet.Id, 1, 1);
            var b1 = new CellAddress(harness.A.Sheet.Id, 1, 2);
            var d1 = new CellAddress(harness.A.Sheet.Id, 1, 4);
            harness.A.Sheet.SetCell(a1, new NumberValue(5));
            harness.A.Sheet.SetFormula(b1, "A1*2");
            harness.A.RecalcEngine.RecalculateAllFormulas(harness.A.Workbook);

            harness.A.Grid.SelectedRange = new GridRange(b1, b1);
            harness.A.InvokeClickHandler("CopyBtn_Click");
            PumpDispatcher();

            harness.SharedSession.HasContent.Should().BeTrue(
                "sanity: Copy in window A must populate the shared clipboard session");
            var capturedContent = harness.SharedSession.Content;
            harness.A.Grid.ClipboardRange.Should().NotBeNull(
                "sanity: Copy in window A must show its own marching ants");

            // Window B: a totally unrelated, DIFFERENT workbook that never copied anything. Give it
            // some content so Delete/Backspace are genuine (non-no-op) edits, then fire the purely
            // local, no-clipboard-intent action under test.
            var bA1 = new CellAddress(harness.B.Sheet.Id, 1, 1);
            harness.B.Sheet.SetCell(bA1, new NumberValue(99));
            harness.B.Grid.SelectedRange = new GridRange(bA1, bA1);
            harness.B.InvokePrivateParameterlessMethod(windowBLocalActionMethodName);
            PumpDispatcher();

            // Window B never copied anything, so it must never have shown a marquee of its own --
            // this rules out the fix trivially "passing" by accidentally rendering B's stale range.
            harness.B.Grid.ClipboardRange.Should().BeNull();

            // THE REGRESSION: window B's local gesture must not have destroyed window A's still-
            // valid copy, and window A's marquee (the visual promise a Paste will work) must still
            // agree with that -- the bug this remediates is exactly a case where content and marquee
            // silently disagreed.
            harness.SharedSession.HasContent.Should().BeTrue(
                $"{windowBLocalActionMethodName} in window B carries no clipboard intent and must not " +
                "clear the shared session window A owns");
            harness.SharedSession.Content.Should().BeSameAs(
                capturedContent,
                "window A's captured snapshot must be untouched, not merely non-null");
            harness.A.Grid.ClipboardRange.Should().NotBeNull(
                "window A's marquee must still agree with the shared session it owns");

            // Prove this isn't merely a field staying non-null: an ACTUAL Paste in window A -- via
            // the internal (formula-preserving) clipboard, which requires the real OS clipboard's
            // marker to still match -- must still reproduce the copied FORMULA, shifted for its new
            // column (B1 -> D1 is +2 columns, so A1*2 -> C1*2).
            harness.A.Grid.SelectedRange = new GridRange(d1, d1);
            harness.A.InvokeClickHandler("PasteBtn_Click");
            PumpDispatcher();

            var pasted = harness.A.Sheet.GetCell(d1);
            pasted.Should().NotBeNull();
            pasted!.FormulaText.Should().Be(
                "C1*2",
                "Paste in window A must still resolve through the internal clipboard after window B's " +
                $"local {windowBLocalActionMethodName} gesture, proving A can still genuinely paste " +
                "(not merely that a flag stayed set)");
        });
    }

    // No-regression sibling: a genuine new Copy IS clipboard intent and legitimately supersedes
    // whatever was on the shared clipboard before, exactly like the real OS clipboard -- this must
    // keep working (deliberately NOT gated by ownership) even though it, too, clears the shared
    // session out from under a previous owner.
    [Fact]
    public void GenuineCopyInWindowB_StillReplacesWindowAsSharedClipboardContent()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            using var harness = TwoWindowClipboardHarness.Create();

            var a1 = new CellAddress(harness.A.Sheet.Id, 1, 1);
            harness.A.Sheet.SetCell(a1, new NumberValue(1));
            harness.A.Grid.SelectedRange = new GridRange(a1, a1);
            harness.A.InvokeClickHandler("CopyBtn_Click");
            PumpDispatcher();
            var firstCapture = harness.SharedSession.Content;
            firstCapture.Should().NotBeNull();

            var bA1 = new CellAddress(harness.B.Sheet.Id, 1, 1);
            harness.B.Sheet.SetCell(bA1, new NumberValue(2));
            harness.B.Grid.SelectedRange = new GridRange(bA1, bA1);
            harness.B.InvokeClickHandler("CopyBtn_Click");
            PumpDispatcher();

            harness.SharedSession.HasContent.Should().BeTrue();
            harness.SharedSession.Content.Should().NotBeSameAs(
                firstCapture,
                "a genuine new Copy in window B is real clipboard intent and must replace the shared " +
                "content, exactly like copying elsewhere on the real OS clipboard");
        });
    }

    private sealed class SingleWindowContext(MainWindow window, Workbook workbook, RecalcEngine recalcEngine)
    {
        public MainWindow Window { get; } = window;
        public Workbook Workbook { get; } = workbook;
        public RecalcEngine RecalcEngine { get; } = recalcEngine;
        public Sheet Sheet { get; } = workbook.GetSheetAt(0);
        public FreeX.App.UI.GridView Grid { get; } = (FreeX.App.UI.GridView)window.FindName("SheetGrid");

        public void InvokeClickHandler(string methodName)
        {
            var method = typeof(MainWindow).GetMethod(
                methodName,
                PrivateInstance,
                [typeof(object), typeof(RoutedEventArgs)]);
            method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
            method!.Invoke(Window, [Window, new RoutedEventArgs()]);
        }

        public void InvokePrivateParameterlessMethod(string methodName)
        {
            var method = typeof(MainWindow).GetMethod(methodName, PrivateInstance, Type.EmptyTypes);
            method.Should().NotBeNull($"{methodName} should exist as a private parameterless method on MainWindow");
            method!.Invoke(Window, []);
        }
    }

    private sealed class TwoWindowClipboardHarness : IDisposable
    {
        public WorkbookClipboardSession SharedSession { get; }
        public SingleWindowContext A { get; }
        public SingleWindowContext B { get; }

        private TwoWindowClipboardHarness(
            WorkbookClipboardSession sharedSession, SingleWindowContext a, SingleWindowContext b)
        {
            SharedSession = sharedSession;
            A = a;
            B = b;
        }

        public static TwoWindowClipboardHarness Create()
        {
            // The SAME WorkbookClipboardSession instance handed to both windows' constructors is
            // exactly what App.xaml.cs's `services.AddSingleton<WorkbookClipboardSession>()` +
            // ActivatorUtilities.CreateInstance<MainWindow> produces for every window opened through
            // DI in the real app (see MainWindow.xaml.cs constructor comment).
            var sharedSession = new WorkbookClipboardSession();
            var aInit = ConstructWindow("A.xlsx", "Sheet1", sharedSession);
            var bInit = ConstructWindow("B.xlsx", "Sheet1", sharedSession);

            aInit.Window.Show();
            bInit.Window.Show();
            PumpDispatcher();

            // MainWindow's Loaded-time initialization (fired only once Show()/the dispatcher pump
            // actually runs, NOT synchronously inside the constructor) can replace
            // WorkbookRef.Current with its own WorkbookDocumentContext-managed workbook instance --
            // re-read it only now, exactly like every other multi-window test in this project
            // (e.g. R90_CrossWorkbookFormulaPointModeWpfTests, MainWindowClipboardCutMoveTests),
            // never before Show()+pump has actually run.
            var a = new SingleWindowContext(aInit.Window, aInit.WorkbookRef.Current, aInit.RecalcEngine);
            var b = new SingleWindowContext(bInit.Window, bInit.WorkbookRef.Current, bInit.RecalcEngine);

            return new TwoWindowClipboardHarness(sharedSession, a, b);
        }

        private static (MainWindow Window, WorkbookRef WorkbookRef, RecalcEngine RecalcEngine) ConstructWindow(
            string workbookName, string sheetName, WorkbookClipboardSession sharedSession)
        {
            var initialWorkbook = new Workbook(workbookName);
            initialWorkbook.AddSheet(sheetName);
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                recalcEngine,
                Array.Empty<IFileAdapter>(),
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance,
                workbookClipboardSession: sharedSession);
            return (window, workbookRef, recalcEngine);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(A.Window);
            MainWindowTestCleanup.CloseWithoutSavePrompt(B.Window);
            PumpDispatcher();
        }
    }
}
