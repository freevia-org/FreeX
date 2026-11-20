using System.Windows;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Excel's Paste Options button: after a paste the "(Ctrl)" button appears at the pasted range, and
/// choosing Values from it replaces the paste with values only.
/// </summary>
public sealed class PasteOptionsButtonTests
{
    [Fact]
    public void Paste_ShowsTheButton_AndValuesReplacesThePasteWithValues()
    {
        StaTestRunner.Run(() =>
        {
            var window = CreateWindow();
            try
            {
                var sheet = window.Session.Workbook.Sheets[0];
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var c3 = new CellAddress(sheet.Id, 3, 3);
                window.SetActiveCell(a1);
                window.Session.SelectCell(a1);
                window.Session.CommitCellText("=1+1").Success.Should().BeTrue();
                window.UpdateViewport();
                DispatcherTestPump.PumpDispatcher();

                window.SetActiveCell(a1);
                window.ExecuteCopy();
                window.SetActiveCell(c3);
                window.ExecutePaste();
                window.UpdateViewport();
                DispatcherTestPump.PumpDispatcher();

                sheet.GetCell(c3)!.FormulaText.Should().NotBeNullOrEmpty("sanity: the paste copied the formula");
                window.PasteOptionsButtonForTest.Should().NotBeNull();
                window.PasteOptionsButtonForTest!.Visibility.Should().Be(Visibility.Visible, "the Paste Options button follows a paste");

                window.ApplyPasteOption("Paste Values", new GridRange(c3, c3));
                DispatcherTestPump.PumpDispatcher();

                sheet.GetCell(c3)!.FormulaText.Should().BeNullOrEmpty("Values replaces the pasted formula");
                sheet.GetValue(c3).Should().Be(new NumberValue(2));
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                DispatcherTestPump.PumpDispatcher();
            }
        });
    }

    private static MainWindow CreateWindow()
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
        window.Activate();
        window.UpdateLayout();
        DispatcherTestPump.PumpDispatcher();
        return window;
    }
}
