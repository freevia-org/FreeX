namespace FreeW.Core.Model.Tests;

/// <summary>
/// r559: Word table formulas (<c>=SUM(ABOVE)</c> and friends) read their operands out of the
/// document's own table cells, so every number they parse is file-controlled.
///
/// <para><see cref="TableFormulaEvaluator.TryParseCellNumber"/> strips everything that is not a
/// digit, sign or point before parsing, which happens to make the literal "NaN" and "Infinity"
/// unreachable -- they reduce to an empty string and are rejected. That is worth recording,
/// because it means the usual spellings do NOT apply here. What survives the strip is a long run
/// of DIGITS, which overflows to Infinity on parse exactly as r550 found in ZoomPercentPolicy,
/// where NumberStyles.Number forbade an exponent and a long paste overflowed anyway.</para>
///
/// <para>An infinite operand propagates through the sum into the field result the document
/// displays. The remedy is the method's own contract: it already answers false for text it cannot
/// read as a number.</para>
/// </summary>
public class R559_TableFormulaCellNumbersAreFiniteTests
{
    [Fact]
    public void A_run_of_digits_too_long_to_represent_is_not_a_number()
    {
        var huge = new string('9', 400);

        TableFormulaEvaluator.TryParseCellNumber(huge, out var value)
            .Should().BeFalse("400 digits overflow to " + value + ", which is not a usable operand");
    }

    [Fact]
    public void The_usual_spellings_cannot_reach_this_parser_at_all()
    {
        // Recorded rather than assumed: the digit-strip removes every letter, so these reduce to
        // an empty string and are already rejected. A later round should not "fix" them here.
        TableFormulaEvaluator.TryParseCellNumber("NaN", out _).Should().BeFalse();
        TableFormulaEvaluator.TryParseCellNumber("Infinity", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("42", 42d)]
    [InlineData("-17.5", -17.5d)]
    [InlineData("$1,234.50", 1234.50d)]
    [InlineData("12%", 12d)]
    public void Ordinary_cell_text_still_parses(string text, double expected)
    {
        // Non-vacuity: the currency and percent cases pin the deliberate strip-then-parse
        // behaviour, so a guard that rejected anything unusual would fail here rather than
        // quietly making every table formula evaluate to zero.
        TableFormulaEvaluator.TryParseCellNumber(text, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    [Fact]
    public void A_sum_over_an_unrepresentable_cell_stays_finite()
    {
        // End to end through the evaluator: the operand is skipped the way any unreadable cell
        // already is, rather than turning the whole field result into infinity.
        var table = new Table();
        foreach (var text in new[] { "10", new string('9', 400), "20" })
        {
            var row = new TableRow();
            var cell = new TableCell();
            cell.Paragraphs.Add(new Paragraph(text));
            row.Cells.Add(cell);
            table.Rows.Add(row);
        }

        var resultRow = new TableRow();
        resultRow.Cells.Add(new TableCell());
        table.Rows.Add(resultRow);

        var result = TableFormulaEvaluator.EvaluateValue(table, 3, 0, "SUM(ABOVE)");

        double.IsFinite(result).Should().BeTrue("the sum was " + result);
    }
}
