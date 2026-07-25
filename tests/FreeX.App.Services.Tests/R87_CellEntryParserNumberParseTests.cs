using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R87-formula-number-parse-locale-5-1..5-4: four cell-entry parsing gaps fixed together --
/// (1) a time-only literal ("15:30"/"3:30 PM") never parsed as a time, always falling through to
/// text; (2) typed entry never honored the target cell's Text ("@") number format, so it still got
/// numeric/date coercion; (3) typing a percent/currency/fraction/time literal into a General cell
/// never auto-applied the matching number format; (4) a plain (no currency symbol) parenthesized
/// negative like "(123)" wasn't recognized as -123.
/// </summary>
public sealed class R87_CellEntryParserNumberParseTests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    private static Workbook CreateWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("Book");
        sheet = workbook.AddSheet("Sheet1");
        return workbook;
    }

    // --- Finding 1: time-only entry ------------------------------------------------------------

    [Fact]
    public void CreateCell_ConvertsTwentyFourHourTimeOnlyLiteralToATimeSerial()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("15:30", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>()
            .Which.Value.Should().BeApproximately(15.0 * 3600 / 86400.0 + 30.0 * 60 / 86400.0, 1e-9);
    }

    [Fact]
    public void CreateCell_ConvertsTwelveHourAmPmTimeOnlyLiteralToATimeSerial()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("3:30 PM", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>()
            .Which.Value.Should().BeApproximately(15.0 * 3600 / 86400.0 + 30.0 * 60 / 86400.0, 1e-9);
    }

    [Fact]
    public void CreateCell_AppliesATimeNumberFormatWhenTimeOnlyLiteralTypedIntoGeneralCell()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = CreateWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 2, 2);

        var cell = CellEntryParser.CreateCell("15:30", address, useR1C1ReferenceStyle: false, workbook);

        workbook.GetStyle(cell.StyleId).NumberFormat.Should().Be("h:mm AM/PM");
    }

    // Sibling/no-regression: a full date literal must still parse via the ordinary date path
    // (unaffected by the new time-only branch), and must NOT get an auto-applied number format --
    // only the auto-inferred percent/currency/fraction/time shapes do (matching pre-existing
    // behavior for dates, which already render correctly under General format).
    [Fact]
    public void CreateCell_StillConvertsFullDateLiteralViaOrdinaryDatePathWithNoStyleChange()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = CreateWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 2, 2);

        var cell = CellEntryParser.CreateCell("1/2/2024", address, useR1C1ReferenceStyle: false, workbook);

        cell.Value.Should().BeOfType<DateTimeValue>()
            .Which.ToDateTime().Should().Be(new DateTime(2024, 1, 2));
        cell.StyleId.Should().Be(StyleId.Default);
    }

    // --- Finding 2: target cell's Text ("@") format must be honored ----------------------------

    [Fact]
    public void CreateCell_KeepsLeadingZerosAsLiteralTextWhenTargetCellIsTextFormatted()
    {
        var workbook = CreateWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 3, 3);
        var textStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "@" });
        sheet.SetCell(address, new Cell { StyleId = textStyle });

        var cell = CellEntryParser.CreateCell("007", address, useR1C1ReferenceStyle: false, workbook);

        cell.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("007");
    }

    [Fact]
    public void CreateCell_KeepsDateLiteralAsLiteralTextWhenTargetCellIsTextFormatted()
    {
        var workbook = CreateWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 3, 3);
        var textStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "@" });
        sheet.SetCell(address, new Cell { StyleId = textStyle });

        var cell = CellEntryParser.CreateCell("1/2/2024", address, useR1C1ReferenceStyle: false, workbook);

        cell.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("1/2/2024");
    }

    // Sibling/no-regression: the same literal into a plain (non-Text) General-formatted cell must
    // still coerce numerically -- Finding 2's target-format check must not suppress ordinary
    // coercion for the common case.
    [Fact]
    public void CreateCell_StillCoercesLeadingZerosNumberWhenTargetCellIsGeneralFormatted()
    {
        var workbook = CreateWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 3, 3);

        var cell = CellEntryParser.CreateCell("007", address, useR1C1ReferenceStyle: false, workbook);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(7);
    }

    // --- Finding 3: percent/currency/fraction auto-applies the matching format -----------------

    [Fact]
    public void CreateCell_AppliesPercentNumberFormatWhenPercentLiteralTypedIntoGeneralCell()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = CreateWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 4, 4);

        var cell = CellEntryParser.CreateCell("50%", address, useR1C1ReferenceStyle: false, workbook);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(0.5);
        workbook.GetStyle(cell.StyleId).NumberFormat.Should().Be("0%");
    }

    [Fact]
    public void CreateCell_AppliesCurrencyNumberFormatWhenDollarLiteralTypedIntoGeneralCell()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = CreateWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 4, 4);

        var cell = CellEntryParser.CreateCell("$5", address, useR1C1ReferenceStyle: false, workbook);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(5);
        workbook.GetStyle(cell.StyleId).NumberFormat.Should().Be("$#,##0.00");
    }

    [Fact]
    public void CreateCell_AppliesFractionNumberFormatWhenFractionLiteralTypedIntoGeneralCell()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = CreateWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 4, 4);

        var cell = CellEntryParser.CreateCell("1 1/2", address, useR1C1ReferenceStyle: false, workbook);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(1.5);
        workbook.GetStyle(cell.StyleId).NumberFormat.Should().Be("# ?/?");
    }

    // Sibling/no-regression: a cell that already carries an explicit non-General number format
    // must NOT have it silently overwritten by the auto-inferred shape -- Excel only auto-applies
    // when the destination is currently General.
    [Fact]
    public void CreateCell_DoesNotOverwriteAnAlreadyExplicitNumberFormatWhenPercentLiteralTyped()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = CreateWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 4, 4);
        var existingStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.0000" });
        sheet.SetCell(address, new Cell { StyleId = existingStyle });

        var cell = CellEntryParser.CreateCell("50%", address, useR1C1ReferenceStyle: false, workbook);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(0.5);
        cell.StyleId.Should().Be(StyleId.Default);
    }

    // --- Finding 4: plain parenthesized negative -----------------------------------------------

    [Fact]
    public void CreateCell_ConvertsPlainParenthesizedIntegerToItsNegativeValue()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("(123)", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(-123);
    }

    [Fact]
    public void CreateCell_ConvertsPlainParenthesizedGroupedDecimalToItsNegativeValue()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("(1,234.56)", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(-1234.56);
    }

    // Sibling/no-regression: the already-working currency-marked parenthesized form must keep
    // working unaffected by the new plain-parenthesis support.
    [Fact]
    public void CreateCell_StillConvertsCurrencyMarkedParenthesizedLiteralToItsNegativeValue()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("($123)", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(-123);
    }

    // Non-US locale sanity check: de-DE uses '.' as thousands separator and ',' as decimal
    // separator; a plain parenthesized de-DE-formatted number must still negate correctly.
    [Fact]
    public void CreateCell_ConvertsPlainParenthesizedNumberUnderNonUsLocale()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");

        var cell = CellEntryParser.CreateCell("(1.234,56)", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(-1234.56);
    }

    [Fact]
    public void CreateCell_ConvertsTwentyFourHourTimeOnlyLiteralUnderNonUsLocale()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");

        var cell = CellEntryParser.CreateCell("15:30", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>()
            .Which.Value.Should().BeApproximately(15.0 * 3600 / 86400.0 + 30.0 * 60 / 86400.0, 1e-9);
    }

    // --- Finding 3 (end-to-end): EditCellsCommand.Apply must not clobber the auto-inferred style -

    private static (Workbook Workbook, Sheet Sheet, WorkbookCellEditService Service) CreateEditService()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var recalcEngine = new FreeX.Core.Calc.RecalcEngine(new FreeX.Core.Calc.DependencyGraph(), new FreeX.Core.Formula.FormulaEvaluator());
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbook));
        var service = new WorkbookCellEditService(commandBus, recalcEngine);
        return (workbook, sheet, service);
    }

    [Fact]
    public void CommitCellText_KeepsTheAutoInferredPercentFormatThroughEditCellsCommand()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var (workbook, sheet, service) = CreateEditService();
        var address = new CellAddress(sheet.Id, 1, 1);

        var result = service.CommitCellText(workbook, sheet.Id, address, "50%");

        result.Success.Should().BeTrue();
        var cell = sheet.GetCell(address)!;
        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(0.5);
        workbook.GetStyle(cell.StyleId).NumberFormat.Should().Be("0%");
    }

    // Sibling/no-regression: an ordinary (non-shape-inferring) edit through the same command path
    // must still preserve the cell's pre-existing formatting exactly as before.
    [Fact]
    public void CommitCellText_StillPreservesExistingFormattingForAnOrdinaryNumberEdit()
    {
        var (workbook, sheet, service) = CreateEditService();
        var address = new CellAddress(sheet.Id, 1, 1);
        var boldStyle = workbook.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetCell(address, new Cell { StyleId = boldStyle, Value = new NumberValue(1) });

        var result = service.CommitCellText(workbook, sheet.Id, address, "4");

        result.Success.Should().BeTrue();
        var cell = sheet.GetCell(address)!;
        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(4);
        cell.StyleId.Should().Be(boldStyle);
    }
}
