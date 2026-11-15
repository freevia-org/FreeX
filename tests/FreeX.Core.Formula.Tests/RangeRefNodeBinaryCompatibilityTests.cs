using FluentAssertions;
using FreeX.Core.Formula;

namespace FreeX.Core.Formula.Tests;

public class RangeRefNodeBinaryCompatibilityTests
{
    [Fact]
    public void FiveArgumentConstructor_RemainsAvailableToCompiledCallers()
    {
        var constructor = typeof(RangeRefNode).GetConstructor(
            [typeof(CellRefNode), typeof(CellRefNode), typeof(string), typeof(string), typeof(bool)]);
        constructor.Should().NotBeNull();
        var cell = new CellRefNode("A", 1);
        var range = (RangeRefNode)constructor!.Invoke([cell, cell, "First", "Last", true]);
        range.SpanKind.Should().Be(SpanReferenceKind.CellRange);
        range.IsSingleCellSpan.Should().BeTrue();
    }
}
