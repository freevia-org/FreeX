namespace Free.Shared.AppServices;

public sealed class ResourceKeyStatusBarTextProvider : IStatusBarTextProvider
{
    private readonly Func<string, string> _getText;

    public ResourceKeyStatusBarTextProvider(Func<string, string> getText)
    {
        ArgumentNullException.ThrowIfNull(getText);
        _getText = getText;
    }

    public string GetReadyText() =>
        _getText(StatusBarTextResourceKeys.ReadyText);

    public string GetReadyText(bool isManualCalculationMode, bool hasPendingRecalculation) =>
        _getText(StatusBarTextResourceKeys.CellModeResourceKey(isManualCalculationMode, hasPendingRecalculation));

    public string GetReadoutFormat(StatusBarReadoutKind kind) =>
        _getText(StatusBarTextResourceKeys.ReadoutFormat(kind));

    public string GetReadoutLabel(StatusBarReadoutKind kind) =>
        _getText(StatusBarTextResourceKeys.ReadoutLabel(kind));
}
