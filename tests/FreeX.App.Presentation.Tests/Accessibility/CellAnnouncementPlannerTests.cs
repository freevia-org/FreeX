using FluentAssertions;
using FreeX.App.Presentation.Accessibility;

namespace FreeX.App.Presentation.Tests.Accessibility;

public sealed class CellAnnouncementPlannerTests
{
    [Fact]
    public void BuildName_OrdersEveryPortableCue()
    {
        var metadata = new CellAnnouncementMetadata(
            HasComment: true,
            CommentTitle: "Threaded Comment",
            IsFormula: true,
            IsMerged: true,
            HasDataValidation: true,
            HasHyperlink: true,
            IsLocked: true);

        CellAnnouncementPlanner.BuildName("G7", "100", metadata)
            .Should()
            .Be("G7: 100, has threaded comment, is a formula, is merged, has data validation, has a hyperlink, is locked");
    }

    [Theory]
    [InlineData(null, "A1")]
    [InlineData("", "A1")]
    [InlineData(" ", "A1")]
    [InlineData("42", "A1: 42")]
    public void BuildName_PreservesAddressAndValuePolicy(string? value, string expected)
    {
        CellAnnouncementPlanner.BuildName("A1", value, default).Should().Be(expected);
    }
}
