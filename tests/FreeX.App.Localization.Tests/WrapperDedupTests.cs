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
            ["src", "FreeX.App.Localization"],
            ["src", "FreeX.App.Host", "FreeX.App.Host.csproj"],
            ["src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj"]);
}
