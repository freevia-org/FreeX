using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>Canonical command identity for the shared Chart Design color catalog.</summary>
public static class ChartColorRibbonCommandCatalog
{
    public const string ParentCommandId = "freew.chart-colors";

    public static string CommandId(ChartColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        return $"{ParentCommandId}-{scheme.Id}";
    }
}
