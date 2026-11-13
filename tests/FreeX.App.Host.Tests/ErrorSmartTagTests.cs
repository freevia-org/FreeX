using System.Windows;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Excel's error smart tag: a cell holding a number stored as text shows the warning button once
/// active, and its Convert to Number makes the cell a real number.
/// </summary>
public sealed class ErrorSmartTagTests
{
    [Fact]
    public void NumberStoredAsText_ShowsTheSmartTag_AndConvertToNumberFixesIt()
    {
        StaTestRunner.Run(() =>
        {
            var window = CreateWindow();
            try
            {
                var sheet = window.Session.Workbook.Sheets[0];
                var a1 = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(a1, new TextValue("123"));
                window.SetActiveCell(new CellAddress(sheet.Id, 1, 2));
                DispatcherTestPump.PumpDispatcher();
                IsShown(window).Should().BeFalse("the active cell has no error");

                window.SetActiveCell(a1);
                window.UpdateViewport();
                DispatcherTestPump.PumpDispatcher();

                window.GetErrorIndicatorCode(a1).Should().NotBeNull();
                IsShown(window).Should().BeTrue("the active cell shows the error smart tag");

                window.ExecuteErrorSmartTagAction(ErrorSmartTagAction.ConvertToNumber);
                DispatcherTestPump.PumpDispatcher();

                sheet.GetValue(a1).Should().Be(new NumberValue(123));
                IsShown(window).Should().BeFalse("the fixed cell no longer shows the smart tag");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                DispatcherTestPump.PumpDispatcher();
            }
        });
    }

    private static bool IsShown(MainWindow window) =>
        window.ErrorSmartTagButtonForTest is { Visibility: Visibility.Visible };

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
