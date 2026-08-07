using System.Reflection;

using FluentAssertions;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R127-findreplace-selectionscope-multiarea-1
/// (src/FreeX.App.Host/FindReplaceDialog.xaml.cs).
///
/// Excel restricts Replace All / Find All to the pre-open selection whenever more than one cell was
/// selected before opening Find &amp; Replace -- INCLUDING a multi-area (Ctrl+click) selection, where the
/// scope is the UNION of every disjoint area, not just the last one clicked. Before this fix,
/// CaptureSelectionScopeAtOpen only ever read <c>SheetGrid.SelectedRange</c> (a single GridRange), never
/// <c>SheetGrid.SelectedRanges</c> (the app's actual multi-area representation, set via
/// SetSelectedRangesIfChanged for a Ctrl+click additional area -- see MainWindow.Selection.cs). A user who
/// selected B2:C4, then Ctrl+clicked to also select E2:F4, had SheetGrid.SelectedRange collapsed to just the
/// newest area (E2:F4) while SheetGrid.SelectedRanges held the full union ([B2:C4, E2:F4]); Replace All
/// silently dropped matches inside B2:C4 even though it stayed visibly selected.
///
/// The fix resolves the scope through SelectionStyleCommandPlanner.ResolveRanges (the same choke point
/// MainWindow.CommandExecution.cs already uses for this exact SelectedRange/SelectedRanges duality),
/// which prefers SheetGrid.SelectedRanges when populated and falls back to SheetGrid.SelectedRange
/// otherwise.
/// </summary>
public sealed class R127_FindReplaceMultiAreaSelectionScopeTests
{
    [Fact]
    public void WpfFindReplaceDialog_MultiAreaCtrlClickSelectionAtOpen_PopulatesSelectionScopeWithBothAreas()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var areaOne = new GridRange(
                    new CellAddress(sheet.Id, 2, 2),
                    new CellAddress(sheet.Id, 4, 3)); // B2:C4
                var areaTwo = new GridRange(
                    new CellAddress(sheet.Id, 2, 5),
                    new CellAddress(sheet.Id, 4, 6)); // E2:F4

                // Mirrors MainWindow.Selection.cs's Ctrl+click handling: SelectedRanges accumulates
                // BOTH disjoint areas, while SelectedRange collapses to only the newest one clicked.
                window.SheetGrid.SelectedRanges = [areaOne, areaTwo];
                window.SheetGrid.SelectedRange = areaTwo;

                var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
                var dialog = new FindReplaceDialog(
                    () => workbook,
                    commandBus,
                    _ => { },
                    replaceMode: true,
                    getCurrentSheetId: () => sheet.Id,
                    getActiveSelectionCell: () => window.SheetGrid.SelectedRange?.Start)
                {
                    Owner = window
                };
                dialog.Show();
                try
                {
                    var options = InvokeCreateFindOptions(dialog);

                    // Pre-fix, this would be a single-element list containing only areaTwo (E2:F4),
                    // silently dropping areaOne (B2:C4) even though it stayed visibly selected.
                    options.SelectionScope.Should().NotBeNull(
                        "a multi-area Ctrl+click selection at dialog-open time must restrict Replace All, matching Excel");
                    options.SelectionScope.Should().BeEquivalentTo(
                        new[] { areaOne, areaTwo },
                        "the scope must be the UNION of every disjoint Ctrl+click area, not just the last one clicked");
                }
                finally
                {
                    dialog.Close();
                }
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void WpfFindReplaceDialog_ReplaceAll_WithMultiAreaSelection_ReplacesInsideEveryArea()
    {
        // End-to-end: the finding's exact concrete scenario. B2:C4 and E2:F4 are both selected
        // (Ctrl+click); "2024" appears inside each area plus at A100 (outside both). Replace All must
        // touch both selected areas and leave A100 alone.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var b3 = new CellAddress(sheet.Id, 3, 2); // inside B2:C4
                var e3 = new CellAddress(sheet.Id, 3, 5); // inside E2:F4
                var a100 = new CellAddress(sheet.Id, 100, 1); // outside both areas
                sheet.SetCell(b3, new TextValue("FY2024"));
                sheet.SetCell(e3, new TextValue("FY2024"));
                sheet.SetCell(a100, new TextValue("FY2024"));

                var areaOne = new GridRange(
                    new CellAddress(sheet.Id, 2, 2),
                    new CellAddress(sheet.Id, 4, 3)); // B2:C4
                var areaTwo = new GridRange(
                    new CellAddress(sheet.Id, 2, 5),
                    new CellAddress(sheet.Id, 4, 6)); // E2:F4
                window.SheetGrid.SelectedRanges = [areaOne, areaTwo];
                window.SheetGrid.SelectedRange = areaTwo;

                var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
                var dialog = new FindReplaceDialog(
                    () => workbook,
                    commandBus,
                    _ => { },
                    replaceMode: true,
                    getCurrentSheetId: () => sheet.Id,
                    getActiveSelectionCell: () => window.SheetGrid.SelectedRange?.Start)
                {
                    Owner = window
                };
                dialog.Show();
                try
                {
                    DialogSourceTestSupport.GetPrivateField<System.Windows.Controls.TextBox>(dialog, "ReplaceFindBox").Text = "2024";
                    DialogSourceTestSupport.GetPrivateField<System.Windows.Controls.TextBox>(dialog, "ReplaceBox").Text = "2025";

                    DialogSourceTestSupport.InvokePrivateHandler(dialog, "ReplaceAll_Click");

                    sheet.GetValue(b3.Row, b3.Col).Should().Be(new TextValue("FY2025"), "B3 is inside the first selected area");
                    sheet.GetValue(e3.Row, e3.Col).Should().Be(new TextValue("FY2025"), "E3 is inside the second (Ctrl+click) selected area");
                    sheet.GetValue(a100.Row, a100.Col).Should().Be(
                        new TextValue("FY2024"),
                        "A100 is outside both selected areas and must be left untouched");
                }
                finally
                {
                    dialog.Close();
                }
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void WpfFindReplaceDialog_MultiCellContiguousSelectionAtOpen_StillPopulatesSelectionScope()
    {
        // Sibling no-regression case: this fix must not disturb the ordinary single-area multi-cell
        // scenario (R60_FindReplaceSelectionScopeWiringTests) -- when SelectedRanges is null/empty and
        // only SelectedRange is set, the scope must still resolve to that one range via the
        // SelectionStyleCommandPlanner.ResolveRanges fallback.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var range = new GridRange(
                    new CellAddress(sheet.Id, 2, 2),
                    new CellAddress(sheet.Id, 3, 2)); // B2:B3 -- more than one cell
                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = range;

                var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
                var dialog = new FindReplaceDialog(
                    () => workbook,
                    commandBus,
                    _ => { },
                    replaceMode: true,
                    getCurrentSheetId: () => sheet.Id,
                    getActiveSelectionCell: () => window.SheetGrid.SelectedRange?.Start)
                {
                    Owner = window
                };
                dialog.Show();
                try
                {
                    var options = InvokeCreateFindOptions(dialog);
                    options.SelectionScope.Should().NotBeNull();
                    options.SelectionScope.Should().ContainSingle().Which.Should().Be(range);
                }
                finally
                {
                    dialog.Close();
                }
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static FindOptions InvokeCreateFindOptions(FindReplaceDialog dialog)
    {
        var method = typeof(FindReplaceDialog).GetMethod("CreateFindOptions", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(FindReplaceDialog), "CreateFindOptions");
        return (FindOptions)method.Invoke(dialog, null)!;
    }
}
