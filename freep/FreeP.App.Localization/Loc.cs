using Free.Shared.Localization;

namespace FreeP.App.Localization;

/// <summary>
/// Portable, UI-framework-agnostic localization provider for FreeP-owned shell,
/// backstage, and common UI text.
/// </summary>
public abstract class Loc : LocalizedResourceCatalog<Loc>
{
    protected Loc()
    {
    }
}
