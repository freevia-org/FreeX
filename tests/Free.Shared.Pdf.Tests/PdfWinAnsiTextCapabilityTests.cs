using FluentAssertions;
using Free.Shared.Pdf;

namespace Free.Shared.Pdf.Tests;

/// <summary>
/// Unit tests for <see cref="PdfWinAnsiTextCapability.TryEncodeWinAnsiByte"/>.
/// </summary>
public sealed class PdfWinAnsiTextCapabilityTests
{
    // ── ASCII printable range ─────────────────────────────────────────────────

    [Theory]
    [InlineData(' ', 0x20)]
    [InlineData('A', 0x41)]
    [InlineData('~', 0x7E)]
    public void TryEncodeWinAnsiByte_AsciiPrintable_EncodesDirectly(char ch, byte expected)
    {
        PdfWinAnsiTextCapability.TryEncodeWinAnsiByte(ch, out byte actual).Should().BeTrue();
        actual.Should().Be(expected);
    }

    // ── Latin-1 Supplement (U+00A0–U+00FF) ───────────────────────────────────

    [Fact]
    public void TryEncodeWinAnsiByte_NoBreakSpace_EncodesAs0xA0()
    {
        PdfWinAnsiTextCapability.TryEncodeWinAnsiByte((char)0x00A0, out byte value).Should().BeTrue();
        value.Should().Be(0xA0);
    }

    [Fact]
    public void TryEncodeWinAnsiByte_EAcute_EncodesAs0xE9()
    {
        PdfWinAnsiTextCapability.TryEncodeWinAnsiByte((char)0x00E9, out byte value).Should().BeTrue();
        value.Should().Be(0xE9);
    }

    [Fact]
    public void TryEncodeWinAnsiByte_LatinSmallLetterYWithDiaeresis_EncodesAs0xFF()
    {
        PdfWinAnsiTextCapability.TryEncodeWinAnsiByte((char)0x00FF, out byte value).Should().BeTrue();
        value.Should().Be(0xFF);
    }

    // ── CP1252 extension characters ───────────────────────────────────────────

    [Fact]
    public void TryEncodeWinAnsiByte_Euro_EncodedAs0x80()
    {
        // U+20AC EURO SIGN maps to WinAnsi byte 0x80 via the explicit switch.
        PdfWinAnsiTextCapability.TryEncodeWinAnsiByte((char)0x20AC, out byte value).Should().BeTrue();
        value.Should().Be(0x80);
    }

    // ── F6 regression: C1 control chars must NOT encode as raw bytes ──────────
    // The second range check in TryEncodeWinAnsiByte covers U+00A0–U+00FF (direct cast to
    // byte).  C1 control characters U+0080–U+009F are NOT in that range and must fall
    // through to the switch, which correctly rejects them (they are not valid WinAnsi
    // code points — byte positions 0x80–0x9F in CP1252 belong to printable characters
    // like the Euro sign, not to their Unicode C1 counterparts).

    [Fact]
    public void TryEncodeWinAnsiByte_U0080_IsNotEncodedAs0x80()
    {
        // Key regression: U+0080 (C1 PAD control) must not produce byte 0x80.
        // Byte 0x80 in WinAnsi is the Euro sign (U+20AC), not the C1 PAD control.
        PdfWinAnsiTextCapability.TryEncodeWinAnsiByte((char)0x0080, out byte value).Should().BeFalse(
            "U+0080 is a C1 control character; it has no direct WinAnsi encoding (0x80 = Euro sign)");
        value.Should().Be(0, "out value must be 0 when encoding fails");
    }

    [Theory]
    [InlineData((char)0x0080)] // PAD
    [InlineData((char)0x0085)] // NEXT LINE
    [InlineData((char)0x009F)] // APPLICATION PROGRAM COMMAND
    public void TryEncodeWinAnsiByte_C1ControlChars_AreNotEncodable(char ch)
    {
        // C1 control characters (U+0080–U+009F) have no WinAnsi encoding.
        PdfWinAnsiTextCapability.TryEncodeWinAnsiByte(ch, out _).Should().BeFalse(
            $"U+{(int)ch:X4} is a C1 control character with no WinAnsi mapping");
    }

    // ── Characters outside the WinAnsi repertoire ─────────────────────────────

    [Theory]
    [InlineData((char)0x0100)] // Latin Extended-A (Ā) — not in WinAnsi
    [InlineData((char)0x0400)] // Cyrillic (Ѐ) — not in WinAnsi
    public void TryEncodeWinAnsiByte_OutsideWinAnsiRepertoire_IsNotEncodable(char ch)
    {
        PdfWinAnsiTextCapability.TryEncodeWinAnsiByte(ch, out _).Should().BeFalse(
            $"U+{(int)ch:X4} is outside the WinAnsi character repertoire");
    }
}
