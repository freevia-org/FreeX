using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for freex-data-validation-ui F1: Excel auto-clears a cell's red "Circle
/// Invalid Data" oval the instant the flagged value is corrected. Before this fix, that re-check
/// (WorkbookValidationCircleWorkflow.Prune, via the host's private PruneCorrectedValidationCircles
/// helper in MainWindow.DataCommands.cs) was wired into only seven specific Data-ribbon commands
/// (Text to Columns, Remove Duplicates, Advanced Filter, Consolidate, Subtotal, Forecast Sheet,
/// Data Table). The single most common way a user corrects a circled cell -- typing the right value
/// directly into it and pressing Enter -- goes through CommitEdit/CommitEditAcrossSelection ->
/// CompleteWorkbookSessionCellCommit (MainWindow.Editing.cs), which never called the pruner, so the
/// stale red circle stayed drawn over an already-valid cell. These tests drive the real formula-bar
/// commit path only (CommitEdit, exactly what pressing Enter in the grid calls), not the pruner or
/// WorkbookValidationCircleWorkflow directly.
/// </summary>
public sealed class R167_ValidationCircleClearOnTypedFixTests
{
    [Fact]
    public void CommitEdit_TypingValueThatSatisfiesTheRule_ClearsTheCirclesOnTheCorrectedCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var a1 = new CellAddress(sheet.Id, 1, 1);

            AddWholeNumberBetweenRule(sheet, a1, 1, 10);

            // AlertStyle = Warning is not enforced by CommitCellText (only Stop blocks), so the
            // invalid entry commits -- matching how a real Circle Invalid Data scenario arises.
            harness.CommitCellEdit(a1, "50");
            harness.InvokePrivateHandler("CircleInvalidDataMenuItem_Click");

            harness.CurrentValidationCircleCells().Should().Contain(a1,
                "the out-of-range entry must be circled before the fix");

            // The exact user gesture the finding describes: click the circled cell, type a value
            // that satisfies the rule, press Enter -- via the real CommitEdit path.
            harness.CommitCellEdit(a1, "5");

            harness.CurrentValidationCircleCells().Should().NotContain(a1,
                "Excel auto-clears a cell's red circle the instant the flagged value is corrected " +
                "by a plain typed edit, without the user having to re-run Circle Invalid Data");
        });
    }

    // No-regression sibling: a cell that is STILL invalid after a typed edit (e.g. corrected to a
    // different, still out-of-range value) must remain circled -- the fix must only clear circles
    // on cells that actually became valid, not blanket-clear on every commit.
    [Fact]
    public void CommitEdit_TypingValueThatStillViolatesTheRule_KeepsTheCircleOnThatCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var a1 = new CellAddress(sheet.Id, 1, 1);

            AddWholeNumberBetweenRule(sheet, a1, 1, 10);

            harness.CommitCellEdit(a1, "50");
            harness.InvokePrivateHandler("CircleInvalidDataMenuItem_Click");
            harness.CurrentValidationCircleCells().Should().Contain(a1);

            // Still out of range (1-10) after the edit.
            harness.CommitCellEdit(a1, "99");

            harness.CurrentValidationCircleCells().Should().Contain(a1,
                "a cell that is still invalid after a typed edit must remain circled");
        });
    }

    private static void AddWholeNumberBetweenRule(Sheet sheet, CellAddress address, int min, int max)
    {
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new FreeX.Core.Model.GridRange(address, address),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = min.ToString(),
            Formula2 = max.ToString(),
            AlertStyle = DvAlertStyle.Warning,
            ShowErrorMessage = true
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        public MainWindow Window { get; }
        public Workbook Workbook { get; }


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
                NullUserMessageService.Instance);

            Window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied workbook
            // with a fresh one via CreateNewWorkbook() -- capture the *live* workbook afterward so
            // the test operates on the same Workbook instance MainWindow's handlers use.
            Workbook = workbookRef.Current;

        }

        public void InvokePrivateHandler(string methodName) =>
            DialogSourceTestSupport.InvokePrivateHandler(Window, methodName);

        /// <summary>
        /// Commits <paramref name="text"/> into <paramref name="address"/> via the real formula-bar
        /// commit path (<c>CommitEdit</c>), exactly as an ordinary interactive edit would -- not via
        /// direct sheet mutation and not via reflectively invoking any private validation-circle
        /// pruning helper.
        /// </summary>
        public void CommitCellEdit(CellAddress address, string text)
        {
            ((SheetGridView)Window.FindName("SheetGrid")).SelectedRange = new FreeX.Core.Model.GridRange(address, address);
            ((TextBox)Window.FindName("FormulaBar")).Text = text;
            (Window.CommitEdit()).Should().BeTrue();
            PumpDispatcher();
        }

        /// <summary>
        /// Reads the validation-circle set the grid is actually rendering right now (SheetGrid's own
        /// ValidationCircleCells dependency property, the same list RenderValidationCircles draws
        /// from), not the sheet model's stored list -- proving the fix reaches the screen, not just
        /// the data.
        /// </summary>
        public IReadOnlyList<CellAddress> CurrentValidationCircleCells() =>
            ((SheetGridView)Window.FindName("SheetGrid")).ValidationCircleCells
                ?? Array.Empty<CellAddress>();

        public void Dispose()
        {
            foreach (System.Windows.Window ownedWindow in Window.OwnedWindows.Cast<System.Windows.Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(Window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
