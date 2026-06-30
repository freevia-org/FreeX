using Free.Shared.Localization;

namespace FreeX.App.Localization;

/// <summary>
/// Shared UI text facade for platform shells that keep local <c>UiText</c> entry points.
/// </summary>
public abstract class LocalizedUiText : LocalizedUiTextCatalog<Loc>
{
    protected LocalizedUiText()
    {
    }
}
