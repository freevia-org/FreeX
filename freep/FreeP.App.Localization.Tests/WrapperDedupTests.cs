using FreeP.App.Localization;
using Xunit;

namespace FreeP.App.Localization.Tests;

public sealed class WrapperDedupTests
{
    [Fact]
    public void AppWrappers_UseSharedContractsAndConventionOwnedResourceMetadata() =>
        LocalizationWrapperContractTestSupport.AssertAppWrappers<
            Loc,
            LocalizedUiText,
            AppLanguageCatalog>(
            ["freep", "FreeP.App.Localization"],
            ["freep", "FreeP.App.Host", "FreeP.App.Host.csproj"],
            ["freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj"]);
}
