using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Functional safety net for the three editable Home combos (Font, Font Size, Number Format) on the
/// DECLARATIVE ribbon. These tests drive the actual rendered combo control resolved via
/// <c>FindName</c> (which the declarative swap re-points at the on-screen control) — typing/selecting
/// a value and raising the commit event must apply the corresponding style to the selection.
/// Before the host-side wiring existed the rendered combo had no SelectionChanged/commit handlers, so
/// these assertions failed even though the displayed text synced.
/// </summary>
public sealed class MainWindowRenderedRibbonComboTests
{
    private static MainWindow CreateWindow(WorkbookRef workbookRef)
    {
        return new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
            new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
            [],
            workbookRef,
            workbookRef.Current,
            NullUserMessageService.Instance);
    }

    private static WorkbookRef CreateWorkbookRef()
    {
        var initialWorkbook = new Workbook("Book1");
        initialWorkbook.AddSheet("Sheet1");
        return new WorkbookRef { Current = initialWorkbook };
    }

    /// <summary>Raises an Enter KeyDown on the rendered combo (the realistic commit gesture for a
    /// typed value), exactly as WPF would deliver it to the wired host KeyDown handler.</summary>
    private static void PressEnter(MainWindow window, ComboBox combo)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        combo.RaiseEvent(args);
    }

    [Fact]
    public void RenderedFontCombo_CommitText_AppliesFontNameToSelection()
    {
        StaTestRunner.Run(() =>
        {
            var workbookRef = CreateWorkbookRef();
            var window = CreateWindow(workbookRef);
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
                grid.SelectedRange = new GridRange(address, address);

                fontBox.Text = "Arial";
                PressEnter(window, fontBox);
                PumpDispatcher();

                var style = workbook.GetStyle(sheet.GetCell(address)!.StyleId);
                style.FontName.Should().Be("Arial");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void RenderedFontSizeCombo_CommitText_AppliesFontSizeToSelection()
    {
        StaTestRunner.Run(() =>
        {
            var workbookRef = CreateWorkbookRef();
            var window = CreateWindow(workbookRef);
            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var address = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(address, new TextValue("Size target"));

                var grid = (FreeX.App.UI.GridView)window.FindName("SheetGrid");
                var sizeBox = (ComboBox)window.FindName("FontSizeBox");
                grid.SelectedRange = new GridRange(address, address);

                sizeBox.Text = "28";
                PressEnter(window, sizeBox);
                PumpDispatcher();

                var style = workbook.GetStyle(sheet.GetCell(address)!.StyleId);
                style.FontSize.Should().Be(28);
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }
}
