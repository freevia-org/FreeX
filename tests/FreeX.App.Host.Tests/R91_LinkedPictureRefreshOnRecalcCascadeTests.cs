using System.Reflection;
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
/// Regression coverage for R91-print-twin-two-tier-synthetic-sweep-3: r90's linked/Camera-picture
/// live refresh (<see cref="MainWindow"/>.RefreshLinkedPicturesAffectedBy) only ever fed it
/// outcome.AffectedCells -- the cells the triggering command DIRECTLY edited. A formula cell inside
/// the picture's LinkedSourceRange that only changes because some OTHER, out-of-range cell it
/// depends on was edited (a RecalcEngine-cascaded recalculation) never appeared in that set, so the
/// picture kept showing its stale pre-edit value forever. The fix feeds RecalculateIfAutomatic's own
/// RecalcReport.RecalculatedCells back into RefreshLinkedPicturesAffectedBy too. Drives the real
/// private TryExecuteCommand/RecalculateIfAutomatic choke points via reflection (the same command
/// pipeline every real edit-handler call site in MainWindow.HomeEditing.cs/CellsCommands.cs reaches)
/// rather than mutating the PictureModel directly.
/// </summary>
public sealed class R91_LinkedPictureRefreshOnRecalcCascadeTests
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

    /// <summary>Drives the exact same private choke point every real cell-edit command in
    /// MainWindow.CommandExecution.cs ultimately reaches.</summary>
    private static bool ExecuteCommandThroughRealChokePoint(MainWindow window, IWorkbookCommand command)
    {
        var method = typeof(MainWindow).GetMethod(
            "TryExecuteCommand",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(IWorkbookCommand), typeof(string)],
            modifiers: null);
        method.Should().NotBeNull();
        return (bool)method!.Invoke(window, [command, "Test Command"])!;
    }

    /// <summary>Drives the exact same private RecalculateIfAutomatic choke point every real
    /// edit-handler call site (MainWindow.HomeEditing.cs/CellsCommands.cs/etc.) calls after
    /// TryExecuteCommand returns.</summary>
    private static void InvokeRecalculateIfAutomatic(MainWindow window, IReadOnlyList<CellAddress> changedCells)
    {
        var method = typeof(MainWindow).GetMethod(
            "RecalculateIfAutomatic", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(window, [changedCells]);
        PumpDispatcher();
    }

    [Fact]
    public void EditingACellOutsideTheLinkedRange_ThatCascadesARecalcIntoIt_RefreshesTheLinkedPicture() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 1, 2);
            var sourceRange = new GridRange(b1, b1);
            var destination = new CellAddress(sheet.Id, 5, 4);

            // A1 = 5, B1 = A1*2 (a formula cell). Seed both and let a Calculate Full pass build the
            // dependency graph and cached values, mirroring how a real, previously-opened workbook's
            // formulas are already tracked before any further edit happens.
            sheet.SetCell(a1, Cell.FromValue(new NumberValue(5)));
            sheet.SetCell(b1, Cell.FromFormula("A1*2"));
            DialogSourceTestSupport.InvokePrivateHandler(window, "CalcFullBtn_Click");
            sheet.GetValue(b1).Should().Be(new NumberValue(10));

            // Paste Special > Linked Picture of B1 (the same PasteRangeAsPictureCommand
            // MainWindow.ClipboardCommands.cs constructs for that feature), run through the same
            // choke point.
            ExecuteCommandThroughRealChokePoint(
                window,
                new PasteRangeAsPictureCommand(
                    sheet.Id,
                    sourceRange,
                    [(b1, "10")],
                    destination,
                    isLinkedToSourceRange: true,
                    sourceSheetName: sheet.Name)).Should().BeTrue();

            var picture = sheet.Pictures.Should().ContainSingle().Subject;
            picture.Cells.Should().Contain(c => c.Text == "10");

            // The failure scenario: edit A1 (OUTSIDE the picture's source range B1) and let the
            // workbook recalculate automatically -- exactly the two-step every real edit-handler
            // call site performs (TryExecuteCommand, then RecalculateIfAutomatic with the command's
            // own AffectedCells). B1 never appears in outcome.AffectedCells (only A1 does); B1 only
            // shows up in the RecalcEngine's own cascaded RecalculatedCells.
            var editOutcome = default(CommandOutcome);
            var tryExecute = typeof(MainWindow).GetMethod(
                "TryExecuteCommand",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(IWorkbookCommand), typeof(string), typeof(CommandOutcome).MakeByRefType()],
                modifiers: null);
            tryExecute.Should().NotBeNull();
            var parameters = new object?[] { new EditCellsCommand(sheet.Id, a1, new NumberValue(50)), "Edit Cell", null };
            var success = (bool)tryExecute!.Invoke(window, parameters)!;
            success.Should().BeTrue();
            editOutcome = (CommandOutcome)parameters[2]!;

            InvokeRecalculateIfAutomatic(window, editOutcome.AffectedCells ?? [a1]);

            sheet.GetValue(b1).Should().Be(new NumberValue(100), "B1 = A1*2 must recalculate to 100 after A1 becomes 50");
            picture.Cells.Should().Contain(
                c => c.Text == "100",
                "the linked picture must refresh from B1's cascaded-recalculated value even though " +
                "the triggering edit's own AffectedCells was only [A1]");
            picture.Cells.Should().NotContain(c => c.Text == "10", "the stale pre-edit snapshot must be gone");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    /// <summary>No-regression sibling: a recalculation that does NOT touch the picture's source
    /// range at all must not rebuild its cached snapshot.</summary>
    [Fact]
    public void RecalcCascade_ThatDoesNotTouchTheLinkedRange_LeavesTheLinkedPicturesSnapshotUntouched() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 1, 2);
            var c1 = new CellAddress(sheet.Id, 1, 3); // unrelated formula, not near the picture
            var sourceRange = new GridRange(b1, b1);
            var destination = new CellAddress(sheet.Id, 5, 4);

            sheet.SetCell(a1, Cell.FromValue(new NumberValue(5)));
            sheet.SetCell(b1, Cell.FromValue(new NumberValue(10))); // plain value, not a formula
            sheet.SetCell(c1, Cell.FromFormula("A1*3"));
            DialogSourceTestSupport.InvokePrivateHandler(window, "CalcFullBtn_Click");
            sheet.GetValue(c1).Should().Be(new NumberValue(15));

            ExecuteCommandThroughRealChokePoint(
                window,
                new PasteRangeAsPictureCommand(
                    sheet.Id,
                    sourceRange,
                    [(b1, "10")],
                    destination,
                    isLinkedToSourceRange: true,
                    sourceSheetName: sheet.Name)).Should().BeTrue();

            var picture = sheet.Pictures.Should().ContainSingle().Subject;
            var snapshotBefore = picture.Cells.Select(c => c with { }).ToList();

            var tryExecute = typeof(MainWindow).GetMethod(
                "TryExecuteCommand",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(IWorkbookCommand), typeof(string), typeof(CommandOutcome).MakeByRefType()],
                modifiers: null);
            tryExecute.Should().NotBeNull();
            var parameters = new object?[] { new EditCellsCommand(sheet.Id, a1, new NumberValue(50)), "Edit Cell", null };
            ((bool)tryExecute!.Invoke(window, parameters)!).Should().BeTrue();
            var editOutcome = (CommandOutcome)parameters[2]!;

            InvokeRecalculateIfAutomatic(window, editOutcome.AffectedCells ?? [a1]);

            sheet.GetValue(c1).Should().Be(new NumberValue(150), "C1 = A1*3 must recalculate to 150 (unrelated to the picture)");
            picture.Cells.Should().BeEquivalentTo(snapshotBefore,
                "a recalculation cascade that never touches the linked picture's source range must not rebuild its snapshot");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });
}
