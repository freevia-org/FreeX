using System.IO;
using System.Windows;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for J25: a secondary window opened over an already-open workbook
/// (Excel-style "New Window") must get its own autosave timer + recovery snapshot, and closing
/// one window sharing a workbook must never strip autosave/crash-recovery coverage from a
/// surviving sibling window that is still open over the same (shared, still-dirty) workbook.
/// </summary>
public sealed class MultiWindowAutosaveOwnershipTests
{
    /// <summary>Self-contained temp directory helper (avoids relying on another test project's internal type).</summary>
    private sealed class RecoveryTempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public RecoveryTempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    private static MainWindow CreateWindow(
        WorkbookRef workbookRef,
        WorkbookWindowRegistry registry,
        WorkbookDocumentState documentState)
    {
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
            documentState,
            windowRegistry: registry)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };

        return window;
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static int SnapshotFileCount(string recoveryDirectory) =>
        Directory.Exists(recoveryDirectory)
            ? Directory.GetFiles(recoveryDirectory, "*.fxl").Length
            : 0;

    [Fact]
    public void ViewNewWindow_AttachesIndependentAutosaveToSecondaryWindow()
    {
        StaTestRunner.Run(() =>
        {
        using var temp = new RecoveryTempDirectory();
        var store = new AutosaveSnapshotStore(temp.Path);

        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        var workbookRef = new WorkbookRef { Current = workbook };
        var registry = new WorkbookWindowRegistry();
        var documentState = new WorkbookDocumentState();

        var primary = CreateWindow(workbookRef, registry, documentState);
        primary.AttachAutosaveService(new AutosaveService(store), store);
        primary.Show();
        primary.Activate();
        PumpDispatcher();

        // Simulate the fixed ViewNewWindowBtn_Click wiring: a secondary window over the same
        // shared workbook + registry must also get AttachAutosaveService called on it.
        var secondary = CreateWindow(workbookRef, registry, documentState);
        secondary.AttachAutosaveService(new AutosaveService(store), store);
        secondary.Show();
        secondary.Activate();
        PumpDispatcher();

        try
        {
            registry.Count.Should().Be(2);

            documentState.MarkDirty();

            // Both windows' autosave services must be independently wired (non-null) and able
            // to produce their own snapshot on tick — this is the part that was missing for the
            // secondary window before the fix (it stayed null forever).
            primary.AutosaveServiceForCrashHandler.Should().NotBeNull();
            secondary.AutosaveServiceForCrashHandler.Should().NotBeNull();
            secondary.AutosaveServiceForCrashHandler.Should().NotBeSameAs(primary.AutosaveServiceForCrashHandler);

            primary.AutosaveServiceForCrashHandler!.OnTimerTick();
            secondary.AutosaveServiceForCrashHandler!.OnTimerTick();

            // Each window ticks against the same dirty shared workbook, but into its own
            // uniquely-tagged snapshot file, so there should be exactly two recovery files.
            SnapshotFileCount(temp.Path).Should().Be(2);
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(secondary);
            MainWindowTestCleanup.CloseWithoutSavePrompt(primary);
            PumpDispatcher();
        }
        });
    }

    [Fact]
    public void ClosingOneWindow_DoesNotDeleteSurvivingWindowsSnapshotOrStopItsTimer()
    {
        StaTestRunner.Run(() =>
        {
        using var temp = new RecoveryTempDirectory();
        var store = new AutosaveSnapshotStore(temp.Path);

        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        var workbookRef = new WorkbookRef { Current = workbook };
        var registry = new WorkbookWindowRegistry();
        var documentState = new WorkbookDocumentState();

        var primary = CreateWindow(workbookRef, registry, documentState);
        primary.AttachAutosaveService(new AutosaveService(store), store);
        primary.Show();
        primary.Activate();
        PumpDispatcher();

        var secondary = CreateWindow(workbookRef, registry, documentState);
        secondary.AttachAutosaveService(new AutosaveService(store), store);
        secondary.Show();
        secondary.Activate();
        PumpDispatcher();

        // The shared workbook (shared WorkbookDocumentState, per DI's AddSingleton<WorkbookDocumentState>)
        // is dirtied, and both windows produce their own recovery snapshot.
        documentState.MarkDirty();
        primary.AutosaveServiceForCrashHandler!.OnTimerTick();
        secondary.AutosaveServiceForCrashHandler!.OnTimerTick();
        SnapshotFileCount(temp.Path).Should().Be(2);

        // User closes the primary window (e.g. via "Don't Save") while the secondary window
        // remains open over the still-dirty shared workbook — the routine multi-window scenario
        // from the finding. Only the primary's own recovery snapshot must be deleted; the
        // secondary's snapshot (and, by extension, its still-running timer/coverage) must survive.
        MainWindowTestCleanup.CloseWithoutSavePrompt(primary);
        PumpDispatcher();

        SnapshotFileCount(temp.Path).Should().Be(1, "the surviving secondary window's own snapshot must not be deleted " +
            "when a sibling window sharing the workbook closes");

        // The still-open secondary window keeps producing snapshots for the still-dirty shared
        // workbook on subsequent ticks — autosave coverage was not silently lost.
        documentState.MarkDirty();
        secondary.AutosaveServiceForCrashHandler!.OnTimerTick();
        SnapshotFileCount(temp.Path).Should().Be(1);

        MainWindowTestCleanup.CloseWithoutSavePrompt(secondary);
        PumpDispatcher();

        SnapshotFileCount(temp.Path).Should().Be(0, "closing the last window over the workbook cleans up its own snapshot too");
        });
    }
}
