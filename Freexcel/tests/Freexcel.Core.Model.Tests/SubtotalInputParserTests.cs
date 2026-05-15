using FluentAssertions;
using Freexcel.Core.Commands;

namespace Freexcel.Core.Model.Tests;

public sealed class SubtotalInputParserTests
{
    [Fact]
    public void TryParse_AcceptsMultipleSubtotalColumnsAndOptions()
    {
        var parsed = SubtotalInputParser.TryParse("1,2+3,sum,replace pagebreak above", out var options, out var error);

        parsed.Should().BeTrue(error);
        options.GroupColumnOffset.Should().Be(0);
        options.SubtotalColumnOffsets.Should().Equal(1u, 2u);
        options.FunctionNumber.Should().Be(9);
        options.ReplaceExisting.Should().BeTrue();
        options.PageBreakBetweenGroups.Should().BeTrue();
        options.SummaryBelowData.Should().BeFalse();
    }

    [Fact]
    public void TryParse_RejectsInvalidSubtotalColumn()
    {
        var parsed = SubtotalInputParser.TryParse("1,0,sum", out _, out var error);

        parsed.Should().BeFalse();
        error.Should().Contain("subtotal column");
    }
}
