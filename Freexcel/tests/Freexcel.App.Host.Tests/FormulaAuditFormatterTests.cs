using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class FormulaAuditFormatterTests
{
    [Fact]
    public void FormatAddress_UsesSheetNameAndA1Reference()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Inputs");

        FormulaAuditFormatter.FormatAddress(workbook, new CellAddress(sheet.Id, 12, 3))
            .Should()
            .Be("Inputs!C12");
    }

    [Fact]
    public void FormatAddress_FallsBackWhenSheetIsMissing()
    {
        var workbook = new Workbook("Book");

        FormulaAuditFormatter.FormatAddress(workbook, new CellAddress(SheetId.New(), 1, 1))
            .Should()
            .Be("Sheet!A1");
    }

    [Fact]
    public void FormatAddresses_LimitsLongListsWithExcelStyleSummary()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Calc");
        var addresses = Enumerable.Range(1, 14)
            .Select(row => new CellAddress(sheet.Id, (uint)row, 1))
            .ToList();

        FormulaAuditFormatter.FormatAddresses(workbook, addresses)
            .Should()
            .Be("Calc!A1, Calc!A2, Calc!A3, Calc!A4, Calc!A5, Calc!A6, Calc!A7, Calc!A8, Calc!A9, Calc!A10, Calc!A11, Calc!A12\n...and 2 more.");
    }
}
