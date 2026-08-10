using FreeX.App.Localization;
using Xunit;

namespace FreeX.App.Localization.Tests;

public sealed class WrapperDedupTests
{
    [Fact]
    public void AppWrappers_UseSharedContractsAndConventionOwnedResourceMetadata() =>
        LocalizationWrapperContractTestSupport.AssertAppWrappers<
            Loc,
            LocalizedUiText,
            AppLanguageCatalog>(
            ["src", "FreeX.App.Localization"]);
}
