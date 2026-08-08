using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;

using FreeX.Core.Model;

using Pointer = Avalonia.Input.Pointer;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R82-render-scroll-viewport-5-1: both WorksheetScrollBar_ValueChanged and
/// SheetScrollViewer_PointerWheelChanged used to call RefreshShell("Ready") unconditionally on every
/// scroll tick, which -- on top of BuildSheetGrid's own per-cell control rebuild -- ALSO rebuilt the
/// sheet-tab strip, the cell-address/formula-bar readouts, every format-toggle button, the status-bar
/// model, the pivot field pane/contextual tab, the save button, and the ribbon's toggle states, none of
/// which a pure viewport pan (no active-cell or selection change) can ever affect. These tests drive the
/// real production mouse-wheel handler (via <see cref="MainWindow.RaisePointerWheelChangedForTest"/>) and
/// assert on the new <see cref="MainWindow.SheetTabsBuildCountForTest"/>/<see
/// cref="MainWindow.SheetGridBuildCountForTest"/> counters rather than a source-string proxy.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R82_ScrollViewportSkipsFullShellRefreshTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task PlainMouseWheelScroll_RefreshesGrid_ButDoesNotRebuildSheetTabStrip()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("R82ScrollFixture");
                window.Session.SelectSheet(sheet.Id);
                for (var row = 1; row <= 200; row++)
                    sheet.SetCell(new CellAddress(sheet.Id, (uint)row, 1), new NumberValue(row));
                window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

                var gridBuildCountBefore = window.SheetGridBuildCountForTest;
                var tabsBuildCountBefore = window.SheetTabsBuildCountForTest;

                var pointer = new Pointer(1, PointerType.Mouse, isPrimary: true);
                for (var i = 0; i < 5; i++)
                {
                    var args = new PointerWheelEventArgs(
                        window,
                        pointer,
                        window.SheetGridHostForTest,
                        new Point(10, 10),
                        0,
                        new PointerPointProperties(),
                        KeyModifiers.None,
                        new Vector(0, -1));
                    window.RaisePointerWheelChangedForTest(args);
                }

                // Failing before the fix: RefreshShell("Ready") rebuilt the sheet-tab strip on every one
                // of these ticks, so SheetTabsBuildCountForTest would have grown right along with
                // SheetGridBuildCountForTest instead of staying flat.
                window.SheetGridBuildCountForTest.Should().BeGreaterThan(gridBuildCountBefore,
                    "scrolling must still refresh the visible grid content (BuildSheetGrid has no " +
                    "container recycling yet, so this alone doesn't prove the fix)");
                window.SheetTabsBuildCountForTest.Should().Be(tabsBuildCountBefore,
                    "a pure viewport pan must not rebuild the sheet-tab strip -- nothing about which " +
                    "sheet is active or how its tabs render changes when only the visible rows/cols pan");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    // No-regression sibling: the full (non-pan) RefreshShell path -- used by cell edits, selection
    // changes, and everything else outside of a pure scroll -- must still rebuild the sheet-tab strip.
    // Only the new pan-only fast path is allowed to skip it.
    [Fact]
    public async Task DirectRefreshShellCall_StillRebuildsSheetTabStrip()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Session.Workbook.AddSheet("R82ScrollFixtureSibling");

                var tabsBuildCountBefore = window.SheetTabsBuildCountForTest;
                InvokeRefreshShell(window, "Ready");

                window.SheetTabsBuildCountForTest.Should().BeGreaterThan(tabsBuildCountBefore,
                    "the full RefreshShell path must still rebuild the sheet-tab strip -- only " +
                    "RefreshShellForViewportPan (pure scroll) is allowed to skip it");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static void InvokeRefreshShell(MainWindow window, string status) =>
        typeof(MainWindow)
            .GetMethod("RefreshShell", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [status]);
}
