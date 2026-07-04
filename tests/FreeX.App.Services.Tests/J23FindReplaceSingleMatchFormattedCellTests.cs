using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for J23 on the single-match Replace path: the Find &amp; Replace dialog's
/// "Replace" button goes through FindReplaceDialogPlanner.TryReplaceSingleMatch, which must pass
/// the workbook down to FindReplaceService.TryCreateReplacementCommand so Values-mode replacement
/// operates on the same number-format-aware display text Find matched. Without it, Replace All
/// works on formatted currency/percent/date cells while single-match Replace silently skips them.
/// Mirrors the ReplaceAll scenarios in FreeX.Integration.Tests/FindReplaceServiceParityTests.
/// </summary>
public sealed class J23FindReplaceSingleMatchFormattedCellTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandBus CommandBus) Setup()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbook));
        return (workbook, sheet, commandBus);
    }

    [Fact]
    public void TryReplaceSingleMatch_CurrencyCell_ReplacesFormattedDisplayText()
    {
        // Cell holds 1000 formatted as currency, displaying "$1,000.00" (what Find matched).
        // The single-match Replace must not silently skip it just because the invariant raw
        // value ("1000") doesn't contain the searched formatted string.
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var currencyStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var cell = Cell.FromValue(new NumberValue(1000));
        cell.StyleId = currencyStyle;
        sheet.SetCell(a1, cell);

        var match = new FindResult(a1, "$1,000.00");
        var result = FindReplaceDialogPlanner.TryReplaceSingleMatch(
            wb,
            commandBus,
            match,
            "$1,000.00",
            "$2,000.00",
            matchCase: false,
            matchEntireCell: false);

        result.Replaced.Should().BeTrue(result.Failure?.ErrorMessage);
        var updated = sheet.GetCell(a1)!;
        // Replacement text re-parses as a number (same as manual entry), preserving the
        // currency format and value semantics.
        updated.Value.Should().Be(new NumberValue(2000));
        updated.StyleId.Should().Be(currencyStyle);
    }

    [Fact]
    public void TryReplaceSingleMatch_PercentCell_ReplacesFormattedDisplayText()
    {
        // 0.5 formatted as "0%" displays "50%". Replacing "50%" with "75%" must update the
        // underlying value to 0.75, not skip the cell or store literal text.
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var percentStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "0%" });
        var cell = Cell.FromValue(new NumberValue(0.5));
        cell.StyleId = percentStyle;
        sheet.SetCell(a1, cell);

        var match = new FindResult(a1, "50%");
        var result = FindReplaceDialogPlanner.TryReplaceSingleMatch(
            wb,
            commandBus,
            match,
            "50%",
            "75%",
            matchCase: false,
            matchEntireCell: false);

        result.Replaced.Should().BeTrue(result.Failure?.ErrorMessage);
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(0.75));
    }

    [Fact]
    public void TryReplaceSingleMatch_UnformattedNumberCell_StillReplacesByInvariantText()
    {
        // Default-styled numeric cells keep working exactly as before: invariant rendering
        // ("42") is both what Find matches and what Replace operates on.
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(42));

        var match = new FindResult(a1, "42");
        var result = FindReplaceDialogPlanner.TryReplaceSingleMatch(
            wb,
            commandBus,
            match,
            "42",
            "43",
            matchCase: false,
            matchEntireCell: false);

        result.Replaced.Should().BeTrue(result.Failure?.ErrorMessage);
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(43));
    }
}
