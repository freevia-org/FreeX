using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class FormulaR1C1ResolverAdoptionTests
{
    [Theory]
    [InlineData("R[0]C[0]", "E5", "R5C5")]
    [InlineData("R[-4]C[-4]", "A1", "R1C1")]
    [InlineData("R12C3", "$C$12", "R12C[-2]")]
    public void StyleConversionAndReferenceCycling_ShareR1C1PartSemantics(
        string reference,
        string expectedA1,
        string expectedCycled)
    {
        var anchor = new CellAddress(SheetId.New(), 5, 5);

        FormulaReferenceStyleService.ToA1(reference, anchor).Should().Be(expectedA1);
        FormulaReferenceCycler.TryCycleR1C1ReferenceAtCaret(
                reference,
                1,
                anchor,
                out var cycled,
                out _,
                out _)
            .Should().BeTrue();
        cycled.Should().Be(expectedCycled);
    }

    [Theory]
    [InlineData("R[9223372036854775807]C", "R[9223372036854775807]C")]
    [InlineData("R9223372036854775808C1", "R9223372036854775808C1")]
    public void StyleConversionAndReferenceCycling_RejectOverflowingParts(string reference, string expected)
    {
        var anchor = new CellAddress(SheetId.New(), 5, 5);

        FormulaReferenceStyleService.ToA1(reference, anchor).Should().Be(expected);
        FormulaReferenceCycler.TryCycleR1C1ReferenceAtCaret(
                reference,
                1,
                anchor,
                out var cycled,
                out _,
                out _)
            .Should().BeFalse();
        cycled.Should().Be(expected);
    }
}
