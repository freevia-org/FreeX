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
    bool SuppressRecentFiles);

public sealed record PresentationFileSaveResult(
    string SavedPath,
    bool SuppressRecentFiles);

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
            SuppressRecentFiles: false);
    }

    public static PresentationFileSaveResult Save(string path, Presentation presentation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(presentation);

        ExportAtomicWriter.WriteAllBytes(path, SerializePresentation(path, presentation));
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
