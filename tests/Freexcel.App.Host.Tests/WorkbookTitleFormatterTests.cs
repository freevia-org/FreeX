using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class WorkbookTitleFormatterTests
{
    [Theory]
    [InlineData("Book1", false, false, "Book1 - Freexcel")]
    [InlineData("Book1", true, false, "Book1* - Freexcel")]
    [InlineData("Budget", false, true, "Budget [Group] - Freexcel")]
    [InlineData("Budget", true, true, "Budget [Group]* - Freexcel")]
    public void Format_CombinesWorkbookDirtyAndGroupedState(
        string workbookName,
        bool isDirty,
        bool isGrouped,
        string expected)
    {
        WorkbookTitleFormatter.Format(workbookName, isDirty, isGrouped).Should().Be(expected);
    }

    [Fact]
    public void DisplayNameFromPath_UsesSavedFileNameWithoutExtension()
    {
        WorkbookTitleFormatter.DisplayNameFromPath(@"C:\Work\Quarterly Budget.xlsx")
            .Should()
            .Be("Quarterly Budget");
    }
}
