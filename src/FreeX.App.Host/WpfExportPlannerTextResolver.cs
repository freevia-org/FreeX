using FreeX.App.Services;

namespace FreeX.App.Host;

internal static class WpfExportPlannerTextResolver
{
    public static readonly ExportPlannerTextResolver Instance = new(
        UiText.Get,
        (key, args) => UiText.Format(key, args));
}
