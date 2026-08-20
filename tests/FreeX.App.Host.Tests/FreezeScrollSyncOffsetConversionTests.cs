using System.Reflection;
using System.Windows;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for the MED finding freeze-scroll-sync F1 (MainWindow.MultiWindow.cs
/// GetScrollOffset/SetScrollOffset): Synchronous Scrolling copied the origin window's raw
/// ScrollBar.Value straight onto the partner window's ScrollBar.Value. ScrollBar.Value is NOT a
/// worksheet row/column index -- it is offset from each window's own frozen-row/frozen-column
/// count (<see cref="WorkbookViewportScrollPlanner.ScrollbarValueToWorksheetIndex"/>), and Freeze
/// Panes is explicitly per-window state (R89-freeze-split-per-window-1). When the origin and
/// partner windows have different frozen counts, applying the origin's raw Value to the partner
/// resolved to a DIFFERENT worksheet row/column, drifting the two windows out of sync instead of
/// showing corresponding regions.
/// </summary>
public sealed class FreezeScrollSyncOffsetConversionTests
{
    [Fact]
    public void SetScrollOffset_PartnerWithDifferentFreezePanes_ResolvesToSameWorksheetOrigin() =>
        StaTestRunner.Run(() =>
        {
            using var origin = ScrollSyncHarness.Create();
            using var partner = ScrollSyncHarness.Create();

            // Origin window: no Freeze Panes, scrolled so worksheet row 50 / col 6 is at the
            // top-left of its viewport.
            origin.SetScrollbarValues(vertical: 50, horizontal: 6);

            // Partner window: Freeze Panes is per-window (R89-freeze-split-per-window-1) -- THIS
            // window independently froze its own top 5 rows / 2 columns, so the same raw
            // ScrollBar.Value resolves to a DIFFERENT worksheet row/column than it does in origin.
            partner.SetFreezePanes(frozenRows: 5, frozenCols: 2);

            var offset = origin.Window.GetScrollOffset();
            partner.Window.SetScrollOffset(offset);

            var resolved = WorkbookViewportScrollPlanner.CalculateViewportOrigin(
                5u, 2u, partner.VerticalValue, partner.HorizontalValue);

            resolved.TopRow.Should().Be(
                50u,
                "Synchronous Scrolling must show the SAME worksheet row the origin window " +
                "showed, resolved through the partner's OWN Freeze Panes offset -- not the raw " +
                "ScrollBar.Value, which is relative to each window's own frozen-row count");
            resolved.LeftCol.Should().Be(
                6u,
                "same reasoning applies to columns / Freeze Panes' frozen-column count");
        });

    /// <summary>
    /// No-regression sibling: the common case -- neither window has Freeze Panes on -- must keep
    /// resolving to the same worksheet origin exactly as before this fix (ScrollBar.Value IS the
    /// worksheet row/column when the frozen count is zero, so the new conversion is a no-op).
    /// </summary>
    [Fact]
    public void SetScrollOffset_NeitherWindowHasFreezePanes_StillResolvesToSameWorksheetOrigin() =>
        StaTestRunner.Run(() =>
        {
            using var origin = ScrollSyncHarness.Create();
            using var partner = ScrollSyncHarness.Create();

            origin.SetScrollbarValues(vertical: 50, horizontal: 6);

            var offset = origin.Window.GetScrollOffset();
            partner.Window.SetScrollOffset(offset);

            var resolved = WorkbookViewportScrollPlanner.CalculateViewportOrigin(
                0u, 0u, partner.VerticalValue, partner.HorizontalValue);

            resolved.TopRow.Should().Be(50u);
            resolved.LeftCol.Should().Be(6u);
        });

    private sealed class ScrollSyncHarness : IDisposable
    {
        public readonly MainWindow Window;
        private readonly MethodInfo _setFreezePanes;
        private readonly MethodInfo _updateViewport;

        private ScrollSyncHarness(MainWindow window)
        {
            Window = window;
            _setFreezePanes = typeof(MainWindow)
                .GetMethod("SetFreezePanes", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetFreezePanes");
            _updateViewport = typeof(MainWindow)
                .GetMethod("UpdateViewport", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateViewport");
        }

        public double VerticalValue => Window.VerticalScroll!.Value;
        public double HorizontalValue => Window.HorizontalScroll!.Value;

        public void SetFreezePanes(uint frozenRows, uint frozenCols)
        {
            _setFreezePanes.Invoke(Window, [frozenRows, frozenCols]);
            PumpDispatcher();
        }

        public void SetScrollbarValues(double vertical, double horizontal)
        {
            Window.VerticalScroll!.Value = vertical;
            Window.HorizontalScroll!.Value = horizontal;
            PumpDispatcher();
        }

        // MainWindow_Loaded (fired by Show/PumpDispatcher below) replaces the constructor's
        // workbook with a brand new one via CreateNewWorkbook(), so populate the LIVE post-Loaded
        // sheet, not the one passed into the constructor (mirrors ViewportSelectionHarness in
        // R31_ViewportSelectionLogicTests.cs).
        public static ScrollSyncHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                Array.Empty<FreeX.Core.IO.IFileAdapter>(),
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();

            var harness = new ScrollSyncHarness(window);

            // Give the live sheet enough rows/columns that the scroll bars' Maximum comfortably
            // exceeds every test value used above, instead of the near-empty default used range.
            var liveSheet = window.Session.Workbook.Sheets[0];
            for (uint row = 1; row <= 200; row++)
                liveSheet.SetCell(new CellAddress(liveSheet.Id, row, 40), new NumberValue(row));

            harness._updateViewport.Invoke(window, []);
            PumpDispatcher();

            return harness;
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(Window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
