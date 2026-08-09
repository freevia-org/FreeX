using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R77: normal Avalonia cell selection must recompute the PivotTable contextual ribbon state, matching
/// the WPF viewport refresh that reevaluates contextual state after every active-cell change.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R77_PivotContextualSelectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task CellClick_EnteringAndLeavingPivot_RefreshesPivotContext()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var pivot = new PivotTableModel
                {
                    Name = "ContextPivot",
                    SourceRange = Range(sheet.Id, 1, 1, 4, 3),
                    TargetRange = Range(sheet.Id, 1, 5, 4, 7),
                };
                sheet.PivotTables.Add(pivot);

                var outside = new CellAddress(sheet.Id, 10, 10);
                window.SelectClickedCell(outside, KeyModifiers.None);
                window.RibbonContextStateForTest.IsActive("pivot.active").Should().BeFalse();

                window.SelectClickedCell(pivot.TargetRange.Start, KeyModifiers.None);
                window.RibbonContextStateForTest.IsActive("pivot.active").Should().BeTrue(
                    "the WPF shell shows PivotTable contextual tabs after the active cell enters the pivot");

                window.SelectClickedCell(outside, KeyModifiers.None);
                window.RibbonContextStateForTest.IsActive("pivot.active").Should().BeFalse(
                    "leaving the pivot must retract its contextual tabs");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static GridRange Range(
        SheetId sheetId,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
}
