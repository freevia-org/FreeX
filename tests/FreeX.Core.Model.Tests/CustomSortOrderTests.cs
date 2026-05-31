using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

public sealed class CustomSortOrderTests
{
    [Fact]
    public void TryParse_RejectsBlankOrNormalOrder()
    {
        CustomSortOrder.TryParse(null, out _).Should().BeFalse();
        CustomSortOrder.TryParse("", out _).Should().BeFalse();
        CustomSortOrder.TryParse("   ", out _).Should().BeFalse();
        CustomSortOrder.TryParse("Normal", out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_ParsesCommaSeparatedTokens()
    {
        CustomSortOrder.TryParse("Jan, Feb, Mar", out var order).Should().BeTrue();
        order.Should().NotBeNull();
        order!.Tokens.Should().Equal("Jan", "Feb", "Mar");
    }

    [Fact]
    public void IndexOf_RanksByListPositionCaseInsensitive()
    {
        CustomSortOrder.TryParse("Jan, Feb, Mar", out var order).Should().BeTrue();

        order!.IndexOf("jan").Should().Be(0);
        order.IndexOf("FEB").Should().Be(1);
        order.IndexOf("Mar").Should().Be(2);
    }

    [Fact]
    public void IndexOf_ReturnsNegativeForValuesNotInList()
    {
        CustomSortOrder.TryParse("Jan, Feb, Mar", out var order).Should().BeTrue();

        order!.IndexOf("Apr").Should().BeLessThan(0);
        order.IndexOf("").Should().BeLessThan(0);
    }

    [Fact]
    public void Compare_OrdersListMembersByPosition_NotAlphabetically()
    {
        CustomSortOrder.TryParse("Jan, Feb, Mar, Apr", out var order).Should().BeTrue();

        // Alphabetically Apr < Feb < Jan < Mar, but custom order keeps calendar order.
        order!.Compare("Mar", "Jan").Should().BeGreaterThan(0);
        order.Compare("Feb", "Apr").Should().BeLessThan(0);
        order.Compare("Feb", "Feb").Should().Be(0);
    }

    [Fact]
    public void Compare_PlacesListMembersBeforeNonMembers()
    {
        CustomSortOrder.TryParse("Jan, Feb", out var order).Should().BeTrue();

        order!.Compare("Feb", "Zebra").Should().BeLessThan(0);
        order.Compare("Zebra", "Jan").Should().BeGreaterThan(0);
    }

    [Fact]
    public void Compare_FallsBackToOrdinalIgnoreCaseForTwoNonMembers()
    {
        CustomSortOrder.TryParse("Jan, Feb", out var order).Should().BeTrue();

        order!.Compare("Apple", "Banana").Should().BeLessThan(0);
        order.Compare("banana", "Apple").Should().BeGreaterThan(0);
    }
}
