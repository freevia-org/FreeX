using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Proofing;

/// <summary>
/// Neutral FreeW persistence for the user spelling dictionary. The file is a UTF-8, word-per-line
/// <c>.lex</c> store (see <see cref="CustomDictionaryFileStore"/>); shell-specific spell-check
/// registration remains in the WPF host.
/// </summary>
public sealed class CustomDictionaryStore : CustomDictionaryFileStore
{
    public const string FileName = DefaultFileName;

    public CustomDictionaryStore(string? storePath, IAtomicLineSetFileSystem? fileSystem = null)
        : base(storePath, fileSystem)
    {
    }

    public static CustomDictionaryStore Load() => new(ResolveDefaultPath(FileName));
}
