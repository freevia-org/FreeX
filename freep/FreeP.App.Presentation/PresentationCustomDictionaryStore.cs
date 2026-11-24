namespace FreeP.App.Compositor;

/// <summary>
/// Neutral FreeP persistence for the reviewer's custom spelling dictionary, so a word added via "Add to
/// Dictionary" survives closing the presentation and restarting the app instead of living only in the
/// in-memory <see cref="PresentationProofingDictionaryState"/> for the lifetime of the
/// <c>PresentationReviewWorkflowSession</c>. The file format, case-insensitive matching and atomic
/// persistence are shared with FreeW through <see cref="CustomDictionaryFileStore"/>.
///
/// <para>
/// A null <see cref="CustomDictionaryFileStore.DictionaryPath"/> (the default used by tests) makes every
/// operation an in-memory no-op, so unit tests never touch the real user data folder unless they opt in
/// with <see cref="Load"/>.
/// </para>
/// </summary>
public sealed class PresentationCustomDictionaryStore : CustomDictionaryFileStore
{
    public const string FileName = DefaultFileName;

    public PresentationCustomDictionaryStore(
        string? storePath,
        IAtomicLineSetFileSystem? fileSystem = null)
        : base(storePath, fileSystem)
    {
    }

    /// <summary>The default disk-backed store, rooted at the ambient product's app-data folder.</summary>
    public static PresentationCustomDictionaryStore Load() => new(ResolveDefaultPath(FileName));
}
