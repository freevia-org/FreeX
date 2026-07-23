using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R75-commands-clear-delete-4-1
/// (<c>MainWindow.KeyboardParity.cs</c>'s <c>ClearSelectionAndEdit</c>, routed by Backspace):
/// Backspace on a multi-cell selection previously cleared the WHOLE selection (via
/// <c>ClearSelectedRangeContents</c>, shared with the Delete key/ribbon path) before entering edit
/// -- but Excel's Backspace clears ONLY the active cell. The fix routes this shortcut through
/// <see cref="FreeX.App.Services.WorkbookSession.ClearActiveCellContents"/> instead.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R75_BackspaceActiveCellOnlyClearTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Backspace_OnMultiCellSelection_ClearsOnlyActiveCell_LeavesRestUntouched()
    {
        await Run(async (window, sheet) =>
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var a2 = new CellAddress(sheet.Id, 2, 1);
            var a3 = new CellAddress(sheet.Id, 3, 1);
            sheet.SetCell(a1, new NumberValue(1));
            sheet.SetCell(a2, new NumberValue(2));
            sheet.SetCell(a3, new NumberValue(3));

            window.Session.SelectRange(new GridRange(a1, a3));

            await Press(window, Key.Back, KeyModifiers.None);

            sheet.GetValue(a1).Should().Be(BlankValue.Instance, "Backspace must clear the active cell (A1)");
            sheet.GetValue(a2).Should().Be(new NumberValue(2), "Backspace must NOT touch A2 -- it is not Delete/Clear Contents");
            sheet.GetValue(a3).Should().Be(new NumberValue(3), "Backspace must NOT touch A3 -- it is not Delete/Clear Contents");
        });
    }

    [Fact]
    public async Task DeleteKey_OnMultiCellSelection_StillClearsWholeSelection()
    {
        // Sibling no-regression: the pre-existing Delete-key full-selection clear must be
        // completely unaffected by adding the Backspace-only-active-cell path.
        await Run(async (window, sheet) =>
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var a2 = new CellAddress(sheet.Id, 2, 1);
            var a3 = new CellAddress(sheet.Id, 3, 1);
            sheet.SetCell(a1, new NumberValue(1));
            sheet.SetCell(a2, new NumberValue(2));
            sheet.SetCell(a3, new NumberValue(3));

            window.Session.SelectRange(new GridRange(a1, a3));

            await Press(window, Key.Delete, KeyModifiers.None);

            sheet.GetValue(a1).Should().Be(BlankValue.Instance, "Delete must still clear the whole selection");
            sheet.GetValue(a2).Should().Be(BlankValue.Instance, "Delete must still clear the whole selection");
            sheet.GetValue(a3).Should().Be(BlankValue.Instance, "Delete must still clear the whole selection");
        });
    }

    [Fact]
    public async Task Backspace_OnSingleCellSelection_StillClearsThatCell()
    {
        await Run(async (window, sheet) =>
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new NumberValue(99));

            window.Session.SelectRange(new GridRange(a1, a1));

            await Press(window, Key.Back, KeyModifiers.None);

            sheet.GetValue(a1).Should().Be(BlankValue.Instance, "Backspace on a single-cell selection must clear that cell");
        });
    }

    private static async Task Run(Func<MainWindow, Sheet, Task> test)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("BackspaceClear");
            window.Session.SelectSheet(sheet.Id);
            try
            {
                await test(window, sheet);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static async Task Press(MainWindow window, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers };
        await window.RaiseKeyDownForTest(args);
        args.Handled.Should().BeTrue($"{modifiers}+{key} should be consumed by MainWindow");
    }
}
