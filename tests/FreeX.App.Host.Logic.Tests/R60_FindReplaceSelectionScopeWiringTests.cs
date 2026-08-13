using System.Reflection;

using FluentAssertions;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R60-commands-find-replace-6-2: FindOptions.SelectionScope existed (added for Round 48) but no
/// production UI ever populated it -- the WPF FindReplaceDialog's CreateFindOptions() and the
/// Avalonia shell's CreateFindOptions() both constructed FindOptions without SelectionScope, so
/// Excel's documented behavior ("when more than one cell is selected before Find &amp; Replace is
/// opened, Replace All/Find All is restricted to that selection") was permanently dead: Replace All
/// always rewrote the whole sheet/workbook even with a multi-cell selection active.
///
/// The fix captures the grid's selected range once, when the dialog opens (via the WPF dialog's
/// Loaded event reading its Owner MainWindow's SheetGrid.SelectedRange, and via the Avalonia shell
/// reading _session.SelectedRange inline at dialog-construction time), and threads it through as
/// FindOptions.SelectionScope whenever more than one cell was selected.
/// </summary>
public sealed class R60_FindReplaceSelectionScopeWiringTests
{
    [Fact]
    public void WpfFindReplaceDialog_MultiCellSelectionAtOpen_PopulatesSelectionScope()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var range = new GridRange(
                    new CellAddress(sheet.Id, 2, 2),
                    new CellAddress(sheet.Id, 3, 2)); // B2:B3 -- more than one cell
                window.SheetGrid.SelectedRange = range;

                var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
                var dialog = new FindReplaceDialog(
                    () => workbook,
                    command => commandBus.Execute(workbook.Id, command),
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

                    // Pre-fix, CreateFindOptions() never set SelectionScope at all -- this would be null.
                    options.SelectionScope.Should().NotBeNull(
                        "a multi-cell selection at dialog-open time must restrict Replace All, matching Excel");
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

    [Fact]
    public void WpfFindReplaceDialog_SingleCellSelectionAtOpen_LeavesSelectionScopeNull()
    {
        // Sibling no-regression case: Excel only restricts Replace All when MORE than one cell was
        // selected before opening; a single active cell (the common case) must keep searching the
        // whole Within-scoped sheet/workbook exactly as before this fix.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var singleCell = new GridRange(
                    new CellAddress(sheet.Id, 2, 2),
                    new CellAddress(sheet.Id, 2, 2)); // B2 only
                window.SheetGrid.SelectedRange = singleCell;

                var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
                var dialog = new FindReplaceDialog(
                    () => workbook,
                    command => commandBus.Execute(workbook.Id, command),
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
                    options.SelectionScope.Should().BeNull();
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
    public void WpfFindReplaceDialog_ReplaceAll_WithMultiCellSelection_OnlyReplacesInsideSelection()
    {
        // End-to-end: the finding's exact concrete scenario. B2:D5 is selected (contains "2024" at
        // B3); A100 (outside the selection) also contains "2024". Replace All must leave A100 alone.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var b3 = new CellAddress(sheet.Id, 3, 2);
                var a100 = new CellAddress(sheet.Id, 100, 1);
                // "FY2024" (not a pure number) avoids the replaced text being re-parsed as a NumberValue,
                // which would happen if the cell ended up holding the bare digits "2025".
                sheet.SetCell(b3, new TextValue("FY2024"));
                sheet.SetCell(a100, new TextValue("FY2024"));

                var range = new GridRange(
                    new CellAddress(sheet.Id, 2, 2),
                    new CellAddress(sheet.Id, 5, 4)); // B2:D5
                window.SheetGrid.SelectedRange = range;

                var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
                var dialog = new FindReplaceDialog(
                    () => workbook,
                    command => commandBus.Execute(workbook.Id, command),
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

                    sheet.GetValue(b3.Row, b3.Col).Should().Be(new TextValue("FY2025"), "B3 is inside the selection scope");
                    sheet.GetValue(a100.Row, a100.Col).Should().Be(
                        new TextValue("FY2024"),
                        "A100 is outside the selection scope and must be left untouched");
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
