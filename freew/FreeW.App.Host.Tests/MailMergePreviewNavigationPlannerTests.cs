using FreeW.App.Host;

namespace FreeW.App.Host.Tests;

public sealed class MailMergePreviewNavigationPlannerTests
{
    [Theory]
    [InlineData((int)MailMergePreviewNavigationAction.First, 3, 5, 0)]
    [InlineData((int)MailMergePreviewNavigationAction.Previous, 3, 5, 2)]
    [InlineData((int)MailMergePreviewNavigationAction.Previous, 0, 5, 0)]
    [InlineData((int)MailMergePreviewNavigationAction.Next, 3, 5, 4)]
    [InlineData((int)MailMergePreviewNavigationAction.Next, 4, 5, 4)]
    [InlineData((int)MailMergePreviewNavigationAction.Last, 1, 5, 4)]
    [InlineData((int)MailMergePreviewNavigationAction.Next, -2, 5, 1)]
    [InlineData((int)MailMergePreviewNavigationAction.Previous, 99, 5, 3)]
    [InlineData((int)MailMergePreviewNavigationAction.Last, 0, 0, 0)]
    public void TargetIndex_MirrorsWordPreviewRecordNavigation(
        int actionValue,
        int currentIndex,
        int recordCount,
        int expected)
    {
        var action = (MailMergePreviewNavigationAction)actionValue;

        MailMergePreviewNavigationPlanner.TargetIndex(action, currentIndex, recordCount)
            .Should()
            .Be(expected);
    }
}
