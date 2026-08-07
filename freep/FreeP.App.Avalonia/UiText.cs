using FreeP.App.Localization;

namespace FreeP.App.Avalonia;

/// <summary>
/// Thin Avalonia-shell accessor over the portable <see cref="Loc"/> localization provider.
/// Keeps call sites short (<c>UiText.Get("Key")</c>) and gives the shell a single seam to route
/// UI text through, mirroring FreeX.App.Avalonia's <c>UiText</c>.
/// </summary>
internal sealed class UiText : LocalizedUiText
{
    private UiText()
    {
    }
}
