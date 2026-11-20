using System.Windows;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>Excel's Auto Fill Options button follows a fill-handle drag and redoes it another way.</summary>
public sealed class AutofillOptionsButtonTests
{
    [Fact]
    public void FillDrag_ShowsTheButton_AndFillSeriesRedoesTheDrag()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                Array.Empty<FreeX.Core.IO.IFileAdapter>(),
                workbookRef,
                workbook,
                NullUserMessageService.Instance,
                platformClipboard: new InMemoryPlatformClipboard())
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };
            window.Show();
            window.UpdateLayout();
            DispatcherTestPump.PumpDispatcher();
            try
            {
                var sheet = window.Session.Workbook.Sheets[0];
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
                window.UpdateViewport();
                var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
                var fill = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1));

                window.ExecuteAutofill(source, fill, ctrlHeld: false);
                DispatcherTestPump.PumpDispatcher();

                window.AutofillOptionsButtonForTest.Should().NotBeNull();
                window.AutofillOptionsButtonForTest!.Visibility.Should().Be(Visibility.Visible, "the button follows a fill");

                window.ApplyAutofillOption(AutofillMode.FillSeries);
                DispatcherTestPump.PumpDispatcher();

                sheet.GetValue(3, 1).Should().Be(new NumberValue(7));
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                DispatcherTestPump.PumpDispatcher();
            }
        });
    }
}
