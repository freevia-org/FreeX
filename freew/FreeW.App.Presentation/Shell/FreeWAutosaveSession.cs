using Free.Shared.AppServices;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Shell;

/// <summary>
/// FreeW's autosave profile: the product name and document noun used in recovery prompts, the
/// DOCX snapshot serializers, and the 30 s cadence. Everything else is the shared
/// <see cref="AutosaveApplicationSession{TDocument}"/> / <see cref="AutosaveRecoveryProfile"/>.
/// </summary>
public static class FreeWAutosaveProfile
{
    public static AutosaveRecoveryProfile Recovery { get; } = new(
        new AutosaveRecoveryPromptText("FreeW", "documents"),
        "a document",
        new AutosaveRecoveryTextDefaults(
            "FreeW - Recover",
            "Recover",
            "Skip",
            "No unsaved documents were found.",
            "Could not recover the document.\n\n{0}"));

    public static AutosaveApplicationProfile<TextDocument> Session { get; } = new(
        Recovery,
        Interval: TimeSpan.FromSeconds(30),
        WriteSnapshot: WriteSnapshot,
        ReadSnapshot: DocxReader.Read);

    /// <summary>
    /// A password-protected document's recovery copy is encrypted with the same password, so
    /// "Encrypt with Password" never leaves a plaintext copy in the autosave folder. Recovering it
    /// asks for the password again (<see cref="DocumentPersistenceWorkflow.OpenSnapshot"/>).
    /// </summary>
    private static void WriteSnapshot(TextDocument document, string path)
    {
        if (string.IsNullOrEmpty(document.EncryptionPassword))
        {
            DocxWriter.Write(document, path);
            return;
        }

        using var stream = File.Create(path);
        new DocxFileAdapter().Save(document, stream);
    }
}

/// <summary>FreeW's autosave session: the shared lifecycle bound to <see cref="FreeWAutosaveProfile"/>.</summary>
public sealed class FreeWAutosaveSession : AutosaveApplicationSession<TextDocument>
{
    public static TimeSpan DefaultInterval => FreeWAutosaveProfile.Session.Interval;

    public FreeWAutosaveSession(AutosaveDocumentPorts<TextDocument> ports)
        : base(FreeWAutosaveProfile.Session, ports)
    {
    }

    public FreeWAutosaveSession(
        AutosaveDocumentPorts<TextDocument> ports,
        AutosaveSnapshotStore store)
        : base(FreeWAutosaveProfile.Session, ports, store)
    {
    }

    public FreeWAutosaveSession(
        AutosaveDocumentPorts<TextDocument> ports,
        AutosaveSnapshotStore store,
        string snapshotId)
        : base(FreeWAutosaveProfile.Session, ports, store, snapshotId)
    {
    }
}
