using Free.Shared.Localization;

namespace FreeW.App.Localization;

/// <summary>
/// Portable, UI-framework-agnostic localization provider for FreeW-owned shell,
/// backstage, and common UI text.
/// </summary>
[LocalizedResourceCatalog("FreeW.App.Localization.Resources.Strings", "FreeW.App.Localization.resources.dll")]
public abstract class Loc : LocalizedResourceCatalog<Loc>
{
    protected Loc()
    {
    }
}
