using Free.Shared.Localization;

namespace FreeX.App.Localization;

/// <summary>
/// Portable, UI-framework-agnostic localization provider backed by a
/// <see cref="System.Resources.ResourceManager"/> over an app-owned <c>.resx</c> catalog with a
/// shared common-catalog fallback. Mirrors the WPF host's <c>UiText</c> pattern so the
/// macOS/Avalonia shell can become culture-aware without depending on the host catalog (which is
/// under concurrent change).
///
/// Lookups honour <see cref="CultureInfo.CurrentUICulture"/>. The synthetic
/// <c>qps-ploc</c> pseudo-localization culture expands neutral English so layout/format bugs
/// surface in tests and manual smoke runs.
/// </summary>
public abstract class Loc : LocalizedResourceCatalog<Loc>
{
    protected Loc()
    {
    }
}
