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
/// R104: MainWindow.HomeFormatting.cs's ApplyFontSizeAndFitRows (backing the ribbon's
/// Increase/Decrease Font Size buttons) must clamp FontSizePlanner.EstimateFittingRowHeight's
/// result to AutoFitSizingService.MaximumRowHeight (546px) before constructing
/// SetRowHeightCommand, exactly like the already-fixed (R103) WorkbookSession.GetFittingRowHeight
/// path -- otherwise a legal large font size (up to 409pt) makes SetRowHeightCommand reject the
/// unclamped height and the user sees a spurious "Auto Fit Row Height" error dialog instead of
/// the row simply auto-fitting (clamped), as real Excel does.
/// </summary>
public sealed class R104_IncreaseFontSizeAutoFitRowClampTests
{
    [Fact]
    public void R104_IncreaseFontSizeBtn_ExtremeFontAbovePixelCeiling_ClampsRowHeightInsteadOfErroring()
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
                var address = new CellAddress(sheet.Id, 1, 1);

                // Seed the cell with a font size that, once bumped by FontSizePlanner.Increase
                // (which adds 4 above the large-font threshold), produces an unclamped
                // EstimateFittingRowHeight above the 546px ceiling: 402 + 4 = 406;
                // ceil(406*96/72 + 5) = 547 > 546.
                const double seedFontSize = 402;
                var styleId = workbook.RegisterStyle(new CellStyle { FontSize = seedFontSize });
                sheet.SetCell(address, new TextValue("Big font"));
                sheet.GetCell(address)!.StyleId = styleId;

                var grid = (FreeX.App.UI.GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(address, address);
                PumpDispatcher();

                var uncappedEstimate = FontSizePlanner.EstimateFittingRowHeight(FontSizePlanner.Increase(seedFontSize));
                uncappedEstimate.Should().BeGreaterThan(AutoFitSizingService.MaximumRowHeight,
                    "the test is only meaningful if the naive estimate exceeds the pixel ceiling");

                var method = typeof(MainWindow).GetMethod("IncreaseFontSizeBtn_Click", BindingFlags.NonPublic | BindingFlags.Instance);
                method.Should().NotBeNull();
                method!.Invoke(window, [null, new RoutedEventArgs()]);
                PumpDispatcher();

                // No "Auto Fit Row Height" error/warning should have been surfaced to the user.
                messages.Messages.Should().BeEmpty(
                    "a legal font-size increase must never surface a row-height error dialog");

                var newStyle = workbook.GetStyle(sheet.GetCell(address)!.StyleId);
                newStyle.FontSize.Should().Be(FontSizePlanner.Increase(seedFontSize));

                sheet.RowHeights.Should().ContainKey(1);
                sheet.RowHeights[1].Should().Be(AutoFitSizingService.MaximumRowHeight,
                    "the row height must be clamped to Excel's true pixel-space ceiling, not rejected");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void R104_IncreaseFontSizeBtn_OrdinaryFontBelowCeiling_FitsRowExactlyAsBefore()
    {
        // No-regression sibling: an ordinary font-size increase that never nears the pixel
        // ceiling must keep producing the same unclamped fitting height it always has.
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
                var address = new CellAddress(sheet.Id, 1, 1);

                const double seedFontSize = 100;
                var styleId = workbook.RegisterStyle(new CellStyle { FontSize = seedFontSize });
                sheet.SetCell(address, new TextValue("Normal font"));
                sheet.GetCell(address)!.StyleId = styleId;

                var grid = (FreeX.App.UI.GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(address, address);
                PumpDispatcher();

                var expectedHeight = FontSizePlanner.EstimateFittingRowHeight(FontSizePlanner.Increase(seedFontSize));
                expectedHeight.Should().BeLessThan(AutoFitSizingService.MaximumRowHeight);

                var method = typeof(MainWindow).GetMethod("IncreaseFontSizeBtn_Click", BindingFlags.NonPublic | BindingFlags.Instance);
                method!.Invoke(window, [null, new RoutedEventArgs()]);
                PumpDispatcher();

                messages.Messages.Should().BeEmpty();
                sheet.RowHeights[1].Should().Be(expectedHeight);
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
