using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression coverage for two Find/Replace Excel-parity gaps (group O1-findreplace-core):
///   - Replace on a Values-mode match must operate against the same number-format-aware display
///     text Find used, so formatted numeric/percentage/currency cells are actually replaceable
///     instead of being silently skipped (J23).
///   - Find and Replace must support Excel-style wildcards ("*", "?") with "~" escapes, in both
///     the search text and across Match Entire Cell / substring modes, without regressing plain
///     literal search performance/behavior (J43).
/// </summary>
public class FindReplaceServiceParityTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandBus CommandBus) Setup()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new CommandBus(id => new TestCommandContext(workbook));
        return (workbook, sheet, commandBus);
    }

    // ── J23: Replace must operate on the same representation Find matched ───────

    [Fact]
    public void ReplaceAll_CurrencyCell_ReplacesFormattedDisplayText()
    {
        // Cell holds 1000 formatted as currency, displaying "$1,000.00" (Find already matches
        // this formatted text). Replace must not silently skip it just because the invariant
        // raw value ("1000") doesn't contain the searched formatted string.
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var currencyStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var cell = Cell.FromValue(new NumberValue(1000));
        cell.StyleId = currencyStyle;
        sheet.SetCell(a1, cell);

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "$1,000.00", "$2,000.00");

        count.Should().Be(1);
        var updated = sheet.GetCell(a1)!;
        // Replacement text re-parses as a number (Excel re-parses typed/replaced text the same
        // way as manual entry), preserving the currency format and value semantics.
        updated.Value.Should().Be(new NumberValue(2000));
        updated.StyleId.Should().Be(currencyStyle);
    }

    [Fact]
    public void ReplaceAll_PercentCell_ReplacesFormattedDisplayText()
    {
        // 0.5 formatted as "0%" displays "50%". Replacing "50%" with "75%" must update the
        // underlying value to 0.75, not skip the cell or store literal text.
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var percentStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "0%" });
        var cell = Cell.FromValue(new NumberValue(0.5));
        cell.StyleId = percentStyle;
        sheet.SetCell(a1, cell);

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "50%", "75%");

        count.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(0.75));
    }

    [Fact]
    public void ReplaceAll_FormattedCell_ReplacementThatIsNotNumeric_StoresLiteralText()
    {
        // Matching Excel: if the replacement text does not parse back into a number/date, the
        // cell becomes literal text (same as typing non-numeric text over a numeric cell).
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var currencyStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var cell = Cell.FromValue(new NumberValue(1000));
        cell.StyleId = currencyStyle;
        sheet.SetCell(a1, cell);

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "$1,000.00", "N/A");

        count.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("N/A"));
    }

    [Fact]
    public void ReplaceAll_UnformattedNumberCell_StillReplacesByInvariantText()
    {
        // Default-styled numeric cells (no custom number format) keep working exactly as before:
        // invariant rendering ("42") is both what Find matches and what Replace operates on.
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(42));

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "42", "43");

        count.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(43));
    }

    [Fact]
    public void TryCreateReplacementCommand_WithoutWorkbook_FormattedCellNotMatched_ReturnsFalse()
    {
        // Documents the fallback rule: when no workbook is supplied to the single-match replace
        // path, Values-mode matching falls back to the unformatted invariant text (legacy
        // behavior) and a formatted-only match is correctly reported as not replaceable, rather
        // than silently corrupting the cell.
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var currencyStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var cell = Cell.FromValue(new NumberValue(1000));
        cell.StyleId = currencyStyle;
        sheet.SetCell(a1, cell);

        var match = new FindResult(a1, "$1,000.00");
        var created = FindReplaceService.TryCreateReplacementCommand(
            sheet,
            match,
            "$1,000.00",
            "$2,000.00",
            matchCase: false,
            matchEntireCell: false,
            FindLookIn.Values,
            replacementFormat: null,
            out _);

        created.Should().BeFalse();
    }

    [Fact]
    public void TryCreateReplacementCommand_WithWorkbook_FormattedCellIsReplaced()
    {
        // The same scenario succeeds once the owning workbook is supplied, proving the fix is
        // reachable through the public single-match replace API used by the Find/Replace dialog.
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var currencyStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var cell = Cell.FromValue(new NumberValue(1000));
        cell.StyleId = currencyStyle;
        sheet.SetCell(a1, cell);

        var match = new FindResult(a1, "$1,000.00");
        var created = FindReplaceService.TryCreateReplacementCommand(
            sheet,
            match,
            "$1,000.00",
            "$2,000.00",
            matchCase: false,
            matchEntireCell: false,
            FindLookIn.Values,
            replacementFormat: null,
            out var command,
            workbook: wb);

        created.Should().BeTrue();
        var outcome = commandBus.Execute(wb.Id, command);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(2000));
    }

    // ── J43: wildcard support (*, ?, ~ escapes) in Find and Replace ──────────────

    [Fact]
    public void Find_StarWildcard_MatchesAnySuffix()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new TextValue("Product A"));
        sheet.SetCell(a2, new TextValue("Product B"));
        sheet.SetCell(a3, new TextValue("Service X"));

        var results = FindReplaceService.Find(wb, "Product*");

        results.Select(r => r.Address).Should().Equal(a1, a2);
    }

    [Fact]
    public void Find_QuestionMarkWildcard_MatchesExactlyOneChar()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, new TextValue("cat"));
        sheet.SetCell(a2, new TextValue("cart"));

        var results = FindReplaceService.Find(wb, "ca?", matchEntireCell: true);

        results.Should().ContainSingle().Which.Address.Should().Be(a1);
    }

    [Fact]
    public void Find_TildeEscape_MatchesLiteralAsterisk()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, new TextValue("total*"));
        sheet.SetCell(a2, new TextValue("totalX"));

        // "~*" must match a literal '*' only, not act as a glob.
        var results = FindReplaceService.Find(wb, "total~*", matchEntireCell: true);

        results.Should().ContainSingle().Which.Address.Should().Be(a1);
    }

    [Fact]
    public void Find_TildeEscape_MatchesLiteralQuestionMark()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, new TextValue("really?"));
        sheet.SetCell(a2, new TextValue("reallyX"));

        var results = FindReplaceService.Find(wb, "really~?", matchEntireCell: true);

        results.Should().ContainSingle().Which.Address.Should().Be(a1);
    }

    [Fact]
    public void Find_TildeEscape_MatchesLiteralTilde()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("~approx"));

        var results = FindReplaceService.Find(wb, "~~approx", matchEntireCell: true);

        results.Should().ContainSingle().Which.Address.Should().Be(a1);
    }

    [Fact]
    public void Find_WildcardSubstring_MatchesAnywhereWhenNotEntireCell()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("prefix Product Z suffix"));

        var results = FindReplaceService.Find(wb, "Product*", matchEntireCell: false);

        results.Should().ContainSingle().Which.Address.Should().Be(a1);
    }

    [Fact]
    public void Find_Wildcard_CaseInsensitiveByDefault()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("PRODUCT A"));

        var results = FindReplaceService.Find(wb, "product*");

        results.Should().ContainSingle().Which.Address.Should().Be(a1);
    }

    [Fact]
    public void Find_Wildcard_MatchCase_RespectsCaseSensitivity()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("PRODUCT A"));

        var results = FindReplaceService.Find(wb, "product*", matchCase: true);

        results.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceAll_WildcardPattern_ReplacesEachMatchWithLiteralReplacementText()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("Product A, Product B"));

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "Product ?", "Item");

        count.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("Item, Item"));
    }

    [Fact]
    public void ReplaceAll_WildcardMatchEntireCell_ReplacesWholeCellWithLiteralText()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("Product A"));

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "Product*", "Renamed", matchEntireCell: true);

        count.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("Renamed"));
    }

    [Fact]
    public void ReplaceAll_WildcardReplacement_DoesNotTreatDollarSignAsBackreference()
    {
        // Regression guard: Regex.Replace(string,string) would treat "$1"/"$$" in the
        // replacement as backreference syntax. Wildcard replace must use the replacement text
        // completely literally, matching Excel (which never expands wildcards on the
        // replacement side).
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("Product A"));

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "Product*", "$1,000.00 total");

        count.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("$1,000.00 total"));
    }

    [Fact]
    public void ReplaceAll_TildeEscape_LiteralAsteriskInSearchTextIsNotTreatedAsWildcard()
    {
        // "~*" must search for a literal asterisk, not a glob — same rule for Replace as for Find.
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, new TextValue("total*"));
        sheet.SetCell(a2, new TextValue("totalX"));

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "total~*", "sum", matchEntireCell: true);

        count.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("sum"));
        sheet.GetCell(a2)!.Value.Should().Be(new TextValue("totalX"));
    }

    [Fact]
    public void Find_PlainLiteralSearch_HasNoWildcardBehaviorRegression()
    {
        // Sanity/perf-guard: a plain literal pattern with no '*'/'?' still behaves exactly as
        // the pre-wildcard implementation (simple substring containment), including patterns
        // that happen to contain regex metacharacters other than '*'/'?'/'~'.
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("a.b(c)[d]"));

        var results = FindReplaceService.Find(wb, "a.b(c)[d]");

        results.Should().ContainSingle().Which.Address.Should().Be(a1);
    }

    [Fact]
    public void Find_WildcardInFormulaMode_MatchesFormulaText()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "SUM(B1:B5)");

        var results = FindReplaceService.Find(wb, "SUM(*)", searchFormulas: true);

        results.Should().ContainSingle().Which.Address.Should().Be(a1);
    }
}
