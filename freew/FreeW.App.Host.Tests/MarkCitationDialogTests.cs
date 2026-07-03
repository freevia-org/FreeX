using FreeW.App.Host;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class MarkCitationDialogTests
{
    [StaFact]
    public void Dialog_Defaults_SeedLongCitationAsCases()
    {
        var dlg = MarkCitationDialog.CreateForTest("  Brown v. Board  ");

        dlg.AcceptForTest().Should().BeTrue();

        var result = dlg.ResultForTest;
        result.Should().NotBeNull();
        result!.Citation.Category.Should().Be(CitationCategory.Cases);
        result.Citation.LongCitation.Should().Be("Brown v. Board");
        result.Citation.ShortCitation.Should().BeEmpty();
    }

    [StaFact]
    public void Dialog_CategoryAndShortCitation_CarryThroughSharedPlanner()
    {
        var dlg = MarkCitationDialog.CreateForTest();
        dlg.SetForTest(CitationCategory.Statutes, "  17 U.S.C. 107  ", "  fair use  ");

        dlg.AcceptForTest().Should().BeTrue();

        var citation = dlg.ResultForTest!.Citation;
        citation.Category.Should().Be(CitationCategory.Statutes);
        citation.LongCitation.Should().Be("17 U.S.C. 107");
        citation.ShortCitation.Should().Be("fair use");
    }

    [StaFact]
    public void Dialog_BlankLongCitation_StaysOpenWithoutResult()
    {
        var dlg = MarkCitationDialog.CreateForTest(longCitation: "  ", shortCitation: "Brown");

        dlg.AcceptForTest().Should().BeFalse();

        dlg.ResultForTest.Should().BeNull();
        MarkCitationDialogPlanner.TryBuildCitation(
                new MarkCitationDialogState(CitationCategory.Cases, "  ", "Brown"),
                out _,
                out var validation)
            .Should()
            .BeFalse();
        validation!.Message.Should().Be(MarkCitationDialogPlanner.MissingLongCitationMessage);
    }
}
