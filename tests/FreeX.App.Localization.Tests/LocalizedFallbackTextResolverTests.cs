using FluentAssertions;
using Free.Shared.Localization;
using Xunit;

namespace FreeX.App.Localization.Tests;

public sealed class LocalizedFallbackTextResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PrintPreview_PageSetupButton")]
    [InlineData("[[PrintPreview_PageSetupButton]]")]
    public void Resolve_UsesFallbackForUnresolvedCatalogValues(string? resolved)
    {
        LocalizedFallbackTextResolver.Resolve(
                "PrintPreview_PageSetupButton",
                "Page Setup",
                _ => resolved)
            .Should()
            .Be("Page Setup");
    }

    [Fact]
    public void Resolve_ReturnsLocalizedTextAndOptionallyStripsMnemonics()
    {
        LocalizedFallbackTextResolver.Resolve(
                "Common_Cancel",
                "Cancel",
                _ => "_Annuler")
            .Should()
            .Be("_Annuler");

        LocalizedFallbackTextResolver.Resolve(
                "Common_Cancel",
                "Cancel",
                _ => "_Annuler",
                stripMnemonics: true)
            .Should()
            .Be("Annuler");
    }

    [Fact]
    public void Resolve_StripsMnemonicsAfterFallbackSelection()
    {
        LocalizedFallbackTextResolver.Resolve(
                "PrintPreview_PrintButton",
                "_Print...",
                key => LocalizedTextCatalog.CreateMissingText(key),
                stripMnemonics: true)
            .Should()
            .Be("Print...");
    }

    [Fact]
    public void Resolve_PreservesPseudoLocalizedBracketTextThatIsNotTheMissingKey()
    {
        LocalizedFallbackTextResolver.Resolve(
                "Common_Cancel",
                "Cancel",
                _ => "[[CCaanncceell]]")
            .Should()
            .Be("[[CCaanncceell]]");

        LocalizedFallbackTextResolver.IsMissingResourceToken("[[Common_Cancel]]", "Common_Cancel")
            .Should()
            .BeTrue();
        LocalizedFallbackTextResolver.IsMissingResourceToken("[[CCaanncceell]]", "Common_Cancel")
            .Should()
            .BeFalse();
        LocalizedFallbackTextResolver.IsMissingResourceToken("[[Any_Missing_Key]]")
            .Should()
            .BeTrue();
    }
}
