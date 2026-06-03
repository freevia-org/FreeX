using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FlashFillServiceTests
{
    // ── Constant fill ─────────────────────────────────────────────────────────

    [Fact]
    public void Fill_ConstantPattern_FillsAllWithConstant()
    {
        var result = FlashFillService.Fill(
            [("Alice", "Hello"), ("Bob", "Hello")],
            ["Carol", "Dave"]);

        result.Should().BeEquivalentTo(["Hello", "Hello"], o => o.WithStrictOrdering());
    }

    // ── Case transforms ───────────────────────────────────────────────────────

    [Fact]
    public void Fill_UpperCase_TransformsSourceToUpper()
    {
        var result = FlashFillService.Fill(
            [("alice", "ALICE"), ("bob", "BOB")],
            ["carol"]);

        result.Should().BeEquivalentTo(["CAROL"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_LowerCase_TransformsSourceToLower()
    {
        var result = FlashFillService.Fill(
            [("ALICE", "alice"), ("BOB", "bob")],
            ["CAROL"]);

        result.Should().BeEquivalentTo(["carol"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ProperCase_TransformsSourceToTitleCase()
    {
        var result = FlashFillService.Fill(
            [("alice smith", "Alice Smith")],
            ["bob jones"]);

        result.Should().BeEquivalentTo(["Bob Jones"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_InconsistentCasePattern_ReturnsNull()
    {
        // "alice" → "ALICE" suggests UPPER, but "bob" → "Bob" suggests PROPER
        var result = FlashFillService.Fill(
            [("alice", "ALICE"), ("bob", "Bob")],
            ["carol"]);

        result.Should().BeNull();
    }

}

public sealed class FlashFillCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    [Fact]
    public void FlashFillCommand_Apply_FillsBlankCellsUsingDetectedPattern()
    {
        var (wb, sheet, ctx) = Setup();
        // Col A = source data (col index 1)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("John Smith"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jane Doe"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Bob Brown"));
        // Col B = fill column (col index 2): user typed example in row 1
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("John"));
        // Rows 2 and 3 in col B are blank

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 2, sourceColIndex: 1, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        (sheet.GetValue(2, 2) as TextValue)?.Value.Should().Be("Jane");
        (sheet.GetValue(3, 2) as TextValue)?.Value.Should().Be("Bob");
    }

    [Fact]
    public void FlashFillCommand_Revert_RestoresBlankCells()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("John Smith"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jane Doe"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Bob Brown"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("John"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 2, sourceColIndex: 1, startRow: 1, endRow: 3);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        // B2 and B3 should be blank again
        sheet.GetValue(2, 2).Should().BeOfType<BlankValue>();
        sheet.GetValue(3, 2).Should().BeOfType<BlankValue>();
    }

    [Fact]
    public void FlashFillCommand_NoPattern_ReturnsFailureOutcome()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Alice"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Bob"));
        // Examples that have no consistent pattern
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("world"));
        // Row 3 is blank (the only row to fill) — but two examples with no pattern

        // Put something to fill
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Carol"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 2, sourceColIndex: 1, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Command_SourceColumnOnRight_FillsCorrectly()
    {
        var wb    = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx   = new SimpleCtx(wb);

        // Source data in col 2: "ALICE", "BOB", "CAROL"
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("ALICE"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("BOB"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("CAROL"));

        // Example in col 1 row 1: "alice" (LOWER pattern)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("alice"));

        // Fill col=1, source col=2
        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 1, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("bob"));
        sheet.GetCell(3, 1)!.Value.Should().Be(new TextValue("carol"));
    }

    [Fact]
    public void FlashFillCommand_WhenTwoLeftColumnsArePopulated_UsesMultiColumnPatternFirst()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Ada Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Grace Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Alan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Turing"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("Alan Turing"));
    }

    [Fact]
    public void FlashFillCommand_SelectedRangeWithBlankSourceRow_FillsPopulatedRows()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Ada Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Grace Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Alan"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Turing"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 4);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(3, 3).Should().BeOfType<BlankValue>();
        sheet.GetCell(4, 3)!.Value.Should().Be(new TextValue("Alan Turing"));
        outcome.AffectedCells.Should().Equal(new CellAddress(sheet.Id, 4, 3));
    }

    [Fact]
    public void FlashFillCommand_WithFirstLastEmailExamples_UsesInferredDomain()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("ada.lovelace@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("grace.hopper@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Alan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Turing"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("alan.turing@contoso.com"));
    }

    [Fact]
    public void FlashFillCommand_WithFirstInitialLastEmailExamples_UsesInferredDomain()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("alovelace@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("ghopper@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Alan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Turing"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("aturing@contoso.com"));
    }

    [Fact]
    public void FlashFillCommand_WithLastFirstInitialEmailExamples_UsesInferredDomain()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("lovelacea@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("hopperg@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Alan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Turing"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("turinga@contoso.com"));
    }

    [Fact]
    public void FlashFillCommand_WhenSourceColumnIsNotImmediateLeft_UsesSingleSourcePattern()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Ada Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("ADA LOVELACE"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Grace Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("GRACE HOPPER"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Wrong"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new TextValue("ALAN TURING"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 4, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("Alan Turing"));
    }

    [Fact]
    public void Fill_StripThousandSeparators_RemovesCommasFromNumbers()
    {
        var result = FlashFillService.Fill(
            [("1,234", "1234"), ("5,678", "5678")],
            ["9,000", "12,345"]);

        result.Should().BeEquivalentTo(["9000", "12345"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_StripThousandSeparators_HandlesMultipleGroupSeparators()
    {
        var result = FlashFillService.Fill(
            [("1,234,567", "1234567"), ("9,000,001", "9000001")],
            ["2,500,000"]);

        result.Should().BeEquivalentTo(["2500000"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_StripThousandSeparators_HandlesMixedDecimalAndGrouping()
    {
        var result = FlashFillService.Fill(
            [("1,234.56", "1234.56"), ("9,000.00", "9000.00")],
            ["2,500.75"]);

        result.Should().BeEquivalentTo(["2500.75"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractDigitsOnly_StripsAllNonDigitCharacters()
    {
        var result = FlashFillService.Fill(
            [("(555) 867-5309", "5558675309"), ("(800) 555-0100", "8005550100")],
            ["(212) 555-1234"]);

        result.Should().BeEquivalentTo(["2125551234"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractDigitsOnly_WorksWithDashSeparatedFormats()
    {
        var result = FlashFillService.Fill(
            [("123-45-6789", "123456789"), ("987-65-4321", "987654321")],
            ["555-12-3456"]);

        result.Should().BeEquivalentTo(["555123456"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractDigitsOnly_ReturnsNullWhenSourceHasNoDigits()
    {
        var result = FlashFillService.Fill(
            [("(555) 867-5309", "5558675309"), ("(800) 555-0100", "8005550100")],
            ["no digits here"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_ExtractFinalDigitRun_HandlesLastFourAcrossMixedPhoneFormats()
    {
        var result = FlashFillService.Fill(
            [("(555) 867-5309", "5309"), ("800-555-0100", "0100")],
            ["212.555.1234", "main x6789"]);

        result.Should().BeEquivalentTo(["1234", "6789"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractFinalDigitRun_ReturnsNullWhenRemainingHasNoDigits()
    {
        var result = FlashFillService.Fill(
            [("(555) 867-5309", "5309"), ("800-555-0100", "0100")],
            ["no extension"]);

        result.Should().BeNull();
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
