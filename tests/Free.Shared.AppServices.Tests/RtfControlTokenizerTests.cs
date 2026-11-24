using System.Text;
using System.Text.RegularExpressions;
using Free.Shared.IO;

namespace Free.Shared.AppServices.Tests;

public sealed class RtfControlTokenizerTests
{
    private static RtfControlWord Lex(string rtf, int position = 1)
    {
        RtfControlTokenizer.TryReadControlWord<byte>(Encoding.Latin1.GetBytes(rtf), position, out var word).Should().BeTrue();
        return word;
    }

    [Fact]
    public void ReadsLettersParameterAndConsumesOneDelimiterSpace()
    {
        var word = Lex(@"\fs24  x");

        word.NameStart.Should().Be(1);
        word.NameLength.Should().Be(2);
        word.Parameter.Should().Be(24);
        word.ParameterOutOfRange.Should().BeFalse();
        word.End.Should().Be(6, "only ONE space is the delimiter; the second is text");
    }

    [Fact]
    public void ReadsNegativeParameters()
    {
        Lex(@"\li-720 x").Parameter.Should().Be(-720);
        Lex(@"\u-4064?").Parameter.Should().Be(-4064);
    }

    [Fact]
    public void BareMinusIsNoParameterNotZero()
    {
        var word = Lex(@"\b-x");

        word.Parameter.Should().BeNull();
        word.ParameterOutOfRange.Should().BeFalse();
        word.End.Should().Be(3, "the sign character belongs to the control word token");
    }

    [Fact]
    public void NoParameterWhenNeitherSignNorDigitFollows()
    {
        Lex(@"\par\par").Parameter.Should().BeNull();
        Lex(@"\par\par").End.Should().Be(4);
    }

    [Fact]
    public void NonAsciiLettersEndTheControlWordForBytesAndChars()
    {
        var bytes = RtfControlTokenizer.TryReadControlWord<byte>([(byte)'\\', (byte)'a', 0xE9, (byte)'b'], 1, out var byteWord);
        var chars = RtfControlTokenizer.TryReadControlWord("\\a\u00E9b".AsSpan(), 1, out var charWord);
        var wide = RtfControlTokenizer.TryReadControlWord("\\a\u0141b".AsSpan(), 1, out var wideWord);

        bytes.Should().BeTrue();
        chars.Should().BeTrue();
        wide.Should().BeTrue();
        byteWord.NameLength.Should().Be(1);
        charWord.NameLength.Should().Be(1);
        wideWord.NameLength.Should().Be(1, "U+0141 truncates to 'A' if the char is narrowed; it must not be a letter");
    }

    [Fact]
    public void RejectsPositionsThatAreNotAnAsciiLetter()
    {
        RtfControlTokenizer.TryReadControlWord("\\'41"u8, 1, out _).Should().BeFalse();
        RtfControlTokenizer.TryReadControlWord("\\"u8, 1, out _).Should().BeFalse();
        RtfControlTokenizer.TryReadControlWord("\\@x"u8, 1, out _).Should().BeFalse();
        RtfControlTokenizer.TryReadControlWord("\\[x"u8, 1, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"\x2147483647", 2147483647, false)]
    [InlineData(@"\x2147483648", int.MaxValue, true)]
    [InlineData(@"\x99999999999999999999", int.MaxValue, true)]
    [InlineData(@"\x-2147483648", int.MinValue, false)]
    [InlineData(@"\x-2147483649", int.MinValue, true)]
    [InlineData(@"\x-99999999999999999999", int.MinValue, true)]
    public void ClampsOutOfRangeParametersAndFlagsThem(string rtf, int expected, bool outOfRange)
    {
        var word = Lex(rtf);

        word.Parameter.Should().Be(expected);
        word.ParameterOutOfRange.Should().Be(outOfRange);
        word.End.Should().Be(rtf.Length, "every digit is consumed even when the value is clamped");
    }

    [Fact]
    public void HexEscapeNeedsTwoHexDigitsAndAlwaysConsumesTwo()
    {
        RtfControlTokenizer.TryReadHexEscape("\\'e9x"u8, 2, out var value, out var end).Should().BeTrue();
        value.Should().Be(0xE9);
        end.Should().Be(4);

        RtfControlTokenizer.TryReadHexEscape("\\' 4x"u8, 2, out _, out end).Should().BeFalse();
        end.Should().Be(4);

        RtfControlTokenizer.TryReadHexEscape("\\'4"u8, 2, out _, out end).Should().BeFalse();
        end.Should().Be(3);
    }

    [Theory]
    [InlineData(65, 'A')]
    [InlineData(-4064, '\uF020')]
    [InlineData(32767, '\u7FFF')]
    [InlineData(-32768, '\u8000')]
    [InlineData(65536 + 66, 'B')]
    public void UnicodeParameterIsASignedSixteenBitValue(int parameter, char expected) =>
        RtfControlTokenizer.ToUtf16CodeUnit(parameter).Should().Be(expected);

    [Theory]
    [InlineData(@"?x", 0, 1)]
    [InlineData(@"\'e9x", 0, 4)]
    [InlineData(@"\\x", 0, 2)]
    [InlineData(@"\bullet 5", 0, 8)]
    [InlineData(@"\b-5x", 0, 4)]
    [InlineData(@"{x", 0, 0)]
    [InlineData(@"}x", 0, 0)]
    [InlineData(@"", 0, 0)]
    [InlineData(@"\", 0, 1)]
    public void FallbackUnitsAreOneCharEscapeSymbolOrWholeControlWord(string rtf, int start, int expectedEnd) =>
        RtfControlTokenizer.SkipFallbackUnit(rtf.AsSpan(), start).Should().Be(expectedEnd);

    /// <summary>
    /// Guard: control-word scanning lives once in <see cref="RtfControlTokenizer"/>. The FreeW and FreeP
    /// RTF readers keep their own group/destination handling but must lex through the shared tokenizer
    /// instead of re-implementing letter/digit/delimiter scanning.
    /// </summary>
    [Fact]
    public void RtfReadersLexControlWordsOnlyThroughTheSharedTokenizer()
    {
        var root = TestWorkspaceFileLocator.FindContainingDirectory("FreeX.slnx");
        string[] readers =
        [
            "freew/FreeW.Core.IO/RtfReader.cs",
            "freep/FreeP.App.Presentation/ExternalRichTextClipboardPlanner.cs",
        ];

        Regex[] forbidden =
        [
            new(@"char\.IsLetter\(", RegexOptions.Compiled),
            new(@"char\.IsDigit\(", RegexOptions.Compiled),
            new(@"(?<!RtfControlTokenizer\.)\bIsAsciiLetter\(", RegexOptions.Compiled),
            new(@"(?<!RtfControlTokenizer\.)\bIsAsciiDigit\(", RegexOptions.Compiled),
            new(@"\bParseControlWordParameter\b", RegexOptions.Compiled),
            new(@"NumberStyles\.HexNumber", RegexOptions.Compiled),
        ];

        foreach (var relative in readers)
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
            text = Regex.Replace(text, @"//.*$", "", RegexOptions.Multiline);

            text.Should().Contain("RtfControlTokenizer.TryReadControlWord", $"{relative} must lex through the shared tokenizer");
            foreach (var pattern in forbidden)
                pattern.IsMatch(text).Should().BeFalse($"{relative} re-implements RTF scanning matching {pattern}");
        }
    }
}
