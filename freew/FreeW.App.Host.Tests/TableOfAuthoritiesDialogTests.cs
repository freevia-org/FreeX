using FreeW.App.Host;
using FreeW.Core.Model;
using Xunit;
using FluentAssertions;

namespace FreeW.App.Host.Tests;

/// <summary>
/// STA coverage for <see cref="TableOfAuthoritiesDialog"/>: verifies that the control wiring correctly
/// reflects the chosen options into a <see cref="ToaOptions"/> on accept.
/// Uses the <see cref="TableOfAuthoritiesDialog.CreateForTest"/> /
/// <see cref="TableOfAuthoritiesDialog.AcceptForTest"/> seam.
/// </summary>
public sealed class TableOfAuthoritiesDialogTests
{
    [StaFact]
    public void Dialog_Defaults_ProduceDefaultOptions()
    {
        var dlg = TableOfAuthoritiesDialog.CreateForTest();
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.Options.UsePassim.Should().BeFalse();
        result.Options.KeepOriginalFormatting.Should().BeFalse();
        result.Options.CategoryFilter.Should().BeNull();
        result.Options.TabLeader.Should().Be(ToaTabLeader.Dots);
    }

    [StaFact]
    public void Dialog_Passim_True_OptionCarriesThrough()
    {
        var dlg = TableOfAuthoritiesDialog.CreateForTest(passim: true);
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.Options.UsePassim.Should().BeTrue();
    }

    [StaFact]
    public void Dialog_KeepFormatting_True_OptionCarriesThrough()
    {
        var dlg = TableOfAuthoritiesDialog.CreateForTest(keepFormatting: true);
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.Options.KeepOriginalFormatting.Should().BeTrue();
    }

    [StaFact]
    public void Dialog_CategoryFilter_OptionCarriesThrough()
    {
        var dlg = TableOfAuthoritiesDialog.CreateForTest(categoryFilter: CitationCategory.Statutes);
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.Options.CategoryFilter.Should().Be(CitationCategory.Statutes);
    }

    [StaFact]
    public void Dialog_TabLeader_Dashes_OptionCarriesThrough()
    {
        var dlg = TableOfAuthoritiesDialog.CreateForTest(leader: ToaTabLeader.Dashes);
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.Options.TabLeader.Should().Be(ToaTabLeader.Dashes);
    }

    [StaFact]
    public void Dialog_AllOptions_CombinedCorrectly()
    {
        var dlg = TableOfAuthoritiesDialog.CreateForTest(
            passim: true,
            keepFormatting: true,
            categoryFilter: CitationCategory.Cases,
            leader: ToaTabLeader.None);
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.Options.UsePassim.Should().BeTrue();
        result.Options.KeepOriginalFormatting.Should().BeTrue();
        result.Options.CategoryFilter.Should().Be(CitationCategory.Cases);
        result.Options.TabLeader.Should().Be(ToaTabLeader.None);
    }

    [StaFact]
    public void Dialog_RepresentativePopulatedState_matches_shared_evidence_seed()
    {
        var state = FreeW.App.Presentation.Ribbon.TableOfAuthoritiesDialogPlanner.RepresentativePopulatedState;
        var dlg = TableOfAuthoritiesDialog.CreateForTest(
            passim: state.UsePassim,
            keepFormatting: state.KeepOriginalFormatting,
            categoryFilter: state.CategoryFilter,
            leader: state.TabLeader);

        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.Options.Should().BeEquivalentTo(
            FreeW.App.Presentation.Ribbon.TableOfAuthoritiesDialogPlanner.BuildOptions(state));
    }
}
