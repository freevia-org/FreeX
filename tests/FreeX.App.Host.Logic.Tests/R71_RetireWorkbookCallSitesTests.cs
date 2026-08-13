using System.Reflection;
using Free.Shared.AppServices;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R71-calc-volatile-recalc-4-1's App.Host call sites: File &gt; New
/// (<c>MainWindow.AdoptWorkbookAsInitial</c> in <c>src/FreeX.App.Host/MainWindow.Backstage.cs</c>)
/// must retire the outgoing workbook's sheets from the shared app-lifetime <see cref="RecalcEngine"/>
/// before dropping the reference -- but only when no "New Window" sibling still shares that
/// document (H39), otherwise the sibling's live tracking would be pulled out from under it.
/// </summary>
public sealed class R71_RetireWorkbookCallSitesTests
{
    [Fact]
    public void CreateNewWorkbook_NoSiblingWindow_RetiresOutgoingWorkbookFromEngine()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;
            var sheet = workbook.GetSheetAt(0);

            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 1, 2);
            sheet.SetFormula(a1, "B1");
            sheet.SetFormula(b1, "A1");
            harness.RecalculateWorkbook();

            harness.RecalcEngine.CyclicCells.Should().NotBeEmpty(
                "the seeded circular formula must be tracked before File > New replaces the workbook");

            harness.CreateNewWorkbook();

            harness.RecalcEngine.CyclicCells.Should().BeEmpty(
                "File > New with no sibling window must retire the outgoing workbook's sheets from " +
                "the shared RecalcEngine, and the fresh replacement workbook has no circular reference");
        });
    }

    [Fact]
    public void CreateNewWorkbook_SharedWithSiblingWindow_DoesNotRetireSharedWorkbook()
    {
        // Sibling/no-regression case: a "New Window" sibling still views the document, so File >
        // New on the primary window must detach into a fresh context instead of retiring the
        // shared workbook -- the sibling's cyclic-cell tracking must survive untouched (H39).
        StaTestRunner.Run(() =>
        {
            using var harness = new SharedMainWindowHarness();
            var sheet = harness.Workbook.GetSheetAt(0);

            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 1, 2);
            sheet.SetFormula(a1, "B1");
            sheet.SetFormula(b1, "A1");
            harness.RecalculatePrimary();

            harness.RecalcEngine.CyclicCells.Should().NotBeEmpty(
                "the seeded circular formula on the shared document must be tracked");

            harness.CreateNewWorkbookOnPrimary();

            harness.RecalcEngine.CyclicCells.Should().NotBeEmpty(
                "a sibling window still views the shared workbook, so its cyclic cells must remain " +
                "tracked after the primary window's File > New detaches instead of retiring it");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        public MainWindow Window { get; }
        public Workbook Workbook { get; }
        public RecalcEngine RecalcEngine { get; }

        public MainWindowHarness()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            RecalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
            Window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                RecalcEngine,
                [],
                workbookRef,
                initialWorkbook,
                new RecordingUserMessageService());

            Window.Show();
            PumpDispatcher();

            // MainWindow_Loaded replaces the constructor-supplied workbook with a fresh one (see
            // R22/R46/R49 harnesses) -- capture the *live* workbook afterward.
            Workbook = workbookRef.Current;
        }

        public void RecalculateWorkbook() => Invoke(Window, "RecalculateWorkbook");

        public void CreateNewWorkbook() => Invoke(Window, "CreateNewWorkbook");

        public void Dispose()
        {
            Window.SuppressNextClosePrompt();
            Window.Close();
            PumpDispatcher();
        }
    }

    private sealed class SharedMainWindowHarness : IDisposable
    {
        private readonly MainWindow _primary;
        private readonly MainWindow _secondary;

        public RecalcEngine RecalcEngine { get; }
        public Workbook Workbook { get; }

        public SharedMainWindowHarness()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var registry = new WorkbookWindowRegistry();
            var documentState = new WorkbookDocumentState();
            RecalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));

            _primary = CreateWindow(workbookRef, registry, documentState, RecalcEngine, commandBus);
            _primary.Show();
            PumpDispatcher();

            // Constructed AFTER the primary's Loaded event has replaced workbookRef.Current with
            // its own fresh Book1 and registered itself: this window's HasWindowForDocument check
            // now finds the primary, so it adopts the shared workbook instead of creating its own
            // (Excel "New Window" semantics, H39).
            _secondary = CreateWindow(
                workbookRef,
                registry,
                documentState,
                RecalcEngine,
                commandBus,
                _primary.Session.CreateSiblingView(600, 800));
            _secondary.Show();
            PumpDispatcher();

            Workbook = workbookRef.Current;
        }

        private static MainWindow CreateWindow(
            WorkbookRef workbookRef,
            WorkbookWindowRegistry registry,
            WorkbookDocumentState documentState,
            RecalcEngine engine,
            ICommandBus commandBus,
            WorkbookSession? session = null) =>
            new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                engine,
                [],
                workbookRef,
                workbookRef.Current,
                new RecordingUserMessageService(),
                documentState,
                windowRegistry: registry,
                workbookSession: session);

        public void RecalculatePrimary() => Invoke(_primary, "RecalculateWorkbook");

        public void CreateNewWorkbookOnPrimary() => Invoke(_primary, "CreateNewWorkbook");

        public void Dispose()
        {
            _secondary.SuppressNextClosePrompt();
            _secondary.Close();
            PumpDispatcher();
            _primary.SuppressNextClosePrompt();
            _primary.Close();
            PumpDispatcher();
        }
    }

    private static void Invoke(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, [])
            ?? throw new MissingMethodException(nameof(MainWindow), methodName);
        method.Invoke(window, []);
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// No-op <see cref="IUserMessageService"/> for tests that construct <see cref="MainWindow"/>
    /// directly and don't want real WPF MessageBox windows popping up.
    /// </summary>
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
