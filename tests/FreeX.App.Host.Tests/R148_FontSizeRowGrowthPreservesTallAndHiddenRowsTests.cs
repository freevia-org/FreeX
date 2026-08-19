using System.Reflection;
using System.Windows;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Free.Shared.AppServices;
using FluentAssertions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R148 remediation: MainWindow.HomeFormatting.cs's ApplyFontSizeAndFitRows (backing the ribbon's
/// Font Size box and Increase/Decrease Font Size buttons) used to build ONE flat
/// SetRowHeightCommand spanning the whole selected row range, which overwrites every row's height
/// -- including one taller than the new font's fitting height -- and clears every row's hidden
/// flag across the span. The shared WorkbookSession.CreateFontSizeRowGrowthCommands helper
/// (R148-rowcol-sizing-F3) already fixed this for the Avalonia shell (via
/// WorkbookSession.SetSelectedRangeFontSize); this test proves the WPF host's own mirror
/// (CreateFontSizeRowGrowthCommands in MainWindow.HomeFormatting.cs) now does the same: select
/// rows 1-3 with row 1 at a tall custom height and row 3 hidden, click Increase Font Size, and
/// row 1 must keep its tall height while row 3 must stay hidden.
/// </summary>
public sealed class R148_FontSizeRowGrowthPreservesTallAndHiddenRowsTests
{
    [Fact]
    public void R148_IncreaseFontSizeBtn_TallCustomRowAndHiddenRowInSelection_BothPreserved()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var messages = new RecordingUserMessageService();
            var window = new MainWindow(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                messages);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);

                // Row 1: a wrapped-text/banner row manually resized taller than any font-size-driven
                // fitting height will ever reach.
                const double tallCustomHeight = 300.0;
                sheet.RowHeights[1] = tallCustomHeight;

                // Row 2: ordinary content at the default row height, seeded with a small font so the
                // Increase button produces a real fitting-height growth.
                const double seedFontSize = 11;
                var address = new CellAddress(sheet.Id, 2, 1);
                var styleId = workbook.RegisterStyle(new CellStyle { FontSize = seedFontSize });
                sheet.SetCell(address, new TextValue("Normal"));
                sheet.GetCell(address)!.StyleId = styleId;

                // Row 3: explicitly hidden by the user.
                sheet.HiddenRows.Add(3);

                var grid = (FreeX.App.UI.GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 3, 1));
                PumpDispatcher();

                var expectedFittedHeight = FontSizePlanner.EstimateFittingRowHeight(FontSizePlanner.Increase(seedFontSize));
                expectedFittedHeight.Should().BeLessThan(tallCustomHeight,
                    "the test is only meaningful if the font-driven fitting height would have collapsed row 1's tall custom height");

                var method = typeof(MainWindow).GetMethod("IncreaseFontSizeBtn_Click", BindingFlags.NonPublic | BindingFlags.Instance);
                method.Should().NotBeNull();
                method!.Invoke(window, [null, new RoutedEventArgs()]);
                PumpDispatcher();

                messages.Messages.Should().BeEmpty();

                sheet.RowHeights[1].Should().Be(tallCustomHeight,
                    "a font-size change must only ever GROW a row, never collapse a manually-sized taller row");

                sheet.HiddenRows.Should().Contain(3,
                    "a font-size change must never un-hide a row caught inside the selection's row span");
                sheet.RowHeights.Should().NotContainKey(3,
                    "the hidden row must not receive a new explicit height either");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public List<string> Messages { get; } = new();

        public void ShowError(string message, string title = "Error") => Messages.Add($"Error: {title}: {message}");

        public void ShowWarning(string message, string title = "Warning") => Messages.Add($"Warning: {title}: {message}");

        public void ShowInfo(string message, string title = "Information") => Messages.Add($"Info: {title}: {message}");

        public bool AskYesNo(string message, string title = "Confirm")
        {
            Messages.Add($"AskYesNo: {title}: {message}");
            return true;
        }

        public UserMessageResult ShowMessage(string message, string title, UserMessageButtons buttons, UserMessageIcon icon)
        {
            Messages.Add($"ShowMessage: {title}: {message}");
            return UserMessageResult.Ok;
        }
    }
}
