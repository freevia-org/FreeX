using Free.Shared.Localization;

namespace FreeP.App.Localization;

/// <summary>
/// FreeP UI text facade for shells that need common dialog and shared backstage strings.
/// </summary>
public abstract class LocalizedUiText : LocalizedUiTextCatalog<Loc>
{
    protected LocalizedUiText()
    {
    }
}
