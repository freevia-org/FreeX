using Avalonia.Controls;

namespace FreeW.App.Avalonia.Backstage;

internal sealed partial class BackstageView
{
    internal Control BuildPaneForVisualHarness(string routeId) => routeId switch
    {
        "backstage-home" => BuildHomePane(),
        "backstage-new" => BuildNewPane(),
        "backstage-open" => BuildOpenPane(),
        "backstage-info" => BuildInfoPane(),
        "backstage-share" => BuildSharePane(),
        "backstage-save-as" => BuildSaveAsPane(),
        "backstage-print" => BuildPrintPane(),
        "backstage-export" => BuildExportPane(),
        "backstage-account" => BuildAccountPane(),
        "backstage-options" => BuildOptionsPane(),
        _ => throw new ArgumentOutOfRangeException(nameof(routeId), routeId, "Unknown Backstage visual route."),
    };
}
