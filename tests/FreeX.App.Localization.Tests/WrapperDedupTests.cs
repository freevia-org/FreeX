using FreeX.App.Localization;
using Xunit;

namespace FreeX.App.Localization.Tests;

public sealed class WrapperDedupTests
{
    [Fact]
    public void AppWrappers_UseSharedContractsAndKeepProductMetadata() =>
        LocalizationWrapperContractTestSupport.AssertAppWrappers<
            Loc,
            LocalizedUiText,
            AppLanguageOption,
            AppLanguageCatalog>(
            ["src", "FreeX.App.Localization"],
            "FreeX.App.Localization.Resources.Strings",
            "FreeX.App.Localization.resources.dll");
}
