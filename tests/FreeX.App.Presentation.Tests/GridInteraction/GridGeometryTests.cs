using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class GridGeometryTests
{
    [Fact]
    public void GridRect_DerivesEdgesFromOriginAndSize()
    {
        var rect = new GridRect(10, 20, 30, 40);

        rect.Left.Should().Be(10);
        rect.Top.Should().Be(20);
        rect.Right.Should().Be(40);
        rect.Bottom.Should().Be(60);
    }

    [Fact]
    public void GridRect_FromEdges_BuildsNonNegativeSize()
    {
        var rect = GridRect.FromEdges(left: 5, top: 7, right: 25, bottom: 47);

        rect.Should().Be(new GridRect(5, 7, 20, 40));
    }

    [Fact]
    public void GridAutoScrollRequest_ReportsWhetherAnyAxisScrolls()
    {
        new GridAutoScrollRequest(0, 0).HasAnyDirection.Should().BeFalse();
        new GridAutoScrollRequest(-1, 0).HasAnyDirection.Should().BeTrue();
        new GridAutoScrollRequest(0, 1).HasAnyDirection.Should().BeTrue();
    }
}
