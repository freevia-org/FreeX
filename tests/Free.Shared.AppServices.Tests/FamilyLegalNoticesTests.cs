namespace Free.Shared.AppServices.Tests;

public sealed class FamilyLegalNoticesTests
{
    [Fact]
    public void CombinedNotice_disclaims_affiliation_and_uses_the_official_mark_footnote_form()
    {
        FamilyLegalNotices.CombinedTrademarkNotice.Should().Contain("not affiliated with, authorized, sponsored, endorsed, or approved by Microsoft Corporation");
        FamilyLegalNotices.CombinedTrademarkNotice.Should().Contain("trademarks of the Microsoft group of companies");
        FamilyLegalNotices.CombinedTrademarkNotice.Should().Contain("All other trademarks are the property of their respective owners");
    }

    [Fact]
    public void CombinedNotice_limits_product_names_to_referential_plain_text_use()
    {
        FamilyLegalNotices.CombinedTrademarkNotice.Should().Contain("used only in plain text");
        FamilyLegalNotices.CombinedTrademarkNotice.Should().Contain("No Microsoft logos, product icons, sounds, or trade dress");
        FamilyLegalNotices.CombinedTrademarkNotice.Should().Contain("no Microsoft font files are redistributed");
    }
}
