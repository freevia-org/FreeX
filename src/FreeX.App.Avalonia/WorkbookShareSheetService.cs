namespace FreeX.App.Avalonia;

internal sealed record WorkbookShareSheetCapability(
    string ShareSheetLabel,
    bool CanShowShareSheet)
{
    public string ShareSheetLabel { get; init; } = NormalizeLabel(ShareSheetLabel);

    private static string NormalizeLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return label.Trim();
    }
}

internal sealed record WorkbookShareSheetResult(
    bool WasShown,
    string? Message = null)
{
    public static WorkbookShareSheetResult Shown() => new(true);

    public static WorkbookShareSheetResult Unavailable(string message) => new(false, message);
}

internal interface IWorkbookShareSheetService
{
    WorkbookShareSheetCapability Capability { get; }

    Task<WorkbookShareSheetResult> ShowShareSheetAsync(string filePath);
}

internal sealed class UnavailableWorkbookShareSheetService : IWorkbookShareSheetService
{
    private readonly string _unavailableMessage;

    public UnavailableWorkbookShareSheetService(string shareSheetLabel)
    {
        Capability = new WorkbookShareSheetCapability(shareSheetLabel, CanShowShareSheet: false);
        _unavailableMessage = $"{Capability.ShareSheetLabel} is unavailable in this build.";
    }

    public WorkbookShareSheetCapability Capability { get; }

    public Task<WorkbookShareSheetResult> ShowShareSheetAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return Task.FromResult(WorkbookShareSheetResult.Unavailable(_unavailableMessage));
    }
}
