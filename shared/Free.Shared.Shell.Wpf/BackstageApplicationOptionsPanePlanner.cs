using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

public static class BackstageApplicationOptionsPanePlanner
{
    public static BackstageOptionsPaneSpec Build(
        string description,
        IApplicationOptionsSummarySource options,
        string dataFolder,
        string? editText = null,
        Action? edit = null)
    {
        var summary = ApplicationOptionsSummaryPlanner.Build(options, dataFolder);

        return new BackstageOptionsPaneSpec(
            description,
            summary.Rows.Select(row => new BackstageFieldRow(row.Label, row.Value)).ToArray(),
            editText,
            edit);
    }
}
