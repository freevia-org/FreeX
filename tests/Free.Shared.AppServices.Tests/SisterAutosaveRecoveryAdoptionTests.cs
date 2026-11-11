using FreeP.App.Compositor;
using FreeW.App.Presentation.Shell;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// Proves each sister app binds the shared autosave profile layer with the right parameters.
/// The behaviour of that layer is tested once in <see cref="AutosaveApplicationProfileTests"/>.
/// </summary>
public sealed class SisterAutosaveRecoveryAdoptionTests
{
    [Fact]
    public void BindingsRetainProductCadenceAndSerializerPair()
    {
        FreePAutosaveSession.DefaultInterval.Should().Be(TimeSpan.FromSeconds(60));
        FreeWAutosaveSession.DefaultInterval.Should().Be(TimeSpan.FromSeconds(30));
        FreePAutosaveProfile.Session.Interval.Should().Be(FreePAutosaveSession.DefaultInterval);
        FreeWAutosaveProfile.Session.Interval.Should().Be(FreeWAutosaveSession.DefaultInterval);
        FreePAutosaveProfile.Session.Recovery.Should().BeSameAs(FreePAutosaveProfile.Recovery);
        FreeWAutosaveProfile.Session.Recovery.Should().BeSameAs(FreeWAutosaveProfile.Recovery);
    }

    [Fact]
    public void BindingsParameterizePromptProductDocumentNounAndFallback()
    {
        var candidate = new AutosaveRecoveryCandidate(
            "snapshot.fxl",
            "snapshot.sidecar.json",
            new AutosaveSidecar());

        FreePAutosaveProfile.Recovery
            .CreateOffer(
                new AutosaveRecoveryPlan(candidate, "Quarterly"),
                2,
                AutosaveRecoveryPromptMode.StartupQuotedDisplayName)
            .Prompt.Should().Be(
                "FreeP found unsaved changes to \"Quarterly\" from a previous session (2 unsaved presentations found). Recover this one?");

        FreeWAutosaveProfile.Recovery
            .CreateOffer(
                new AutosaveRecoveryPlan(candidate, "Draft"),
                2,
                AutosaveRecoveryPromptMode.Manual)
            .Prompt.Should().Be(
                "Recover unsaved changes to Draft? (2 unsaved documents found.)");

        FreePAutosaveProfile.Recovery.UnnamedItemFallback.Should().Be("a presentation");
        FreeWAutosaveProfile.Recovery.UnnamedItemFallback.Should().Be("a document");
    }

    [Fact]
    public void BindingsRetainProductSpecificTextAndFreePBackstageExtension()
    {
        var presentationText = FreePAutosaveProfile.Recovery.ResolveText();
        var documentText = FreeWAutosaveProfile.Recovery.ResolveText();

        presentationText.Title.Should().Be("FreeP - Recover");
        presentationText.NoDocumentsMessage.Should().Be("No unsaved presentations were found.");
        presentationText.FailureMessageFormat.Should().Be("Could not recover the presentation.\n\n{0}");
        documentText.Title.Should().Be("FreeW - Recover");
        documentText.NoDocumentsMessage.Should().Be("No unsaved documents were found.");
        documentText.FailureMessageFormat.Should().Be("Could not recover the document.\n\n{0}");

        FreePAutosaveProfile.ResolveBackstageLabel().Should().Be("Recover Unsaved Presentations");
        FreePAutosaveProfile.RequiredResourceKeys.Should().Contain("Autosave_Recovery_Backstage_Label");
        FreeWAutosaveProfile.Recovery.RequiredResourceKeys
            .Should().NotContain("Autosave_Recovery_Backstage_Label");
    }
}
