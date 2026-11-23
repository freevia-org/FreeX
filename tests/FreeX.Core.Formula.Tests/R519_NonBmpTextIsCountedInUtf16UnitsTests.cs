using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Non-BMP text (an emoji such as U+1F600, two UTF-16 units) is counted in CODE POINTS by LEN, LEFT, RIGHT, MID,
/// FIND, SEARCH and REPLACE. Round 519 pinned the opposite (UTF-16 units, LEFT splitting the pair) from the
/// general belief about older Excel builds; measured against the installed Excel 16.0 (build 20625) through COM,
/// LEN("A" &amp; emoji &amp; "B") is 3, MID(...,2,1) is the whole emoji, UNICODE(MID(...)) is 128512 and
/// LEN(UNICHAR(128512)) is 1 -- no function ever returns half of a pair. The 32767-character cell limit is still
/// counted in UTF-16 units (REPT(emoji,16384) is #VALUE! although LEN would say 16384). The byte-oriented
/// LENB/MIDB/FINDB family is unchanged.
/// </summary>
public partial class FunctionLibraryTests
{
    private const string Grinning = "\U0001F600"; // U+1F600, two UTF-16 units, ONE Excel character
    private static readonly string Mixed = "A" + Grinning + "B";

    [Fact]
    public void Len_CountsCodePointsLikeExcel()
    {
        var sheet = MakeSheet((1, 1, new TextValue(Mixed)));

        _eval.Evaluate("=LEN(A1)", sheet).Should().Be(new NumberValue(3));
        _eval.Evaluate("=LEN(UNICHAR(128512))", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Mid_AddressesTheSurrogatePairAsOneCharacter()
    {
        var sheet = MakeSheet((1, 1, new TextValue(Mixed)));

        _eval.Evaluate("=MID(A1,2,1)", sheet).Should().Be(new TextValue(Grinning));
        _eval.Evaluate("=MID(A1,3,1)", sheet).Should().Be(new TextValue("B"));
    }

    [Fact]
    public void Left_KeepsASurrogatePairTogether()
    {
        var sheet = MakeSheet((1, 1, new TextValue(Mixed)));

        _eval.Evaluate("=LEFT(A1,2)", sheet).Should().Be(new TextValue("A" + Grinning));
    }

    [Fact]
    public void Right_CountsFromTheEndInCodePoints()
    {
        var sheet = MakeSheet((1, 1, new TextValue(Mixed)));

        _eval.Evaluate("=RIGHT(A1,1)", sheet).Should().Be(new TextValue("B"));
        _eval.Evaluate("=RIGHT(A1,2)", sheet).Should().Be(new TextValue(Grinning + "B"));
    }
}
