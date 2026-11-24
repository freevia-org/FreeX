using System.IO;
using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// FreeW reads control words through the shared <c>Free.Shared.IO.RtfControlTokenizer</c>, so it follows the
/// RTF specification the same way FreeP does: ASCII-letter control words only, a bare <c>-</c> is no
/// parameter, out-of-range parameters are clamped, hex escapes are exactly two hex digits.
/// </summary>
public class RtfSharedTokenizerBehaviorTests
{
    private static string Text(string rtf)
    {
        using var stream = new MemoryStream(Encoding.Latin1.GetBytes(rtf));
        return RtfReader.Read(stream).PlainText;
    }

    [Fact]
    public void NonAsciiLetterEndsTheControlWord()
    {
        // Latin-1 0xE9 is a letter to char.IsLetter but never part of a control word: \par then literal text.
        Text("{\\rtf1\\ansi a\\paréb}").Should().Contain("éb");
    }

    [Fact]
    public void BareMinusAfterAControlWordIsNoParameter()
    {
        // \ucN with a bare '-' must not become \uc0: the fallback char after \u is still skipped (default \uc1).
        Text("{\\rtf1\\ansi \\u233 ?x}").Should().Be("éx");
        Text("{\\rtf1\\ansi \\uc-\\u233 ?x}").Should().Be("éx");
    }

    [Fact]
    public void OutOfRangeParameterIsClampedAndTheTextStillReads()
    {
        Text("{\\rtf1\\ansi\\li99999999999999999999 hello}").Should().Contain("hello");
        Text("{\\rtf1\\ansi\\bin99999999999 hello}").Should().NotBeNull();
    }

    [Fact]
    public void UnicodeParameterIsASigned16BitValue()
    {
        Text("{\\rtf1\\ansi \\u-4064 ?}").Should().Be("");
    }

    [Fact]
    public void FallbackSkipCountsAControlWordAsOneUnit()
    {
        Text("{\\rtf1\\ansi \\uc2\\u8364\\bullet 5Z}").Should().Be("€Z");
    }

    [Fact]
    public void HexEscapeNeedsTwoHexDigits()
    {
        // " 4" used to parse as 0x04 through NumberStyles.HexNumber's whitespace allowance.
        Text("{\\rtf1\\ansi a\\' 4b}").Should().Be("ab");
    }
}
