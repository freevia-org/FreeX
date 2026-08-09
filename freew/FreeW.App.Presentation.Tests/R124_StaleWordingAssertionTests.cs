using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// R124: two pre-existing tests had encoded PRODUCT WORDING that later commits intentionally changed,
/// and were never updated to match -- both were part of the FreeW.slnx-red carry-forward from r122/r123
/// (r122 only ran FreeW.App.Avalonia.Tests, which does not contain either test class). Neither is a
/// product defect: the current wording is correct per the commits that introduced it (verified below by
/// citing the commit and the sibling test suite that already exercises the current wording).
/// </summary>
public class R124_StaleWordingAssertionTests
{
    /// <summary>
    /// Side-to-Side's cross-page clipboard/undo stopped being deferred in 6769bf3a53 ("freew: verify
    /// side-to-side cross-page editing parity"), which updated FreeW.App.Avalonia.Tests.ViewTabDepthTests
    /// but missed this project's FreeWViewDepthPlannerTests. Only the Avalonia horizontal page-grid layout
    /// remains deferred now.
    /// </summary>
    [Fact]
    public void SideToSide_limitation_no_longer_defers_cross_page_clipboard_and_undo()
    {
        var plan = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.SideToSidePreview);

        plan.Limitation.Should().NotContain("Cross-page clipboard/undo");
        plan.Limitation.Should().Contain("horizontal page-grid layout remains deferred");
    }

    /// <summary>
    /// The thesaurus action tooltip wording moved from "Replace X with Y" to "Insert Y in place of X" in
    /// a57cb73ff4 ("Close FreeW Avalonia thesaurus action parity"), which added
    /// ThesaurusPresentationPlannerTests for the new wording but left this project's
    /// ProofingPresentationPlannerTests asserting the old "Replace" text via the ReplaceToolTip
    /// compatibility alias.
    /// </summary>
    [Fact]
    public void ThesaurusAction_replaceToolTip_alias_reflects_current_insert_wording()
    {
        var entry = new ThesaurusEntry("happy", [new ThesaurusSense("adj", ["glad_of"])]);
        var plan = ThesaurusPresentationPlanner.Build("Happy", entry);

        plan.Senses[0].Actions[0].ReplaceToolTip.Should().Be("Insert \"glad of\" in place of \"Happy\"");
        plan.Senses[0].Actions[0].ReplaceToolTip.Should().Be(plan.Senses[0].Actions[0].InsertToolTip,
            "ReplaceToolTip is documented as a compatibility alias for InsertToolTip, not an independent wording");
    }
}
