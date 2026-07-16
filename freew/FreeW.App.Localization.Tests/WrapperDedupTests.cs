using FreeW.App.Localization;
using Xunit;

namespace FreeW.App.Localization.Tests;

public sealed class WrapperDedupTests
{
    [Fact]
    public void AppWrappers_UseSharedContractsAndKeepProductMetadata() =>
        LocalizationWrapperContractTestSupport.AssertAppWrappers<
            Loc,
            LocalizedUiText,
            AppLanguageOption,
            AppLanguageCatalog>(
            ["freew", "FreeW.App.Localization"],
            "FreeW.App.Localization.Resources.Strings",
            "FreeW.App.Localization.resources.dll");
}
