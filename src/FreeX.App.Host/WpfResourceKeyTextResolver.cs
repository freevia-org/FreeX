using Free.Shared.Localization;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Host;

internal static class WpfResourceKeyTextResolver
{
    public static FreeXPlannerTextResources Resources { get; } = new(UiText.Get, UiText.Format);

    public static ResourceKeyTextResolver Instance => Resources.Text;

    public static IStatusBarTextProvider StatusBarTextProvider { get; } =
        new ResourceKeyStatusBarTextProvider(Instance.Get);

    public static DeferredCommandMessage Resolve(
        DeferredCommandMessagePlan plan,
        Func<string, string>? bodyProjector = null) =>
        DeferredCommandMessageResolver.Resolve(plan, Instance, bodyProjector);
}
