using System.IO;

namespace Freexcel.App.Host;

public enum ShareWorkbookPlanKind
{
    ShareExistingFile,
    SaveAsBeforeShare
}

public sealed record ShareWorkbookPlan(ShareWorkbookPlanKind Kind, string? Path);

public static class ShareWorkbookPlanner
{
    public static ShareWorkbookPlan CreatePlan(string? currentFilePath)
    {
        if (string.IsNullOrWhiteSpace(currentFilePath))
            return new ShareWorkbookPlan(ShareWorkbookPlanKind.SaveAsBeforeShare, null);

        if (!File.Exists(currentFilePath))
            return new ShareWorkbookPlan(ShareWorkbookPlanKind.SaveAsBeforeShare, null);

        return new ShareWorkbookPlan(ShareWorkbookPlanKind.ShareExistingFile, currentFilePath);
    }
}
