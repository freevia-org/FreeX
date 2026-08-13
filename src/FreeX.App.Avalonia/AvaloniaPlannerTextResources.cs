using Free.Shared.Localization;
using FreeX.App.Presentation;
using FreeX.App.Presentation.Filtering;

namespace FreeX.App.Avalonia;

internal static class AvaloniaPlannerTextResources
{
    private static FreeXPlannerTextResources Resources { get; } = new(UiText.Get, UiText.Format);

    public static ResourceKeyTextResolver Text => Resources.Text;

    public static AutoFilterMenuTextResolver AutoFilter => Resources.AutoFilter;
}
