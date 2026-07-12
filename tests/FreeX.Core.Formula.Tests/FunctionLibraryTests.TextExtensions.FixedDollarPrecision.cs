using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R29-text-repass-1: FIXED/DOLLAR were silently clamping the requested decimal-digit count to 99
// (the max precision .NET's "N"/"F" format specifiers support) instead of honoring the full
// requested width the way real Excel does, so e.g. FIXED(1,100) came back one zero short.
public partial class FunctionLibraryTests
{
    [Fact]
    public void Fixed_OneHundredDecimals_PadsPastNetFormatCapToFullRequestedWidth()
    {
        var expected = "1." + new string('0', 100);

        _eval.Evaluate("=FIXED(1,100,TRUE)", MakeSheet())
            .Should().Be(new TextValue(expected));
    }

    [Fact]
    public void Dollar_OneHundredDecimals_PadsPastNetFormatCapToFullRequestedWidth()
    {
        var expected = "$1." + new string('0', 100);

        _eval.Evaluate("=DOLLAR(1,100)", MakeSheet())
            .Should().Be(new TextValue(expected));
    }

    [Fact]
    public void Fixed_OrdinaryDecimalsWithinNetFormatRange_StillFormatsExactly()
    {
        // Sibling case within the pre-existing 0-99 .NET-native path (well under the 99 cap) —
        // must keep working unchanged after the >99 padding fix.
        _eval.Evaluate("=FIXED(1234.567,2,TRUE)", MakeSheet())
            .Should().Be(new TextValue("1234.57"));
    }
}
