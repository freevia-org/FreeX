using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    // R31-formula-text-advanced-1: NUMBERVALUE must not trim a boundary NBSP (U+00A0) — only
    // ASCII space/tab/CR/LF are ignorable to Excel here, matching the interior-NBSP behavior
    // already pinned by Numbervalue_DoesNotStripNonBreakingSpace.
    [Fact]
    public void Numbervalue_LeadingNonBreakingSpace_ReturnsValueError() =>
        _eval.Evaluate("=NUMBERVALUE(CHAR(160)&\"1234\")", MakeSheet())
            .Should().Be(ErrorValue.Value);

    [Fact]
    public void Numbervalue_LeadingAsciiSpace_StillTrimsAndParses() =>
        _eval.Evaluate("=NUMBERVALUE(\" 1234\")", MakeSheet())
            .Should().Be(new NumberValue(1234));
}
