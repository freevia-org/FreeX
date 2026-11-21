using System.Windows;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>Excel's Insert Options button follows an insert of rows and re-formats the new band.</summary>
public sealed class InsertOptionsButtonTests
{
    [Fact]
    public void InsertedRows_ShowTheButton_AndFormatChoicesApplyToTheBand()
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
                var italic = window.Session.Workbook.RegisterStyle(new CellStyle { Italic = true });
                sheet.SetStyleOnly(2, 10, italic);
                window.SetActiveCell(new CellAddress(sheet.Id, 2, 1));
                window.UpdateViewport();

                window.Session.ExecuteCommandPreservingSelection(new InsertRowsCommand(sheet.Id, 2, 1)).Success.Should().BeTrue();
                window.UpdateViewport();
                DispatcherTestPump.PumpDispatcher();

                window.InsertOptionsButtonForTest.Should().NotBeNull();
                window.InsertOptionsButtonForTest!.Visibility.Should().Be(Visibility.Visible, "the Insert Options button follows an insert");

                window.ApplyInsertOption(new InsertedBand(sheet.Id, true, 2, 1), InsertedBandFormat.SameAsAfter);
                sheet.GetStyleOnly(2, 10).Should().Be(italic, "Format Same As Below copies the row that moved down");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                DispatcherTestPump.PumpDispatcher();
            }
        });
    }
}
