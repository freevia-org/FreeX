using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// r519: Excel's text functions count UTF-16 CODE UNITS, not characters a reader would recognise.
/// A non-BMP character such as U+1F600 occupies two units, so in Excel LEN("A&#128512;B") is 4, MID at
/// position 2 for length 2 returns the whole emoji, and LEFT(...,2) deliberately cuts it in half and
/// yields a lone surrogate. FreeX matches that exactly, and these tests pin it.
///
/// <para>They exist because the formula text layer carried four private helpers that walked text by
/// SURROGATE PAIR rather than by code unit -- an alternative, friendlier semantics that Excel does
/// not implement. All four were dead, but dead code encoding the wrong rule is worse than neutral
/// cruft: it reads as the intended design, so a later reader could "fix" emoji handling by wiring
/// them in and silently break compatibility. The helpers are removed and this is the guard that makes
/// such a change fail loudly instead of shipping.</para>
/// </summary>
public partial class FunctionLibraryTests
{
    private const string Grinning = "😀"; // U+1F600, two UTF-16 units
    private static readonly string Mixed = "A" + Grinning + "B";

    [Fact]
    public void Len_CountsUtf16UnitsLikeExcel()
    {
        var sheet = MakeSheet((1, 1, new TextValue(Mixed)));

        // Four units: 'A', high surrogate, low surrogate, 'B'. A text-element count would say 3.
        _eval.Evaluate("=LEN(A1)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Mid_AddressesTheSurrogatePairByItsUnitPositions()
    {
        var sheet = MakeSheet((1, 1, new TextValue(Mixed)));

        _eval.Evaluate("=MID(A1,2,2)", sheet).Should().Be(new TextValue(Grinning));
    }

    [Fact]
    public void Left_SplitsASurrogatePairExactlyAsExcelDoes()
    {
        var sheet = MakeSheet((1, 1, new TextValue(Mixed)));

        // Excel does NOT keep the pair together here; it returns 'A' plus the lone high surrogate.
        // Pinning the broken-looking result is the point: it is the compatible one.
        var result = _eval.Evaluate("=LEFT(A1,2)", sheet);

        result.Should().BeOfType<TextValue>();
        ((TextValue)result).Value.Should().Be("A\uD83D");
    }

    [Fact]
    public void Right_CountsFromTheEndInUnitsToo()
    {
        var sheet = MakeSheet((1, 1, new TextValue(Mixed)));

        _eval.Evaluate("=RIGHT(A1,1)", sheet).Should().Be(new TextValue("B"));
        _eval.Evaluate("=RIGHT(A1,3)", sheet).Should().Be(new TextValue(Grinning + "B"));
    }
}
