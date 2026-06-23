namespace FreeW.App.Host;

internal enum MailMergePreviewNavigationAction
{
    First,
    Previous,
    Next,
    Last
}

internal static class MailMergePreviewNavigationPlanner
{
    public static int TargetIndex(MailMergePreviewNavigationAction action, int currentIndex, int recordCount)
    {
        if (recordCount <= 0)
            return 0;

        var current = Math.Clamp(currentIndex, 0, recordCount - 1);
        return action switch
        {
            MailMergePreviewNavigationAction.First => 0,
            MailMergePreviewNavigationAction.Previous => Math.Max(0, current - 1),
            MailMergePreviewNavigationAction.Next => Math.Min(recordCount - 1, current + 1),
            MailMergePreviewNavigationAction.Last => recordCount - 1,
            _ => current
        };
    }
}
