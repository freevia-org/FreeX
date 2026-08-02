using System.Globalization;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R114: a Text-formatted cell (RawValue is TextValue, e.g. NumberFormat "@") whose DisplayText looks
/// like a percentage ("45%") or a date ("3/4", "12/25") must round-trip through the external OS
/// clipboard unchanged -- exactly like a Text-formatted "00501" already does for plain-number
/// coercion. Before this fix, ClipboardSerializer.RequiresLeadingApostropheEscape only mirrored
/// ParseClipboardValue's plain-number/boolean coercion branches, not its percent (TryParsePastePercent)
/// or date (TryParsePasteDate) branches, so the leading-apostrophe protection was omitted for exactly
/// the values that would be coerced into a NumberValue/DateTimeValue on the paste side.
///
/// The real product entry points are exercised on both ends of the round trip: ClipboardSerializer.
/// Serialize (the Ctrl+C write side) and PasteCommandFactory.CreateExternalTextPasteCommand
/// (the external-clipboard paste side, e.g. a second FreeX process/window pasting from the OS
/// clipboard with no internal paste-buffer available) -- never a hand-built ParseClipboardValue call
/// in isolation.
/// </summary>
public sealed class R114_ClipboardTextPercentDateEscapeTests
{
    /// <summary>Pins CurrentCulture to en-US for the duration of a test so percent/date-candidate
    /// parsing is deterministic regardless of the host machine's ambient locale.</summary>
    private sealed class CurrentCultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CurrentCultureScope(string cultureName)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    [Theory]
    [InlineData("45%")]
    [InlineData("3/4")]
    [InlineData("12/25")]
    [InlineData("6/15/2026")]
    public void CopyThenExternalPaste_TextFormattedPercentOrDateLookingContent_RoundTripsAsText(string displayText)
    {
        using var _ = new CurrentCultureScope("en-US");

        // --- Write side: copy a Text-formatted cell (RawValue TextValue) whose DisplayText looks
        // like a percent/date. This is exactly what a NumberFormat "@" cell serializes as.
        var sourceSheetId = SheetId.New();
        var viewport = new ViewportModel(
            [new DisplayCell(1, 1, new TextValue(displayText), displayText, null, StyleId.Default, null)],
            [],
            []);
        var range = new GridRange(new CellAddress(sourceSheetId, 1, 1), new CellAddress(sourceSheetId, 1, 1));

        var clipboardText = ClipboardSerializer.Serialize(viewport, range);

        // Must carry the protective leading apostrophe -- otherwise the paste side below re-coerces it.
        clipboardText.Should().Be("'" + displayText,
            "a Text-typed cell whose display text looks like a percent/date must be escaped before " +
            "it hits the OS clipboard, exactly like a Text-typed \"00501\" already is");

        // --- Paste side: a second FreeX process/window pastes the surviving OS-clipboard text via the
        // external-clipboard fallback (no internal paste buffer available across processes).
        var wb = new Workbook("test");
        var destSheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var destination = new CellAddress(destSheet.Id, 1, 1);

        var pasteRows = ClipboardSerializer.Deserialize(clipboardText);
        var command = PasteCommandFactory.CreateExternalTextPasteCommand(destSheet.Id, destination, pasteRows);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        destSheet.GetValue(destination).Should().Be(new TextValue(displayText),
            "the destination cell must preserve the original Text value/type, not be coerced into a " +
            "NumberValue (percent) or DateTimeValue (date) by the paste-side parser");
    }

    /// <summary>No-regression sibling: the pre-existing plain-number/boolean escape behavior (the only
    /// branches RequiresLeadingApostropheEscape covered before this fix) must keep working exactly as
    /// before -- a Text-formatted cell containing a plain numeric-looking string or TRUE/FALSE still
    /// gets escaped, and a genuinely non-coercible Text string (no leading apostrophe needed) is left
    /// untouched byte-for-byte.</summary>
    [Theory]
    [InlineData("00501", "'00501")]
    [InlineData("1234", "'1234")]
    [InlineData("TRUE", "'TRUE")]
    [InlineData("FALSE", "'FALSE")]
    [InlineData("hello world", "hello world")]
    [InlineData("Q4 report", "Q4 report")]
    public void CopyTextFormattedCell_PlainNumberBooleanAndOrdinaryText_EscapeDecisionUnchanged(
        string displayText, string expectedClipboardText)
    {
        using var _ = new CurrentCultureScope("en-US");

        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [new DisplayCell(1, 1, new TextValue(displayText), displayText, null, StyleId.Default, null)],
            [],
            []);
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));

        var clipboardText = ClipboardSerializer.Serialize(viewport, range);

        clipboardText.Should().Be(expectedClipboardText);
    }
}
