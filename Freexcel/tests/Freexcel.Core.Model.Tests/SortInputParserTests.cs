using FluentAssertions;
using Freexcel.Core.Commands;

namespace Freexcel.Core.Model.Tests;

public sealed class SortInputParserTests
{
    [Fact]
    public void TryParse_AcceptsMultipleColumnDirectionPairs()
    {
        var parsed = SortInputParser.TryParse("1 asc; 2 desc", out var keys, out var error);

        parsed.Should().BeTrue(error);
        keys.Should().Equal(new SortKey(0, true), new SortKey(1, false));
    }

    [Fact]
    public void TryParse_RejectsZeroColumn()
    {
        var parsed = SortInputParser.TryParse("0 asc", out _, out var error);

        parsed.Should().BeFalse();
        error.Should().Contain("column");
    }
}
