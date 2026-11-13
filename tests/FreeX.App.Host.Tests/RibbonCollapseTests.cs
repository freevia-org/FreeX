using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Excel's collapsed ribbon ("Show tabs only"): Ctrl+F1 hides the ribbon commands; clicking a tab
/// shows them for one use and clicking back in the sheet hides them again.
/// </summary>
public sealed class RibbonCollapseTests
{
    [Fact]
    public void CollapsedRibbon_HidesCommands_AndATabClickShowsThemUntilTheSheetIsClicked()
    {
        StaTestRunner.Run(() =>
        {
            var window = CreateWindow();
            try
            {
                var tabs = (TabControl)window.FindName("RibbonTabs");
                var expandedHeight = tabs.ActualHeight;

                window.RibbonDisplayState.ToggleCollapsed();
                window.UpdateLayout();
                DispatcherTestPump.PumpDispatcher();

                tabs.ActualHeight.Should().BeLessThan(expandedHeight - 40, "only the tab strip is left");

                var header = (TabItem)tabs.SelectedItem;
                RaiseMouseDown(header, UIElement.PreviewMouseDownEvent);
                window.UpdateLayout();

                window.RibbonDisplayState.IsTemporarilyShown.Should().BeTrue("a tab click shows the collapsed ribbon");
                tabs.ActualHeight.Should().BeApproximately(expandedHeight, 2);

                var grid = (FrameworkElement)window.FindName("SheetGrid");
                RaiseMouseDown(grid, UIElement.PreviewMouseDownEvent);
                window.UpdateLayout();

                window.RibbonDisplayState.IsTemporarilyShown.Should().BeFalse("clicking the sheet hides it again");
                tabs.ActualHeight.Should().BeLessThan(expandedHeight - 40);

                window.RibbonDisplayState.ToggleCollapsed();
                window.UpdateLayout();
                tabs.ActualHeight.Should().BeApproximately(expandedHeight, 2, "pinning restores the full ribbon");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                DispatcherTestPump.PumpDispatcher();
            }
        });
    }

    private static void RaiseMouseDown(UIElement target, RoutedEvent routedEvent)
    {
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = routedEvent,
            Source = target,
        });
        DispatcherTestPump.PumpDispatcher();
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
