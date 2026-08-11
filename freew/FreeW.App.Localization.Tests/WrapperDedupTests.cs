using FreeW.App.Localization;
using Xunit;

namespace FreeW.App.Localization.Tests;

public sealed class WrapperDedupTests
{
    [Fact]
    public void AppWrappers_UseSharedContractsAndConventionOwnedResourceMetadata() =>
        LocalizationWrapperContractTestSupport.AssertAppWrappers<
            Loc,
            LocalizedUiText,
            AppLanguageCatalog>(
            ["freew", "FreeW.App.Localization"],
            ["freew", "FreeW.App.Host", "FreeW.App.Host.csproj"],
            ["freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj"]);
}
