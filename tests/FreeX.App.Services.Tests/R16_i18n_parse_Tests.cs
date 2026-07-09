using System.Globalization;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round 16 i18n parsing fix: typed cell entry of a comma-decimal-locale grouped integer (e.g.
/// de-DE "1.234" meaning 1234, '.' as thousands separator) must not silently become the decimal
/// 1.234. Covers finding R16-rtl-i18n-parsing-2.
/// </summary>
public sealed class R16_i18n_parse_Tests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    [Fact]
    public void CreateCell_DeDEGroupedIntegerEntry_ParsesAsTwelveThirtyFour_NotOnePointTwoThreeFour()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");

        // Before the fix, the Float parse lacked AllowThousands, so "." fell through to the
        // invariant fallback and was silently read as a decimal point (1.234) instead of the
        // user's intended grouped integer 1234.
        AssertNumber("1.234", 1234);
    }

    [Fact]
    public void CreateCell_DeDECommaDecimalEntry_ParsesAsOneAndAHalf()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");

        AssertNumber("1,5", 1.5);
    }

    [Fact]
    public void CreateCell_EnUSGroupedIntegerEntry_ParsesAsTwelveThirtyFour()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        AssertNumber("1,234", 1234);
    }

    [Fact]
    public void CreateCell_EnUSDecimalEntry_ParsesAsOnePointFive()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        AssertNumber("1.5", 1.5);
    }

    private static void AssertNumber(string text, double expected)
    {
        var cell = CellEntryParser.CreateCell(text, Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(expected);
    }
}
