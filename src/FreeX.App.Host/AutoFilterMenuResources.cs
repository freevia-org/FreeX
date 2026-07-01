using FreeX.App.Presentation.Filtering;

namespace FreeX.App.Host;

internal static class AutoFilterMenuResources
{
    public static IAutoFilterMenuTextProvider TextProvider { get; } = new UiAutoFilterMenuTextProvider();

    public static string BlankDisplayText => UiText.Get("AutoFilter_BlankDisplayText");

    private sealed class UiAutoFilterMenuTextProvider : IAutoFilterMenuTextProvider
    {
        public string Get(string resourceKey) => UiText.Get(resourceKey);

        public string Format(string resourceKey, string value) => UiText.Format(resourceKey, value);
    }
}
