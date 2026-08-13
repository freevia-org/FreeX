using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class HeaderFooterPresetCatalogTests
{
    [Fact]
    public void PageSetupModel_ProjectsTheCanonicalTypedCatalogs()
    {
        PageSetupDialogModel.HeaderPresetChoices.Should().BeSameAs(HeaderFooterPresetCatalog.HeaderChoices);
        PageSetupDialogModel.FooterPresetChoices.Should().BeSameAs(HeaderFooterPresetCatalog.FooterChoices);
        PageSetupDialogModel.HeaderFooterPresetChoices.Should().BeSameAs(HeaderFooterPresetCatalog.CompactChoices);
    }

    [Fact]
    public void PresetIdentities_DistinguishDuplicateTokenValues()
    {
        HeaderFooterPresetCatalog.HeaderChoices
            .Where(choice => choice.Value == "&[File]")
            .Select(choice => choice.Id)
            .Should().Equal(
                HeaderFooterPresetId.Book,
                HeaderFooterPresetId.BookXlsx,
                HeaderFooterPresetId.FileName);
    }

    [Fact]
    public void TypedPresetApplication_PreservesSideSectionsAndUsesTokenValue()
    {
        var initial = new WorksheetHeaderFooter("left", "custom", "right");
        var choice = HeaderFooterPresetCatalog.FooterChoices
            .Single(candidate => candidate.Id == HeaderFooterPresetId.Time);

        PageSetupDialogPlanner.ApplyFooterPreset(initial, choice)
            .Should().Be(new WorksheetHeaderFooter("left", "&[Time]", "right"));
    }
}
