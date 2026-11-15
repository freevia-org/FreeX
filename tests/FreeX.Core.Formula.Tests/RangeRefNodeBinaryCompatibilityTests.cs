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

    [Fact]
    public void OriginalRecordDeconstructorsAndDeleteConstructor_RemainAvailable()
    {
        var rangeDeconstruct = typeof(RangeRefNode).GetMethod("Deconstruct",
            [typeof(CellRefNode).MakeByRefType(), typeof(CellRefNode).MakeByRefType(),
             typeof(string).MakeByRefType(), typeof(string).MakeByRefType(), typeof(bool).MakeByRefType()]);
        rangeDeconstruct.Should().NotBeNull();
        var cell = new CellRefNode("A", 1);
        object?[] rangeValues = [null, null, null, null, null];
        rangeDeconstruct!.Invoke(new RangeRefNode(cell, cell, "First", "Last", true), rangeValues);
        rangeValues.Should().Equal(cell, cell, "First", "Last", true);

        var deleteConstructor = typeof(DeleteSheetOp).GetConstructor([typeof(string), typeof(IReadOnlyList<string>)]);
        deleteConstructor.Should().NotBeNull();
        var tables = new[] { "Table1" };
        var operation = (DeleteSheetOp)deleteConstructor!.Invoke(["First", tables]);
        var deleteDeconstruct = typeof(DeleteSheetOp).GetMethod("Deconstruct",
            [typeof(string).MakeByRefType(), typeof(IReadOnlyList<string>).MakeByRefType()]);
        deleteDeconstruct.Should().NotBeNull();
        object?[] deleteValues = [null, null];
        deleteDeconstruct!.Invoke(operation, deleteValues);
        deleteValues[0].Should().Be("First");
        deleteValues[1].Should().BeSameAs(tables);
    }
}
