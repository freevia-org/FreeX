using FreeP.App.Localization;
using Xunit;

namespace FreeP.App.Localization.Tests;

public sealed class WrapperDedupTests
{
    [Fact]
    public void AppWrappers_UseSharedContractsAndKeepProductMetadata() =>
        LocalizationWrapperContractTestSupport.AssertAppWrappers<
            Loc,
            LocalizedUiText,
            AppLanguageOption,
            AppLanguageCatalog>(
            ["freep", "FreeP.App.Localization"],
            "FreeP.App.Localization.Resources.Strings",
            "FreeP.App.Localization.resources.dll");
}
