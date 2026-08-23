using FreeP.App.Compositor;
using FreeW.App.Presentation.Shell;
using FreePPlan = FreeP.App.Compositor.AutosaveRecoveryPlan;
using FreeWPlan = FreeW.App.Presentation.Shell.AutosaveRecoveryPlan;

namespace Free.Shared.AppServices.Tests;

public sealed class SisterAutosaveRecoveryAdoptionTests
{
    [Fact]
    public void FacadesRetainProductCadenceAndImplementSharedPlanContract()
    {
        FreePAutosaveSession.DefaultInterval.Should().Be(TimeSpan.FromSeconds(60));
        FreeWAutosaveSession.DefaultInterval.Should().Be(TimeSpan.FromSeconds(30));

        var candidate = new AutosaveRecoveryCandidate(
            "snapshot.fxl",
            "snapshot.sidecar.json",
            new AutosaveSidecar());
        IAutosaveRecoveryPlan presentationPlan = new FreePPlan(candidate, "Presentation");
        IAutosaveRecoveryPlan documentPlan = new FreeWPlan(candidate, "Document");

        presentationPlan.DisplayName.Should().Be("Presentation");
        documentPlan.DisplayName.Should().Be("Document");
    }

    [Fact]
    public void FacadesParameterizePromptProductAndDocumentNoun()
    {
        var candidate = new AutosaveRecoveryCandidate(
            "snapshot.fxl",
            "snapshot.sidecar.json",
            new AutosaveSidecar());

        new FreePRecoveryOffer(
                new FreePPlan(candidate, "Quarterly"),
                2,
                FreePRecoveryPromptMode.StartupQuotedDisplayName)
            .Prompt.Should().Be(
                "FreeP found unsaved changes to \"Quarterly\" from a previous session (2 unsaved presentations found). Recover this one?");

        new FreeWRecoveryOffer(
                new FreeWPlan(candidate, "Draft"),
                2,
                FreeWRecoveryPromptMode.Manual)
            .Prompt.Should().Be(
                "Recover unsaved changes to Draft? (2 unsaved documents found.)");
    }

    [Fact]
    public void FacadesRetainProductSpecificTextAndFreePBackstageExtension()
    {
        var presentationText = FreeP.App.Compositor.AutosaveRecoveryTextCatalog.Resolve();
        var documentText = FreeW.App.Presentation.Shell.AutosaveRecoveryTextCatalog.Resolve();

        presentationText.NoDocumentsMessage.Should().Be("No unsaved presentations were found.");
        presentationText.BackstageLabel.Should().Be("Recover Unsaved Presentations");
        documentText.NoDocumentsMessage.Should().Be("No unsaved documents were found.");
        FreeP.App.Compositor.AutosaveRecoveryTextCatalog.RequiredResourceKeys
            .Should().Contain("Autosave_Recovery_Backstage_Label");
    }
}
