using System.Text;
using Free.Shared.IO;
using Free.Shared.Shell;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationFilePersistenceFormat
{
    PowerPoint,
    LegacyFxp
}

public sealed record PresentationFileOpenResult(
    Presentation Presentation,
    string? SavedPath,
    bool SuppressRecentFiles,
    // r137-remediation2: the write time observed on SavedPath at open, threaded back into a later
    // Save's expectedLastWriteTimeUtc so it can detect another writer having changed the file since
    // -- see Save's own comment and PresentationExternallyModifiedException.
    DateTime? SourceLastWriteTimeUtc = null);

public sealed record PresentationFileSaveResult(
    string SavedPath,
    bool SuppressRecentFiles);

/// <summary>
/// Thrown by <see cref="PresentationFilePersistenceWorkflow.Save"/> when the caller passed the
/// write time it observed on the target path (<c>expectedLastWriteTimeUtc</c>, sourced from
/// <see cref="PresentationFileOpenResult.SourceLastWriteTimeUtc"/>) and the file on disk has since
/// been changed by someone else -- another FreeP instance, a sync client, a colleague on a shared
/// path. Hosts should catch this the same way FreeX's hosts catch
/// <c>WorkbookExternallyModifiedException</c> and prompt the user instead of silently overwriting.
/// </summary>
public sealed class PresentationExternallyModifiedException(string path)
    : Exception($"'{path}' was modified by another program since it was opened.")
{
    public string Path { get; } = path;
}

/// <summary>
/// Renderer-neutral FreeP presentation file workflow. Platform hosts provide picker UI and status text;
/// this type owns the on-disk format choice, package read/write path, and saved-path metadata.
/// </summary>
public static class PresentationFilePersistenceWorkflow
{
    public const string DefaultPresentationExtension = ".pptx";
    public const string MacroEnabledPresentationExtension = ".pptm";
    public const string TemplateExtension = ".potx";
    public const string MacroEnabledTemplateExtension = ".potm";
    public const string SlideShowExtension = ".ppsx";
    public const string MacroEnabledSlideShowExtension = ".ppsm";
    public const string LegacyFxpExtension = ".fxp";

    public static bool IsSupportedPresentationPath(string path)
    {
        var extension = FilePathPolicy.GetExtensionOrEmpty(path);
        return string.Equals(extension, DefaultPresentationExtension, StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, MacroEnabledPresentationExtension, StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, TemplateExtension, StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, MacroEnabledTemplateExtension, StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, SlideShowExtension, StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, MacroEnabledSlideShowExtension, StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, LegacyFxpExtension, StringComparison.OrdinalIgnoreCase);
    }

    public static PresentationFilePersistenceFormat ResolveFormat(string path) =>
        string.Equals(FilePathPolicy.GetExtensionOrEmpty(path), LegacyFxpExtension, StringComparison.OrdinalIgnoreCase)
            ? PresentationFilePersistenceFormat.LegacyFxp
            : PresentationFilePersistenceFormat.PowerPoint;

    public static bool IsLegacyPresentationPath(string path) =>
        ResolveFormat(path) == PresentationFilePersistenceFormat.LegacyFxp;

    public static PresentationFileOpenResult Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var presentation = ResolveFormat(path) switch
        {
            PresentationFilePersistenceFormat.LegacyFxp => FxpFormat.Read(path),
            _ => PptxPackageReader.Read(path),
        };

        // FreeP currently opens editable documents only. If template formats are added later,
        // this is the single place that should switch them to SavedPath = null.
        return new PresentationFileOpenResult(
            presentation,
            SavedPath: path,
            SuppressRecentFiles: false,
            SourceLastWriteTimeUtc: File.GetLastWriteTimeUtc(path));
    }

    public static PresentationFileSaveResult Save(
        string path,
        Presentation presentation,
        DateTime? expectedLastWriteTimeUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(presentation);

        // r137-remediation2: port of FreeX's WorkbookSaveService.SaveAsync / FreeW's
        // DocumentPersistenceWorkflow.Save check. Best-effort check-then-act (not a held file lock,
        // same caveat as those two) -- but it catches the common "someone else saved while I was
        // still editing" case instead of silently discarding their write. Callers that don't pass
        // expectedLastWriteTimeUtc (the default) get the pre-existing unchecked behavior.
        if (expectedLastWriteTimeUtc is { } expectedWriteTimeUtc &&
            File.Exists(path) &&
            File.GetLastWriteTimeUtc(path) != expectedWriteTimeUtc)
        {
            throw new PresentationExternallyModifiedException(path);
        }

        // r138-freep-persistence-modified-metadata: sibling of FreeW's DocumentPersistenceWorkflow.Save
        // fix. PowerPoint refreshes docProps/core.xml's dcterms:modified and cp:lastModifiedBy on every
        // save; PptxPackageWriter just serializes whatever Presentation.Properties already holds, so
        // without a stamp here they stay frozen at creation/open time forever, and the Document
        // Properties dialog / SAVEDATE-style consumers of this same model read the wrong value.
        // Rolled back on any serialize/write failure so a failed save never leaves the in-memory model
        // claiming a save that never reached disk.
        var previousModified = presentation.Properties.Modified;
        var previousLastModifiedBy = presentation.Properties.LastModifiedBy;
        presentation.Properties.Modified = DateTimeOffset.Now;
        presentation.Properties.LastModifiedBy = ResolveLastModifiedByAuthor(presentation.Properties.Author);

        try
        {
            AtomicFileWriter.WriteAllBytes(path, SerializePresentation(path, presentation));
        }
        catch
        {
            presentation.Properties.Modified = previousModified;
            presentation.Properties.LastModifiedBy = previousLastModifiedBy;
            throw;
        }

        return new PresentationFileSaveResult(
            SavedPath: path,
            SuppressRecentFiles: false);
    }

    // Mirrors FreeW's ReviewAuthorIdentityPlanner.ResolveAuthor fallback chain (the same "who is this"
    // identity precedent used elsewhere for authorship in the sister apps): prefer the document's own
    // recorded author, then the OS account name, then a generic default.
    private static string ResolveLastModifiedByAuthor(string? documentAuthor)
    {
        if (!string.IsNullOrWhiteSpace(documentAuthor))
            return documentAuthor.Trim();

        var operatingSystemAuthor = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(operatingSystemAuthor))
            return operatingSystemAuthor.Trim();

        return "FreeP User";
    }

    private static byte[] SerializePresentation(string path, Presentation presentation)
    {
        if (ResolveFormat(path) == PresentationFilePersistenceFormat.LegacyFxp)
            return Encoding.UTF8.GetBytes(FxpFormat.Serialize(presentation));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream, ResolvePackageKind(path));
        return stream.ToArray();
    }

    public static bool IsPowerPointPackagePath(string path) =>
        !string.Equals(FilePathPolicy.GetExtensionOrEmpty(path), LegacyFxpExtension, StringComparison.OrdinalIgnoreCase) &&
        IsSupportedPresentationPath(path);

    public static PresentationPackageKind ResolvePackageKind(string path) =>
        FilePathPolicy.GetExtensionOrEmpty(path).ToLowerInvariant() switch
        {
            MacroEnabledPresentationExtension => PresentationPackageKind.MacroEnabledPresentation,
            TemplateExtension => PresentationPackageKind.Template,
            MacroEnabledTemplateExtension => PresentationPackageKind.MacroEnabledTemplate,
            SlideShowExtension => PresentationPackageKind.SlideShow,
            MacroEnabledSlideShowExtension => PresentationPackageKind.MacroEnabledSlideShow,
            _ => PresentationPackageKind.Presentation,
        };
}
