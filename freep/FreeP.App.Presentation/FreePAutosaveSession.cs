using Free.Shared.AppServices;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// FreeP's autosave profile: the product name and presentation noun used in recovery prompts, the
/// PPTX snapshot serializers, and the 60 s cadence. Everything else is the shared
/// <see cref="AutosaveApplicationSession{TDocument}"/> / <see cref="AutosaveRecoveryProfile"/>.
/// </summary>
public static class FreePAutosaveProfile
{
    // FreeP-only extension of the common recovery chrome: the Backstage > Open command label.
    private static readonly ResourceTextDescriptor BackstageLabelText =
        new("Autosave_Recovery_Backstage_Label", "Recover Unsaved Presentations");

    public static AutosaveRecoveryProfile Recovery { get; } = new(
        new AutosaveRecoveryPromptText("FreeP", "presentations"),
        "a presentation",
        new AutosaveRecoveryTextDefaults(
            "FreeP - Recover",
            "Recover",
            "Skip",
            "No unsaved presentations were found.",
            "Could not recover the presentation.\n\n{0}"));

    public static AutosaveApplicationProfile<Presentation> Session { get; } = new(
        Recovery,
        Interval: TimeSpan.FromSeconds(60),
        WriteSnapshot: (presentation, path) => PptxPackageWriter.Write(presentation, path),
        ReadSnapshot: PptxPackageReader.Read);

    /// <summary>Common recovery resource keys plus FreeP's Backstage label key.</summary>
    public static IReadOnlyList<string> RequiredResourceKeys =>
        [.. Recovery.RequiredResourceKeys, BackstageLabelText.ResourceKey];

    public static string ResolveBackstageLabel(Func<string, string?>? getText = null) =>
        BackstageLabelText.Resolve(getText);
}

/// <summary>FreeP's autosave session: the shared lifecycle bound to <see cref="FreePAutosaveProfile"/>.</summary>
public sealed class FreePAutosaveSession : AutosaveApplicationSession<Presentation>
{
    public static TimeSpan DefaultInterval => FreePAutosaveProfile.Session.Interval;

    public FreePAutosaveSession(AutosaveDocumentPorts<Presentation> ports)
        : base(FreePAutosaveProfile.Session, ports)
    {
    }

    public FreePAutosaveSession(
        AutosaveDocumentPorts<Presentation> ports,
        AutosaveSnapshotStore store)
        : base(FreePAutosaveProfile.Session, ports, store)
    {
    }

    public FreePAutosaveSession(
        AutosaveDocumentPorts<Presentation> ports,
        AutosaveSnapshotStore store,
        string snapshotId)
        : base(FreePAutosaveProfile.Session, ports, store, snapshotId)
    {
    }

    /// <summary>Presentation-noun name for <see cref="AutosaveApplicationSession{TDocument}.CompleteDocumentRecovery"/>.</summary>
    public bool CompletePresentationRecovery(
        AutosaveRecoveryPlan plan,
        bool accepted,
        Action<Presentation, string?> applyRecoveredPresentation,
        AutosaveRecoveryRestoreExceptionPolicy exceptionPolicy =
            AutosaveRecoveryRestoreExceptionPolicy.PreserveCandidate) =>
        CompleteDocumentRecovery(plan, accepted, applyRecoveredPresentation, exceptionPolicy);
}
