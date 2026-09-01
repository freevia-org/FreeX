using System.Reflection;
using System.Windows;
using System.Windows.Automation;
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
/// R90-commands-text-to-columns-5-1: using the Text-to-Columns Destination range-picker (the
/// "Select destination cell" button in <see cref="TextToColumnsDialog"/>) used to silently
/// repurpose the sheet click as the split's SOURCE range too, because
/// <c>TextToColumnsBtn_Click</c> re-read <c>SheetGrid.SelectedRange</c> *after* the modal dialog
/// closed instead of keeping the range that was captured before the dialog opened. The range
/// picker's own selection-restore path (<c>RestoreDialogAfterRangeSelection</c> in
/// MainWindow.DialogRangeSelection.cs) only restores the dialog window's own
/// Left/Top/Opacity/IsHitTestVisible -- it never restores <c>SheetGrid.SelectedRange</c> -- so the
/// grid selection is left pointing at the picked destination cell when the picker session ends.
/// That corrupted selection then got used both as the overwrite-check range AND as the actual
/// source range fed into <c>CreateTextToColumnsCommand</c>, so the split silently read (and wrote
/// to) the picked destination cell instead of the originally selected source range.
/// </summary>
public sealed class R90_TextToColumnsDestinationPickerSourceRangeTests
{
    [Fact]
    public void UsingDestinationPicker_SplitsOriginalSourceRangeAtPickedDestinationInstead()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var a2 = new CellAddress(sheet.Id, 2, 1);
            sheet.SetCell(a1, new TextValue("a,b,c"));
            sheet.SetCell(a2, new TextValue("d,e,f"));

            harness.SelectRange(1, 1, 2, 1);

            // Select A1:A2, Data > Text to Columns > Delimited > Comma (defaults), click the
            // "Select destination cell" picker icon, click F1 on the sheet, then Finish.
            harness.RunTextToColumnsPickingDestination(destinationRow: 1, destinationCol: 6);

            sheet.GetValue(new CellAddress(sheet.Id, 1, 6)).Should().Be(new TextValue("a"), "F1 should receive the first split field");
            sheet.GetValue(new CellAddress(sheet.Id, 1, 7)).Should().Be(new TextValue("b"), "G1 should receive the second split field");
            sheet.GetValue(new CellAddress(sheet.Id, 1, 8)).Should().Be(new TextValue("c"), "H1 should receive the third split field");
            sheet.GetValue(new CellAddress(sheet.Id, 2, 6)).Should().Be(new TextValue("d"), "F2 should receive row 2's first split field");
            sheet.GetValue(new CellAddress(sheet.Id, 2, 7)).Should().Be(new TextValue("e"), "G2 should receive row 2's second split field");
            sheet.GetValue(new CellAddress(sheet.Id, 2, 8)).Should().Be(new TextValue("f"), "H2 should receive row 2's third split field");

            sheet.GetValue(a1).Should().Be(
                new TextValue("a,b,c"),
                "the original source cell A1 must be READ, not repurposed as the destination and left unsplit");
            sheet.GetValue(a2).Should().Be(
                new TextValue("d,e,f"),
                "the original source cell A2 must be READ, not repurposed as the destination and left unsplit");
        });
    }

    // No-regression sibling: leaving the destination picker untouched (accepting the dialog's
    // default in-place destination) must still split starting at the original source column,
    // exactly as before this fix.
    [Fact]
    public void WithoutUsingDestinationPicker_StillSplitsInPlaceAtSourceColumn()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var sheet = harness.Workbook.GetSheetAt(0);
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var a2 = new CellAddress(sheet.Id, 2, 1);
            sheet.SetCell(a1, new TextValue("a,b,c"));
            sheet.SetCell(a2, new TextValue("d,e,f"));

            harness.SelectRange(1, 1, 2, 1);

            harness.RunTextToColumnsAcceptingDefaultDestination();

            sheet.GetValue(a1).Should().Be(new TextValue("a"));
            sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("b"));
            sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new TextValue("c"));
            sheet.GetValue(a2).Should().Be(new TextValue("d"));
            sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new TextValue("e"));
            sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new TextValue("f"));
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

        public void SelectRange(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var sheet = Workbook.GetSheetAt(0);
            var grid = (SheetGridView)Window.FindName("SheetGrid");
            grid.SelectedRanges = null;
            grid.SelectedRange = new GridRange(
                new CellAddress(sheet.Id, startRow, startCol),
                new CellAddress(sheet.Id, endRow, endCol));
            PumpDispatcher();
        }

        /// <summary>
        /// Drives the real TextToColumnsBtn_Click entry point end to end through the modal
        /// dialog: clicks the destination range-picker button, simulates the picked-cell mouse-up
        /// on the sheet grid (exactly what <c>DialogRangePicker_MouseLeftButtonUp</c> forwards to
        /// <c>CompleteDialogRangeSelection</c>), then clicks Finish.
        /// </summary>
        public void RunTextToColumnsPickingDestination(uint destinationRow, uint destinationCol)
        {
            var sheet = Workbook.GetSheetAt(0);
            Window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Window.OwnedWindows.OfType<TextToColumnsDialog>().Single();

                var picker = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == "Select destination cell");
                DialogSourceTestSupport.ClickButton(picker);

                // Simulate clicking the picked cell on the sheet: this is exactly the grid-selection
                // side effect DialogRangePicker_MouseLeftButtonUp forwards to
                // CompleteDialogRangeSelection for (see MainWindow.DialogRangeSelection.cs).
                var grid = (SheetGridView)Window.FindName("SheetGrid");
                grid.SelectedRanges = null;
                grid.SelectedRange = new GridRange(
                    new CellAddress(sheet.Id, destinationRow, destinationCol),
                    new CellAddress(sheet.Id, destinationRow, destinationCol));

                Window.CompleteDialogRangeSelection(true);

                var finishButton = DialogSourceTestSupport.GetPrivateField<Button>(dialog, "_finishButton");
                DialogSourceTestSupport.ClickButton(finishButton);
            }), System.Windows.Threading.DispatcherPriority.Background);

            InvokePrivateHandler("TextToColumnsBtn_Click");
            PumpDispatcher();
        }

        /// <summary>Drives TextToColumnsBtn_Click and immediately accepts the dialog's default destination (never opens the picker).</summary>
        public void RunTextToColumnsAcceptingDefaultDestination()
        {
            Window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Window.OwnedWindows.OfType<TextToColumnsDialog>().Single();
                var finishButton = DialogSourceTestSupport.GetPrivateField<Button>(dialog, "_finishButton");
                DialogSourceTestSupport.ClickButton(finishButton);
            }), System.Windows.Threading.DispatcherPriority.Background);

            InvokePrivateHandler("TextToColumnsBtn_Click");
            PumpDispatcher();
        }

        public void InvokePrivateHandler(string methodName) =>
            DialogSourceTestSupport.InvokePrivateHandler(Window, methodName);

        public void Dispose()
        {
            foreach (Window ownedWindow in Window.OwnedWindows.Cast<Window>().ToList())
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
