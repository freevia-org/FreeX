using System.Globalization;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for round 39's external-clipboard bucket:
/// - R39-io-external-clipboard-2-1: a pasted plain-text field starting with '=' must become a
///   live formula (like real Excel and FreeX's own typed cell entry), not literal text.
/// - R39-io-external-clipboard-2-2: a pasted number using the CURRENT CULTURE's own thousands
///   grouping (e.g. de-DE "1.234,56" -> 1234.56) must be recognized as a number, not misread as
///   text (or worse, as a date candidate).
/// </summary>
public sealed class R39_ExternalClipboardFormulaAndLocaleGroupingTests
{
    /// <summary>Pins CurrentCulture for the duration of a test so locale-dependent parsing assertions
    /// are deterministic regardless of the host machine's ambient locale.</summary>
    private sealed class CurrentCultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CurrentCultureScope(string cultureName)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    // ── R39-io-external-clipboard-2-1: leading '=' becomes a formula ────────────────────────

    [Fact]
    public void ExternalTextPaste_LeadingEqualsBecomesALiveFormulaNotLiteralText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [["=1+1"]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var pasted = sheet.GetCell(address);
        pasted.Should().NotBeNull();
        pasted!.FormulaText.Should().Be("1+1");
    }

    [Fact]
    public void ExternalTextPaste_LeadingEqualsInMultiCellPasteBecomesFormulaAlongsideValues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var origin = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id, origin, [["=SUM(A1:A2)", "42"]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetCell(origin)!.FormulaText.Should().Be("SUM(A1:A2)");
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(42));
    }

    // Sibling no-regression: Excel's leading-apostrophe text escape still wins over the leading
    // '=' formula check, so "'=1+1" stays the literal text "=1+1" (never a formula).
    [Fact]
    public void ExternalTextPaste_LeadingApostropheBeforeEqualsStillForcesLiteralTextNotFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [["'=1+1"]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var pasted = sheet.GetCell(address);
        pasted!.FormulaText.Should().BeNull();
        sheet.GetValue(address).Should().Be(new TextValue("=1+1"));
    }

    // Sibling no-regression: a destination pre-formatted as Text (@) keeps a pasted "=1+1" as a
    // literal string exactly like it already does for numeric-looking input, never becoming a formula.
    [Fact]
    public void ExternalTextPaste_LeadingEqualsIntoTextFormattedDestinationStaysLiteralText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetStyleOnly(address.Row, address.Col, wb.RegisterStyle(new CellStyle { NumberFormat = "@" }));

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [["=1+1"]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetCell(address)!.FormulaText.Should().BeNull();
        sheet.GetValue(address).Should().Be(new TextValue("=1+1"));
    }

    // ── R39-io-external-clipboard-2-2: locale thousands grouping ────────────────────────────

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1.234", 1234)]
    [InlineData("-1.234.567,5", -1234567.5)]
    public void ExternalTextPaste_DeDeCulture_CoercesDotGroupedCommaDecimalNumbers(string text, double expected)
    {
        using var _ = new CurrentCultureScope("de-DE");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[text]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(expected));
    }

    // Sibling no-regression: a malformed de-DE grouping is still rejected as a number (and, since it
    // no longer parses as a number, must NOT be misread as a date candidate either -- it stays text).
    [Theory]
    [InlineData("1.23,4")]
    public void ExternalTextPaste_DeDeCulture_RejectsMalformedGroupingAsTextNotDate(string text)
    {
        using var _ = new CurrentCultureScope("de-DE");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[text]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new TextValue(text));
    }

    // Sibling no-regression: en-US grouping (comma thousands, dot decimal) keeps working exactly as
    // before, unaffected by adding de-DE-style locale-aware grouping support.
    [Theory]
    [InlineData("1,234", 1234)]
    [InlineData("1,234.56", 1234.56)]
    public void ExternalTextPaste_EnUsCulture_StillCoercesCommaGroupedNumbers(string text, double expected)
    {
        using var _ = new CurrentCultureScope("en-US");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[text]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(expected));
    }
}
