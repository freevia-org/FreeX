using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class FormulaDeduplicationPolicyTests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 10, 10);
    private static readonly CellAddress Current = new(Anchor.Sheet, 12, 11);

    [Fact]
    public void ReferenceShifter_TransformsEverySupportedReferenceAndCompositeNode()
    {
        var ast = new FunctionCallNode(
            "TEST",
            [
                new CellRefNode("A", 1, SheetName: "Data"),
                new RangeRefNode(
                    new CellRefNode("B", 2),
                    new CellRefNode("C", 3, IsColAbsolute: true),
                    SheetName: "First",
                    EndSheetName: "Last",
                    IsSingleCellSpan: true),
                new FullColumnRangeRefNode("D", "F", SheetName: "Columns"),
                new FullRowRangeRefNode(4, 6, SheetName: "Rows"),
                new BinaryOpNode(
                    new CellRefNode("G", 7),
                    BinaryOperator.Add,
                    new UnaryOpNode(UnaryOperator.Negate, new CellRefNode("H", 8))),
                new UnionNode([new CellRefNode("I", 9), new CellRefNode("J", 10)]),
                new IntersectionNode(new CellRefNode("K", 11), new CellRefNode("L", 12)),
                new NamedRangeEndpointNode(new CellRefNode("M", 13), new NamedRangeNode("End", "Names"))
            ]);

        var shifted = FormulaAstReferenceShifter.ShiftForCell(ast, Anchor, Current)
            .Should().BeOfType<FunctionCallNode>().Subject;

        shifted.Arguments[0].Should().Be(new CellRefNode("B", 3, SheetName: "Data"));

        var range = shifted.Arguments[1].Should().BeOfType<RangeRefNode>().Subject;
        range.Start.Should().Be(new CellRefNode("C", 4));
        range.End.Should().Be(new CellRefNode("C", 5, IsColAbsolute: true));
        range.SheetName.Should().Be("First");
        range.EndSheetName.Should().Be("Last");
        range.IsSingleCellSpan.Should().BeTrue();

        shifted.Arguments[2].Should().Be(
            new FullColumnRangeRefNode("E", "G", SheetName: "Columns"));
        shifted.Arguments[3].Should().Be(
            new FullRowRangeRefNode(6, 8, SheetName: "Rows"));

        var binary = shifted.Arguments[4].Should().BeOfType<BinaryOpNode>().Subject;
        binary.Left.Should().Be(new CellRefNode("H", 9));
        binary.Right.Should().Be(
            new UnaryOpNode(UnaryOperator.Negate, new CellRefNode("I", 10)));

        FormulaSerializer.Serialize(shifted.Arguments[5]).Should().Be("(J11,K12)");
        shifted.Arguments[6].Should().Be(
            new IntersectionNode(new CellRefNode("L", 13), new CellRefNode("M", 14)));
        shifted.Arguments[7].Should().Be(
            new NamedRangeEndpointNode(new CellRefNode("N", 15), new NamedRangeNode("End", "Names")));
    }

    [Fact]
    public void ReferenceShifter_PreservesAbsoluteReferencesAndUnsupportedNodeBehavior()
    {
        FormulaNode[] unchangedNodes =
        [
            new NumberNode(1),
            new StringNode("A1"),
            new BooleanNode(true),
            new OmittedArgumentNode(),
            new ArrayConstantNode([[new CellRefNode("A", 1)]]),
            new NamedRangeNode("Name", "Sheet"),
            new StructuredReferenceNode("Table", "Column"),
            new StructuredCurrentRowReferenceNode("Column", "Table"),
            new ErrorNode(ErrorValue.Value),
            new CellRefNode("A", 1, IsColAbsolute: true, IsRowAbsolute: true, SheetName: "Data")
        ];

        foreach (var node in unchangedNodes)
        {
            FormulaAstReferenceShifter.HasRelativeReferences(node).Should().BeFalse();
            FormulaAstReferenceShifter.ShiftForCell(node, Anchor, Current)
                .Should().BeSameAs(node);
        }
    }

    [Fact]
    public void ReferenceShifter_ReturnsOriginalGraphWhenNoCoordinateChanges()
    {
        var ast = new FunctionCallNode(
            "SUM",
            [new CellRefNode("A", 1), new CellRefNode("B", 2, IsColAbsolute: true)]);

        FormulaAstReferenceShifter.ShiftForCell(ast, Anchor, Anchor).Should().BeSameAs(ast);
    }

    [Theory]
    [MemberData(nameof(OutOfBoundsReferences))]
    public void ReferenceShifter_ConvertsOutOfBoundsReferencesToRefError(FormulaNode ast, CellAddress current)
    {
        FormulaAstReferenceShifter.ShiftForCell(ast, Anchor, current)
            .Should().Be(new ErrorNode(ErrorValue.Ref));
    }

    public static TheoryData<FormulaNode, CellAddress> OutOfBoundsReferences => new()
    {
        { new CellRefNode("A", 1), new CellAddress(Anchor.Sheet, 1, 9) },
        { new RangeRefNode(new CellRefNode("A", 1), new CellRefNode("B", 2)), new CellAddress(Anchor.Sheet, 1, 9) },
        { new FullColumnRangeRefNode("A", "B"), new CellAddress(Anchor.Sheet, 10, 1) },
        { new FullRowRangeRefNode(1, 2), new CellAddress(Anchor.Sheet, 1, 10) }
    };

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("=1+subtotal (9,A1:A2)", true)]
    [InlineData("=AGGREGATE(9,0,A1:A2)", true)]
    [InlineData("=\"SUBTOTAL(\"&A1", false)]
    [InlineData("=\"said \"\"AGGREGATE(\"\"\"", false)]
    [InlineData("=MYSUBTOTAL(9,A1:A2)", false)]
    [InlineData("=_AGGREGATE(9,0,A1:A2)", false)]
    [InlineData("=.SUBTOTAL(9,A1:A2)", false)]
    [InlineData("=X+SUBTOTAL(9,A1:A2)", true)]
    public void FunctionCallScanner_RecognizesOnlyRealSubtotalOrAggregateCalls(
        string? formula,
        bool expected)
    {
        FormulaFunctionCallScanner.ContainsSubtotalOrAggregateCall(formula).Should().Be(expected);
    }

    [Theory]
    [InlineData("", 7u, true, 7L, false)]
    [InlineData("[0]", 7u, true, 7L, false)]
    [InlineData("[-6]", 7u, true, 1L, false)]
    [InlineData("[8]", 7u, true, 15L, false)]
    [InlineData("12", 7u, true, 12L, true)]
    [InlineData("-2", 7u, true, -2L, true)]
    [InlineData("[]", 7u, false, 0L, false)]
    [InlineData("[abc]", 7u, false, 0L, false)]
    [InlineData("[9223372036854775807]", 7u, false, 0L, false)]
    [InlineData("9223372036854775808", 7u, false, 0L, true)]
    public void R1C1PartResolver_PreservesAbsoluteRelativeAndFailureSemantics(
        string text,
        uint anchor,
        bool expectedSuccess,
        long expectedValue,
        bool expectedAbsolute)
    {
        var success = R1C1ReferencePartResolver.TryResolve(
            text,
            anchor,
            out var value,
            out var absolute);

        success.Should().Be(expectedSuccess);
        value.Should().Be(expectedValue);
        absolute.Should().Be(expectedAbsolute);
    }
}
