using System.Globalization;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R28-clipboard-external-formats-deep-2/3: external (non-FreeX) clipboard plain-text paste had two
/// gaps versus both real Excel and FreeX's own typed-entry path (CellEntryParser):
///   (2) a pasted date or percent literal (e.g. "6/15/2026", "45%") never got recognized and landed
///       as literal text instead of a date/number, unlike typing the same text into the cell.
///   (3) paste never consulted the destination cell's existing Text (@) number format, so pasting a
///       leading-zero string (e.g. "00501") into a column pre-formatted as Text still got numerically
///       coerced to 501, silently losing the leading zeros the user protected via the Text format.
/// PasteCommandFactory.ParseClipboardValue now recognizes percent/date literals, and the plain
/// external-paste path (ExternalTextPasteValuesCommand) consults the destination's effective
/// NumberFormat before coercing, while leaving every other existing coercion (numeric,
/// thousands/parenthesized, TRUE/FALSE, apostrophe-escape, preserveText) untouched.
/// </summary>
public sealed class R28_ExternalClipboardPasteFormatAwareTests
{
    /// <summary>Pins CurrentCulture to en-US for the duration of a test, matching the sibling
    /// PasteCommandFactoryClipboardTextTests convention so date/number assertions are deterministic
    /// regardless of the host machine's ambient locale.</summary>
    private sealed class CurrentCultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CurrentCultureScope(string cultureName)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    // ── R28-clipboard-external-formats-deep-2: date/percent recognition on paste ────────────

    [Fact]
    public void ExternalTextPaste_RecognizesDateLiteralLikeTypedEntry()
    {
        using var _ = new CurrentCultureScope("en-US");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [["6/15/2026"]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var value = sheet.GetValue(address);
        value.Should().BeOfType<DateTimeValue>();
        ((DateTimeValue)value!).ToDateTime().Should().Be(new DateTime(2026, 6, 15));
    }

    [Fact]
    public void ExternalTextPaste_RecognizesPercentLiteralLikeTypedEntry()
    {
        using var _ = new CurrentCultureScope("en-US");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [["45%"]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(0.45));
    }

    // Sibling already-working cases: plain numbers, thousands/parenthesized negatives, TRUE/FALSE,
    // and non-date/non-percent-looking text must keep working exactly as before this fix.
    [Theory]
    [InlineData("2.5", 2.5)]
    [InlineData("(1,234.56)", -1234.56)]
    public void ExternalTextPaste_StillCoercesPlainAndExcelStyleNumbers(string text, double expected)
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

    [Fact]
    public void ExternalTextPaste_StillKeepsOrdinaryTextAsTextNotMisreadAsDate()
    {
        using var _ = new CurrentCultureScope("en-US");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        // "West" has no digits at all, and "1,234,5" (malformed thousands grouping) has no
        // recognized date separator -- neither should ever be misread as a date.
        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [["West"]]);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(address).Should().Be(new TextValue("West"));

        var address2 = new CellAddress(sheet.Id, 1, 2);
        var command2 = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address2, [["1,234,5"]]);
        command2.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(address2).Should().Be(new TextValue("1,234,5"));
    }

    // ── R28-clipboard-external-formats-deep-3: destination Text (@) format is honored ───────

    [Fact]
    public void ExternalTextPaste_IntoExistingTextFormattedCell_KeepsLeadingZerosAsText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var textStyleId = wb.RegisterStyle(new CellStyle { NumberFormat = "@" });
        sheet.SetCell(address, new Cell { Value = new TextValue("00000"), StyleId = textStyleId });

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [["00501"]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new TextValue("00501"));
        // The destination's Text format itself must also survive the edit (an already-populated
        // cell's style is preserved by EditCellsCommand regardless of the pasted value).
        sheet.GetCell(address)!.StyleId.Should().Be(textStyleId);

        command.Revert(ctx);

        sheet.GetValue(address).Should().Be(new TextValue("00000"));
        sheet.GetCell(address)!.StyleId.Should().Be(textStyleId);
    }

    [Fact]
    public void ExternalTextPaste_IntoStyleOnlyTextFormattedBlankCell_KeepsLeadingZerosAsText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        // The common real-world case: format an empty column as Text (no cell content yet, just a
        // style-only override) before pasting in zip codes/IDs with leading zeros.
        var textStyleId = wb.RegisterStyle(new CellStyle { NumberFormat = "@" });
        sheet.SetStyleOnly(address.Row, address.Col, textStyleId);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [["00501"]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new TextValue("00501"));
    }

    // Sibling already-working case: pasting the same leading-zero field into a NOT-Text-formatted
    // destination must keep coercing it to a number exactly as before this fix (no over-correction).
    [Fact]
    public void ExternalTextPaste_IntoGeneralFormattedCell_StillCoercesLeadingZeroFieldToNumber()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [["00501"]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(501));
    }

    [Fact]
    public void ExternalTextPaste_PreserveTextOptionStillForcesTextRegardlessOfDestinationFormat()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            address,
            [["123"]],
            preserveText: true);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new TextValue("123"));
    }
}
