using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookTitleFormatterTests
{
    [Theory]
    [InlineData("Book1", false, false, "Book1 - FreeX")]
    [InlineData("Book1", true, false, "Book1* - FreeX")]
    [InlineData("Budget", false, true, "Budget [Group] - FreeX")]
    [InlineData("Budget", true, true, "Budget [Group]* - FreeX")]
    public void Format_CombinesWorkbookDirtyAndGroupedState(
        string workbookName,
        bool isDirty,
        bool isGrouped,
        string expected)
    {
        WorkbookTitleFormatter.Format(workbookName, isDirty, isGrouped).Should().Be(expected);
    }

    [Theory]
    [InlineData("Book1", false, false, " - 2", "Book1 - 2 - FreeX")]
    [InlineData("Book1", true, false, " - 1", "Book1 - 1* - FreeX")]
    [InlineData("Budget", true, true, " - 3", "Budget - 3 [Group]* - FreeX")]
    [InlineData("Book1", false, false, "", "Book1 - FreeX")]
    public void Format_AppendsWindowNumberSuffixBeforeGroupAndDirtyMarkers(
        string workbookName,
        bool isDirty,
        bool isGrouped,
        string windowSuffix,
        string expected)
    {
        WorkbookTitleFormatter.Format(workbookName, isDirty, isGrouped, windowSuffix).Should().Be(expected);
    }

    [Fact]
    public void DisplayNameFromPath_UsesSavedFileNameWithoutExtension()
    {
        WorkbookTitleFormatter.DisplayNameFromPath(@"C:\Work\Quarterly Budget.xlsx")
            .Should()
            .Be("Quarterly Budget");
    }
}
