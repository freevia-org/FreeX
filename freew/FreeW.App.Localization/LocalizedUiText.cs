using Free.Shared.Localization;

namespace FreeW.App.Localization;

/// <summary>
/// FreeW UI text facade for shells that need common dialog and shared backstage strings.
/// </summary>
public abstract class LocalizedUiText : LocalizedUiTextCatalog<Loc>
{
    protected LocalizedUiText()
    {
    }
}
