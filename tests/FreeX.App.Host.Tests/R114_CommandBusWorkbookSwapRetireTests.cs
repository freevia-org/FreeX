using System.Reflection;
using System.Windows;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R114-commands-workbook-retire-1: the WPF host gives every <see cref="MainWindow"/> one
/// app-lifetime <see cref="CommandBus"/> instance (see <c>App.CreateWorkbookCommandBus</c>). When
/// there is no "New Window" sibling still viewing the outgoing document, File &gt; Open / File &gt;
/// New must retire the outgoing workbook's entry from that SAME bus instance (mirroring the
/// existing <c>_recalcEngine.RetireWorkbook(outgoingWorkbook)</c> call right next to it) -- or the
/// outgoing workbook's up-to-50MB undo/redo stack stays a live, unreachable dictionary entry in
/// <see cref="CommandBus"/> for the rest of the process's life. See
/// <c>MainWindow.AdoptWorkbookAsInitial</c>/<c>MainWindow.OpenFileAsync</c>.
/// </summary>
public sealed class R114_CommandBusWorkbookSwapRetireTests
{
    private sealed class NoOpTestCommand : IWorkbookCommand
    {
        public string Label => "R114 test command";
        public CommandOutcome Apply(ICommandContext ctx) => new(true);
        public void Revert(ICommandContext ctx) { }
    }

    /// <summary>Minimal <see cref="IWorkbookWindow"/> fake used purely so the registry reports a
    /// sibling for a given document, matching the pattern in
    /// R90_NewWindowSourceHintSheetResolutionTests.</summary>
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

    private static MainWindow CreateWindow(
        ICommandBus commandBus,
        WorkbookRef workbookRef,
        WorkbookWindowRegistry? registry = null)
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        return new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            commandBus,
            new RecalcEngine(graph, evaluator),
            [],
            workbookRef,
            workbookRef.Current,
            NullUserMessageService.Instance,
            windowRegistry: registry)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static Workbook GetCurrentWorkbook(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_workbook", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (Workbook)field!.GetValue(window)!;
    }

    private static void InvokeCreateNewWorkbook(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("CreateNewWorkbook", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(window, null);
    }

    [Fact]
    public void FileNew_NoSiblingWindow_RetiresOutgoingWorkbooksUndoStackFromTheSharedCommandBus() =>
        StaTestRunner.Run(() =>
        {
            var seedWorkbook = new Workbook("Seed");
            seedWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = seedWorkbook };
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));

            // No window registry is passed, matching the ordinary single-window session: the
            // shared-document adopt branch never triggers, so every workbook swap below takes the
            // "outgoing workbook is fully replaced" path this defect concerns.
            var window = CreateWindow(commandBus, workbookRef);
            try
            {
                window.Show();
                window.Activate();
                PumpDispatcher(); // MainWindow_Loaded -> CreateNewWorkbook() swaps seedWorkbook out.

                var firstWorkbook = GetCurrentWorkbook(window);

                // Give the window's current workbook some undo history, exactly as real editing
                // would via ICommandBus.Execute -- this is the SAME CommandBus instance the window
                // holds for its whole lifetime.
                commandBus.Execute(firstWorkbook.Id, new NoOpTestCommand()).Success.Should().BeTrue();
                commandBus.CanUndo(firstWorkbook.Id).Should().BeTrue(
                    "the pushed command must land on the live workbook's undo stack before we swap it out");

                // File > New, reusing this same window with no sibling view of firstWorkbook.
                InvokeCreateNewWorkbook(window);

                GetCurrentWorkbook(window).Id.Should().NotBe(firstWorkbook.Id, "the workbook must have been replaced");
                commandBus.CanUndo(firstWorkbook.Id).Should().BeFalse(
                    "File > New reusing this window must retire the outgoing workbook's undo/redo stack " +
                    "from the shared, app-lifetime CommandBus -- otherwise it leaks for the rest of the process");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });

    [Fact]
    public void FileNew_SiblingStillViewingDocument_LeavesOutgoingWorkbooksUndoStackIntact() =>
        StaTestRunner.Run(() =>
        {
            var seedWorkbook = new Workbook("Seed");
            seedWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = seedWorkbook };
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var registry = new WorkbookWindowRegistry();

            // Registry is empty at construction time, so this window still takes the ordinary
            // CreateNewWorkbook() startup path (ShouldAdoptSharedWorkbookOnLoad is false) and then
            // self-registers under whatever workbook it lands on.
            var window = CreateWindow(commandBus, workbookRef, registry);
            try
            {
                window.Show();
                window.Activate();
                PumpDispatcher();

                var firstWorkbook = GetCurrentWorkbook(window);
                commandBus.Execute(firstWorkbook.Id, new NoOpTestCommand()).Success.Should().BeTrue();
                commandBus.CanUndo(firstWorkbook.Id).Should().BeTrue();

                // Now a "New Window" sibling appears over the SAME document -- register a
                // placeholder for it so DocumentSharedWithOtherWindows() is true for this window.
                registry.Register(new DocumentPlaceholderWindow(firstWorkbook.Id));

                // File > New now takes the DetachFromSharedDocumentContext() branch: this window
                // switches to a brand-new CommandBus instance, but the OLD instance (still held by
                // this test, standing in for the sibling window) must keep firstWorkbook's undo
                // history intact -- the sibling is still viewing it.
                InvokeCreateNewWorkbook(window);

                GetCurrentWorkbook(window).Id.Should().NotBe(firstWorkbook.Id);
                commandBus.CanUndo(firstWorkbook.Id).Should().BeTrue(
                    "a document still shared with a 'New Window' sibling must keep its undo history " +
                    "on the original CommandBus instance -- only the detaching window gets a fresh bus");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
}
