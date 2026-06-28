using FreeX.App.Services;

namespace FreeX.App.Host;

internal static class BackstageInfoResources
{
    public static WorkbookInfoDisplayStrings Strings { get; } =
        new(UiText.Get, (key, args) => UiText.Format(key, args));
}
