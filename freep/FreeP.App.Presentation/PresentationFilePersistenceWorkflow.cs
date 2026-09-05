using System.Text;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Opc;
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
    DateTime? SourceLastWriteTimeUtc = null,
    // r174-shared-protection-readonly: whether SavedPath cannot be written back to (OS read-only
    // attribute, a read-only share/volume, or a denied ACL). Matches FreeW's
    // DocumentOpenResult.IsFileSystemReadOnly and FreeX's WorkbookReadOnlyOpenPlan flag so callers
    // can indicate the state up front instead of letting the user edit and only then fail the save.
    bool IsFileSystemReadOnly = false,
    // r454: parts that could not be read and were opened blank. Empty for an undamaged file. The
    // reader recovers one bad slide rather than refusing the deck (deliberately -- see its per-slide
    // catch), so this is the only thing standing between a repaired deck and silent data loss the
    // user saves over.
    IReadOnlyList<string>? LoadWarnings = null);

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

    public static bool IsTemplatePresentationPath(string path)
    {
        var extension = FilePathPolicy.GetExtensionOrEmpty(path);
        return string.Equals(extension, TemplateExtension, StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, MacroEnabledTemplateExtension, StringComparison.OrdinalIgnoreCase);
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

        // r174-shared-protection-readonly: FreeP had no read-only check at all, so a presentation
        // on a read-only file opened fully editable with zero indication until the save failed.
        // Port FreeW's/FreeX's check (shared FileWriteRestrictionProbe). This MUST run before the
        // readers below open their own handle on the file: the probe asks for write access, and a
        // read handle already held here turns it into a self-inflicted sharing violation that the
        // probe deliberately reports as "not restricted".
        var isFileSystemReadOnly = FileWriteRestrictionProbe.IsWriteRestricted(path);

        // r454: the .pptx path reads through ReadWithWarnings so a slide that could not be read is
        // reported instead of quietly arriving blank. FxpFormat has no such recovery -- it either
        // reads the whole document or throws -- so it has nothing to report.
        IReadOnlyList<string> loadWarnings = [];
        Presentation presentation;
        if (ResolveFormat(path) == PresentationFilePersistenceFormat.LegacyFxp)
        {
            presentation = FxpFormat.Read(path);
        }
        else
        {
            var read = PptxPackageReader.ReadWithWarnings(path);
            presentation = read.Presentation;
            loadWarnings = read.Warnings;
        }

        // r154: opening a .potx/.potm template must behave like FreeW's DocumentPersistenceWorkflow
        // (opensAsTemplate ? null : path) and FreeX's XltxFileAdapter -- the template file on disk is
        // a master to create FROM, not a document to save back over. IsTemplatePresentationPath below
        // is extension-based (matching the dialog planner's own "PowerPoint templates" filter), not the
        // package's own PresentationPackageKind, so a mislabeled .pptx that happens to carry template
        // content-type internally still opens as a normal saved document -- only the extension the user
        // picked in Open decides this, exactly as FreeW/FreeX key off the chosen file adapter.
        var savedPath = IsTemplatePresentationPath(path) ? null : path;

        return new PresentationFileOpenResult(
            presentation,
            SavedPath: savedPath,
            SuppressRecentFiles: false,
            SourceLastWriteTimeUtc: savedPath is null ? null : File.GetLastWriteTimeUtc(path),
            // A template open never targets this file for a future save (savedPath is null above for
            // the same reason), so its write-restriction state is irrelevant there.
            IsFileSystemReadOnly: savedPath is not null && isFileSystemReadOnly,
            LoadWarnings: loadWarnings);
    }

    public static PresentationFileSaveResult Save(
        string path,
        Presentation presentation,
        DateTime? expectedLastWriteTimeUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(presentation);

        ExternalFileWriteConflictPolicy.ThrowIfChangedSince(
            path,
            expectedLastWriteTimeUtc,
            static conflictingPath => new PresentationExternallyModifiedException(conflictingPath));

        using var saveStamp = DocumentPropertiesSaveStampTransaction.Begin(
            presentation.Properties,
            "FreeP User");
        AtomicFileWriter.WriteAllBytes(path, SerializePresentation(path, presentation));
        saveStamp.Commit();

        return new PresentationFileSaveResult(
            SavedPath: path,
            SuppressRecentFiles: false);
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
