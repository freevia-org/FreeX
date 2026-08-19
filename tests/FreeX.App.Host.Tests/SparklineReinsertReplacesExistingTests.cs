using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

// Regression coverage for sparkline-lifecycle F1: WPF's Insert Sparkline handler
// (MainWindow.InsertCommands.cs InsertSparkline) never checked for a sparkline already anchored
// at the target cell before adding a new one, so re-inserting over an occupied cell (the only way
// a WPF user can change an existing sparkline's type/range, since this shell has no separate
// edit/clear command) silently stacked a second SparklineModel on the same Location. Both shells
// then draw one sparkline on top of the other, and the file round-trips as two overlapping
// <x14:sparklineGroup> entries naming the same cell -- a shape Excel never produces.
public sealed class SparklineReinsertReplacesExistingTests
{
    [Fact]
    public void ReinsertingSparklineAtOccupiedCell_ReplacesExistingSparklineInsteadOfStacking()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new SparklineTestHarness();
            var sheet = harness.Sheet;
            for (uint row = 1; row <= 5; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

            var dataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1));
            var location = new CellAddress(sheet.Id, 1, 2); // B1

            harness.Grid.SelectedRange = dataRange;
            harness.InsertSparklineViaDialog("line", "B1");

            sheet.Sparklines.Should().ContainSingle();
            sheet.Sparklines[0].Location.Should().Be(location);
            sheet.Sparklines[0].Kind.Should().Be(SparklineKind.Line);

            // Re-insert at the SAME cell with a different kind/data range -- the natural gesture
            // for a WPF user who wants to change an existing sparkline, since there is no edit
            // command. This must replace the existing sparkline, not add a second one on top of it.
            harness.Grid.SelectedRange = dataRange;
            harness.InsertSparklineViaDialog("column", "B1");

            sheet.Sparklines.Should().ContainSingle(
                "re-inserting a sparkline at a cell that already has one must replace it instead of " +
                "stacking a second overlapping SparklineModel at the same Location");
            sheet.Sparklines[0].Location.Should().Be(location);
            sheet.Sparklines[0].Kind.Should().Be(SparklineKind.Column);
        });
    }

    [Fact]
    public void InsertingSparklinesAtDifferentCells_KeepsBothSparklinesIntact()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new SparklineTestHarness();
            var sheet = harness.Sheet;
            for (uint row = 1; row <= 5; row++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
                sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 2));
            }

            var firstDataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1));
            var secondDataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 2),
                new CellAddress(sheet.Id, 5, 2));

            harness.Grid.SelectedRange = firstDataRange;
            harness.InsertSparklineViaDialog("line", "C1");

            harness.Grid.SelectedRange = secondDataRange;
            harness.InsertSparklineViaDialog("column", "D1");

            // Two distinct target cells must NOT be collapsed into a replace -- both sparklines
            // stay, each at its own Location. Guards the adjacent case my sparkline-lifecycle F1
            // fix touches: the replace-at-same-cell logic must key off Location, not "any prior
            // insert in this session".
            sheet.Sparklines.Should().HaveCount(2);
            sheet.Sparklines.Should().ContainSingle(s => s.Location == new CellAddress(sheet.Id, 1, 3) && s.Kind == SparklineKind.Line);
            sheet.Sparklines.Should().ContainSingle(s => s.Location == new CellAddress(sheet.Id, 1, 4) && s.Kind == SparklineKind.Column);
        });
    }

    private sealed class SparklineTestHarness : IDisposable
    {
        public MainWindow Window { get; }
        public Workbook Workbook { get; }
        public Sheet Sheet { get; }
        public FreeX.App.UI.GridView Grid { get; }

        public SparklineTestHarness()
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

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied
            // workbook with a fresh one via CreateNewWorkbook(), so capture the live workbook
            // afterward -- same pattern as MainWindowSheetTabGroupOpsTests.MainWindowHarness.
            Workbook = workbookRef.Current;
            Sheet = Workbook.GetSheetAt(0);
            Grid = (FreeX.App.UI.GridView)Window.FindName("SheetGrid");
        }

        /// <summary>
        /// Drives InsertSparkline(type) end to end: invokes the private handler (the real
        /// production call site reached by the Sparkline Line/Column/Win-Loss ribbon buttons),
        /// waits for the modal SparklineDialog it opens, fills in the location field, and clicks
        /// OK -- exactly what a WPF user does re-clicking Insert Sparkline over an occupied cell.
        /// </summary>
        public void InsertSparklineViaDialog(string type, string locationText)
        {
            QueueOwnedWindowInteraction(Window, owned =>
            {
                var dialog = (Window)owned;
                var locationBox = GetField<TextBox>(dialog, "_locationBox");
                locationBox.Text = locationText;
                DialogSourceTestSupport.ClickButton(GetSparklineOkButton(dialog));
            });

            var method = typeof(MainWindow).GetMethod(
                "InsertSparkline",
                BindingFlags.Instance | BindingFlags.NonPublic,
                [typeof(string)]);
            method.Should().NotBeNull();
            method!.Invoke(Window, [type]);
            PumpDispatcher();
        }

        private static Button GetSparklineOkButton(Window dialog)
        {
            var stack = dialog.Content.Should().BeOfType<StackPanel>().Subject;
            var buttonRow = stack.Children[stack.Children.Count - 1].Should().BeOfType<StackPanel>().Subject;
            return buttonRow.Children.OfType<Button>().Single(button => button.IsDefault);
        }

        private static T GetField<T>(object instance, string fieldName) where T : class
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(instance.GetType().Name, fieldName);
            return (T)field.GetValue(instance)!;
        }

        /// <summary>
        /// Schedules a callback to run once the next modal window owned by <paramref name="window"/>
        /// opens, letting a test drive a synchronous ShowDialog() call made from a
        /// reflection-invoked private MainWindow method. Mirrors
        /// MainWindowSheetTabGroupOpsTests.QueueOwnedWindowInteraction.
        /// </summary>
        private static void QueueOwnedWindowInteraction(MainWindow window, Action<Window> interact)
        {
            void PollForDialog()
            {
                var owned = window.OwnedWindows.Cast<Window>().FirstOrDefault();
                if (owned is null)
                {
                    window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)PollForDialog);
                    return;
                }

                interact(owned);
            }

            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)PollForDialog);
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in Window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(Window);
            PumpDispatcher();
        }
    }
}
