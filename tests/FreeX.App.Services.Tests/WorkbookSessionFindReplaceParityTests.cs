using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for J23/J43 on the WorkbookSession Replace path used by the Avalonia
/// shell's Find &amp; Replace dialog (ReplaceNextValue / ReplaceAllValues). WorkbookSession used to
/// carry a private duplicate of FindReplaceService's replacement logic whose Values-mode matched
/// against the unformatted invariant value text, so Replace silently no-opped on any formatted
/// number/date/percentage/currency cell that Find had matched by its formatted display text, and
/// wildcards were treated literally. The session now delegates to
/// FindReplaceService.TryCreateReplacementCommand (passing the workbook), so these tests mirror
/// the currency/percent/wildcard scenarios from FreeX.Integration.Tests/FindReplaceServiceParityTests
/// through the session APIs.
/// </summary>
public sealed class WorkbookSessionFindReplaceParityTests
{
    [Fact]
    public void ReplaceAllValues_CurrencyCell_ReplacesFormattedDisplayTextAndPreservesStyle()
    {
        // Cell holds 1000 formatted as currency, displaying "$1,000.00" (what Find matched).
        // Replace All must not silently skip it just because the invariant raw value ("1000")
        // doesn't contain the searched formatted string.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var currencyStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var cell = Cell.FromValue(new NumberValue(1000));
        cell.StyleId = currencyStyle;
        sheet.SetCell(a1, cell);
        var session = CreateSession(workbook);

        var result = session.ReplaceAllValues("$1,000.00", "$2,000.00");

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ReplacedCount.Should().Be(1);
        var updated = sheet.GetCell(a1)!;
        // Replacement text re-parses as a number (same as manual entry), preserving the
        // currency format and value semantics.
        updated.Value.Should().Be(new NumberValue(2000));
        updated.StyleId.Should().Be(currencyStyle);
    }

    [Fact]
    public void ReplaceNextValue_PercentCell_ReplacesFormattedDisplayText()
    {
        // 0.5 formatted as "0%" displays "50%". The single-match Replace must update the
        // underlying value to 0.75, not report a replacement without changing the cell.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var percentStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "0%" });
        var cell = Cell.FromValue(new NumberValue(0.5));
        cell.StyleId = percentStyle;
        sheet.SetCell(a1, cell);
        var session = CreateSession(workbook);

        var result = session.ReplaceNextValue("50%", "75%");

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ReplacedCount.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(0.75));
    }

    [Fact]
    public void ReplaceAllValues_FormattedCell_NonNumericReplacement_StoresLiteralText()
    {
        // Matching Excel: if the replacement text does not re-parse into a number, the cell
        // becomes literal text (same as typing non-numeric text over a numeric cell).
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var currencyStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var cell = Cell.FromValue(new NumberValue(1000));
        cell.StyleId = currencyStyle;
        sheet.SetCell(a1, cell);
        var session = CreateSession(workbook);

        var result = session.ReplaceAllValues("$1,000.00", "N/A");

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ReplacedCount.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("N/A"));
    }

    [Fact]
    public void ReplaceAllValues_WildcardPattern_ReplacesEachMatchWithLiteralReplacementText()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("Product A, Product B"));
        var session = CreateSession(workbook);

        var result = session.ReplaceAllValues("Product ?", "Item");

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ReplacedCount.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("Item, Item"));
    }

    [Fact]
    public void ReplaceNextValue_TildeEscape_SearchesLiteralAsteriskNotWildcard()
    {
        // "~*" must match a literal '*' only, not act as a glob — the cell without the literal
        // asterisk stays untouched even though a glob interpretation would match it.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, new TextValue("total*"));
        sheet.SetCell(a2, new TextValue("totalX"));
        var session = CreateSession(workbook);

        var result = session.ReplaceNextValue("total~*", "sum", matchEntireCell: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ReplacedCount.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("sum"));
        sheet.GetCell(a2)!.Value.Should().Be(new TextValue("totalX"));
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
