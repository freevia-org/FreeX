using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests.Dialogs;

public sealed class TableTextConversionDialogPlannerTests
{
    [Fact]
    public void Choices_MatchWpfOrderAndDefaultToTab()
    {
        TableTextConversionDialogPlanner.Choices
            .Select(choice => (choice.Label, choice.Delimiter))
            .Should()
            .Equal(
                ("Tab", '\t'),
                ("Comma  ,", ','),
                ("Semicolon  ;", ';'));
        TableTextConversionDialogPlanner.DefaultChoiceIndex.Should().Be(0);
        TableTextConversionDialogPlanner.DelimiterAt(
            TableTextConversionDialogPlanner.DefaultChoiceIndex).Should().Be('\t');
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void DelimiterAt_InvalidIndexReturnsNull(int index)
    {
        TableTextConversionDialogPlanner.DelimiterAt(index).Should().BeNull();
    }
}
