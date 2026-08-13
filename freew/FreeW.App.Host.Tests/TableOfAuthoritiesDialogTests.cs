using FreeW.App.Host;
using FreeW.Core.Model;
using Xunit;
using FluentAssertions;
using System.Reflection;
using System.Windows.Controls;

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
        result!.UsePassim.Should().BeFalse();
        result.KeepOriginalFormatting.Should().BeFalse();
        result.CategoryFilter.Should().BeNull();
        result.TabLeader.Should().Be(ToaTabLeader.Dots);
    }

    [StaFact]
    public void Dialog_Passim_True_OptionCarriesThrough()
    {
        var dlg = TableOfAuthoritiesDialog.CreateForTest(passim: true);
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.UsePassim.Should().BeTrue();
    }

    [StaFact]
    public void Dialog_KeepFormatting_True_OptionCarriesThrough()
    {
        var dlg = TableOfAuthoritiesDialog.CreateForTest(keepFormatting: true);
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.KeepOriginalFormatting.Should().BeTrue();
    }

    [StaFact]
    public void Dialog_CategoryFilter_OptionCarriesThrough()
    {
        var dlg = TableOfAuthoritiesDialog.CreateForTest(categoryFilter: CitationCategory.Statutes);
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.CategoryFilter.Should().Be(CitationCategory.Statutes);
    }

    [StaFact]
    public void Dialog_TabLeader_Dashes_OptionCarriesThrough()
    {
        var dlg = TableOfAuthoritiesDialog.CreateForTest(leader: ToaTabLeader.Dashes);
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.TabLeader.Should().Be(ToaTabLeader.Dashes);
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
        result!.UsePassim.Should().BeTrue();
        result.KeepOriginalFormatting.Should().BeTrue();
        result.CategoryFilter.Should().Be(CitationCategory.Cases);
        result.TabLeader.Should().Be(ToaTabLeader.None);
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
        result.Should().BeEquivalentTo(
            FreeW.App.Presentation.Ribbon.TableOfAuthoritiesDialogPlanner.BuildOptions(state));
    }

    [StaFact]
    public void Dialog_DoesNotAcceptWhenLeaderSelectionIsMissing()
    {
        var dlg = TableOfAuthoritiesDialog.CreateForTest();
        var leader = (ComboBox)(typeof(TableOfAuthoritiesDialog)
            .GetField("_leaderCombo", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dlg)!);
        leader.SelectedIndex = -1;

        dlg.AcceptForTest().Should().BeNull();
    }
}
