using System.Globalization;
using System.Threading;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R21-text-functions-deep-2: LENB/LEFTB/RIGHTB/MIDB/REPLACEB/FINDB/SEARCHB previously classified every
/// codepoint above U+00FF as a 2-byte DBCS character, over-counting non-CJK scripts (Cyrillic, Greek, Hebrew,
/// Arabic, ...) that are single-byte in every real DBCS codepage (Shift-JIS/GBK/Big5/EUC-KR). Real Excel
/// returns the same byte count as LEN for such text. CJK ideographs/kana/hangul/fullwidth forms must still
/// count as 2 bytes -- but (R60-formula-text-clean-6-1) only when the running culture is itself a DBCS
/// language (ja/zh/ko); under any other culture (e.g. en-US) the *B functions behave exactly like LEN,
/// regardless of the string's content, so CJK-width assertions below run under a forced ja-JP culture.
/// </summary>
public sealed class R21_TextCore_DbcsByteWidth_NonCjkSingleByteTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=LENB(\"Привет\")", 6)] // Cyrillic "Привет" (6 chars)
    [InlineData("=LENB(\"αβγ\")", 3)] // Greek "αβγ"
    [InlineData("=LENB(\"שלום\")", 4)] // Hebrew "שלום"
    [InlineData("=LENB(\"مرحبا\")", 5)] // Arabic "مرحبا"
    [InlineData("=LENB(\"ĀāĂ\")", 3)] // Latin Extended-A
    public void LenB_TreatsSingleByteScriptsAsOneBytePerCharacter(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void LenB_StillCountsCjkIdeographsAsTwoBytes()
    {
        // Mixed text: 'A' (1 byte) + CJK ideograph U+754C (2 bytes) + Cyrillic 'Б' (1 byte) = 4 bytes total,
        // not 6 (which the pre-fix code returned by treating the Cyrillic char as 2 bytes as well). DBCS
        // width doubling only applies under a DBCS-language culture (R60-formula-text-clean-6-1), so force
        // ja-JP here to exercise it.
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
            _eval.Evaluate("=LENB(\"A界Б\")", Sheet()).Should().Be(new NumberValue(4));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void LeftBRightBMidB_SliceCyrillicTextByteForByte()
    {
        var sheet = Sheet();
        _eval.Evaluate("=LEFTB(\"Привет\",3)", sheet)
            .Should().Be(new TextValue("При"));
        _eval.Evaluate("=RIGHTB(\"Привет\",3)", sheet)
            .Should().Be(new TextValue("вет"));
        _eval.Evaluate("=MIDB(\"Привет\",2,2)", sheet)
            .Should().Be(new TextValue("ри"));
    }

    [Fact]
    public void ReplaceB_ReplacesCyrillicByteRangeWithoutOverCounting()
    {
        // Bytes 2-3 (1-based) of "Привет" are 'р','и'; replacing them with "X" should yield "ПXвет".
        _eval.Evaluate("=REPLACEB(\"Привет\",2,2,\"X\")", Sheet())
            .Should().Be(new TextValue("ПXвет"));
    }

    [Fact]
    public void FindBSearchB_ReturnByteOffsetsMatchingCharacterOffsetsForCyrillicText()
    {
        // In real Excel FINDB/SEARCHB on non-DBCS text returns the same position as FIND/SEARCH
        // because each character is 1 byte.
        _eval.Evaluate("=FINDB(\"в\",\"Привет\")", Sheet())
            .Should().Be(new NumberValue(4));
        _eval.Evaluate("=SEARCHB(\"в\",\"Привет\")", Sheet())
            .Should().Be(new NumberValue(4));
    }

    private static Sheet Sheet(params (int Row, int Col, ScalarValue Value)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);
        return sheet;
    }
}
