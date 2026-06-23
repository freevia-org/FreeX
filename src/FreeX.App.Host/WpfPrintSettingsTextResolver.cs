using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Host;

internal static class WpfPrintSettingsTextResolver
{
    public static readonly PrintSettingsTextResolver Instance = new(
        UiText.Get,
        (key, args) => UiText.Format(key, args));
}
