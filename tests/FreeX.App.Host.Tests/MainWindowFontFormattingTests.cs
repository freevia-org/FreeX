using System.Windows.Controls;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

public sealed class MainWindowFontFormattingTests
{
    [Fact]
    public void FontFamilyDropdown_AppliesModelStyleAndGridTypeface()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var address = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(address, new TextValue("Font target"));
                var grid = (FreeX.App.UI.GridView)window.FindName("SheetGrid");
                var fontBox = (ComboBox)window.FindName("FontNameBox");
                var fontName = fontBox.Items.OfType<string>().First(name =>
                    !string.Equals(name, CellStyle.Default.FontName, StringComparison.OrdinalIgnoreCase));

                grid.SelectedRange = new GridRange(address, address);
                fontBox.SelectedItem = fontName;
                PumpDispatcher();

                var style = workbook.GetStyle(sheet.GetCell(address)!.StyleId);
                style.FontName.Should().Be(fontName);
                FreeX.App.UI.GridView.CreateCellTypeface(style).FontFamily.Source.Should().Be(fontName);
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }
}
