using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using Free.Shared.Ribbon;
using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R80-app-ribbon-contextual-5-2: the Avalonia Table Design ▸ Style Options toggles (Total Row,
/// First Column, Last Column, Banded Rows, Banded Columns, Filter Button) were wired only as plain
/// Action delegates in BuildContextualTabCommands, with no ExtraCommandStates entry -- so
/// AvaloniaRibbonRenderer.SyncToggleStates (which only paints ToggleButtons whose registered command
/// implements IRibbonStatefulCommand) never reflected the active table's real flags, unlike the WPF
/// host's _ribbonState.SetChecked(...) calls in RefreshTableContextualTab. These tests drive the real
/// registered ribbon commands (via RibbonCommandRegistryForTest) to assert the fix's
/// GetTableStyleOptionRibbonState wiring reports the table's live state.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R80_TableDesignToggleStateAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ActiveCellInTable_TotalRowAndBandedRowsOn_RibbonCommandsReportChecked()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var table = new StructuredTableModel
                {
                    Id = 1,
                    Name = "StyledTable",
                    Range = Range(sheet.Id, 1, 1, 4, 3),
                    TotalsRowShown = true,
                    ShowRowStripes = true,
                };
                sheet.StructuredTables.Add(table);
                window.Session.SelectCell(table.Range.Start);

                var registry = window.RibbonCommandRegistryForTest!;

                // Failing before the fix: no ExtraCommandStates entry existed for these ids, so they
                // registered as a plain ActionRibbonCommand (not IRibbonStatefulCommand) and this cast failed.
                var totalRow = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Total Row"));
                totalRow.GetState().IsChecked.Should().BeTrue("the table's TotalsRowShown flag is on");

                var bandedRows = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, FreeXRibbonCommandIds.TableBandedRows));
                bandedRows.GetState().IsChecked.Should().BeTrue("the table's ShowRowStripes flag is on");

                // The flags that are OFF on this table must report unchecked, not just "some" state.
                var firstColumn = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "First Column"));
                firstColumn.GetState().IsChecked.Should().BeFalse("the table's ShowFirstColumn flag is off");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    // No-regression sibling: when the active cell is NOT inside any structured table, the toggles
    // must render the safe planner default (unchecked) rather than throwing or reporting stale state.
    [Fact]
    public async Task ActiveCellNotInTable_RibbonCommandsReportDefaultUncheckedState()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("NoTableHere");
                window.Session.SelectSheet(sheet.Id);
                window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

                var registry = window.RibbonCommandRegistryForTest!;

                var totalRow = Assert.IsAssignableFrom<IRibbonStatefulCommand>(
                    GetCommand(registry, "Total Row"));
                totalRow.GetState().Should().Be(RibbonCommandState.Default,
                    "the active cell is not inside a structured table, so the toggle must render the planner default");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static IRibbonCommand GetCommand(IRibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue(
            $"'{id}' must be a registered ribbon command");
        return command!;
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
