using Free.Shared.Localization;

namespace FreeX.App.Localization;

public sealed record AppLanguageOption(string CultureName, string DisplayName);

public abstract class AppLanguageCatalog : LocalizedAppLanguageCatalog<AppLanguageOption, Loc>
{
    protected AppLanguageCatalog()
    {
    }
}
