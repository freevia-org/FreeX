using Free.Shared.IO;
using Free.Shared.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Shell;

/// <summary>
/// Renderer-neutral FreeW document persistence over the file adapter catalog.
/// Hosts still own native pickers and status/error presentation; this workflow owns adapter resolution,
/// template/open-as-copy path decisions, save target planning, and atomic document writes.
/// </summary>
public sealed class DocumentPersistenceWorkflow
{
    public const string DefaultSaveExtension = ".docx";
    public const string DefaultFallbackDisplayName = "Document";

    private readonly IReadOnlyList<IDocumentFileAdapter> _adapters;

    public DocumentPersistenceWorkflow(IReadOnlyList<IDocumentFileAdapter>? adapters = null)
    {
        _adapters = adapters ?? DocumentFileAdapterCatalog.CreateDefaultAdapters();
    }

    public IReadOnlyList<IDocumentFileAdapter> Adapters => _adapters;

    public IReadOnlyList<FileFormatDescriptor> SaveFormats =>
        _adapters.SelectMany(adapter => adapter.Formats).Where(format => format.CanSave).ToArray();

    public bool CanOpenPath(string path) =>
        DocumentFileFormatResolver.FindOpenAdapter(_adapters, Path.GetExtension(path), out _) is not null;

    public bool TryGetSaveFormat(string extension, out FileFormatDescriptor? format) =>
        DocumentFileFormatResolver.FindSaveAdapter(_adapters, extension, out format) is not null;

    public FileOpenDialogPlan BuildOpenDialogPlan(string allSupportedName = DocumentFileDialogRequestPlanner.AllSupportedDocumentsName) =>
        DocumentFileDialogRequestPlanner.BuildOpenDialogPlan(_adapters, allSupportedName);

    public FileSaveDialogPlan BuildSaveDialogPlan(
        string? currentPath,
        string? currentFileName,
        string? suggestedFileName = null,
        string? preferredExtension = null,
        string fallbackDisplayName = DefaultFallbackDisplayName)
    {
        var defaultExtension = ResolveSaveExtension(currentPath, preferredExtension);
        return DocumentFileDialogRequestPlanner.BuildSaveDialogPlanFromSourceName(
            _adapters,
            string.IsNullOrWhiteSpace(suggestedFileName) ? currentFileName : suggestedFileName,
            fallbackDisplayName,
            defaultExtension);
    }

    public FileSavePickerPlan BuildSavePickerPlan(
        string? currentPath,
        string? currentFileName,
        string fallbackDisplayName,
        string? preferredExtension = null)
    {
        var defaultExtension = ResolveSaveExtension(currentPath, preferredExtension);
        return DocumentFileDialogRequestPlanner.BuildSavePickerPlan(
            _adapters,
            currentFileName,
            fallbackDisplayName,
            defaultExtension,
            preferredExtension);
    }

    public DocumentOpenResult Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var adapter = DocumentFileFormatResolver.FindOpenAdapter(_adapters, Path.GetExtension(path), out var format)
            ?? throw new InvalidOperationException($"FreeW has no reader for \"{Path.GetExtension(path)}\" files.");

        using var stream = File.OpenRead(path);
        var document = adapter.Load(stream);
        var savedPath = format?.OpensAsTemplate == true ? null : path;
        return new DocumentOpenResult(document, savedPath, format?.OpensAsTemplate == true, adapter, format);
    }

    public DocumentSnapshotOpenResult OpenSnapshot(string snapshotPath, string? originalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);

        using var stream = File.OpenRead(snapshotPath);
        return new DocumentSnapshotOpenResult(DocxReader.Read(stream), originalPath);
    }

    public bool TryResolveCurrentSaveTarget(string path, out DocumentSaveTarget target) =>
        TryResolveSaveTarget(path, filterIndex: 0, out target);

    public bool TryResolveSaveTarget(string path, int filterIndex, out DocumentSaveTarget target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var chosenExtension = Path.GetExtension(path);
        var adapter = FileDialogSaveSelectionResolver.ResolveAdapter(
            _adapters,
            static candidate => candidate.Formats,
            static (adapters, extension) => DocumentFileFormatResolver.FindSaveAdapter(adapters, extension, out _),
            chosenExtension,
            filterIndex);
        if (adapter is null)
        {
            target = null!;
            return false;
        }

        DocumentFileFormatResolver.FindSaveAdapter(_adapters, chosenExtension, out var format);
        target = new DocumentSaveTarget(path, adapter, format);
        return true;
    }

    public void Save(TextDocument document, DocumentSaveTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);

        var directory = Path.GetDirectoryName(Path.GetFullPath(target.Path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = ExportAtomicWriter.CreateTempPath(target.Path);
        try
        {
            using (var stream = File.Create(tempPath))
                target.Adapter.Save(document, stream);

            ExportAtomicWriter.ReplaceTarget(tempPath, target.Path);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }

            throw;
        }
    }

    private static string ResolveSaveExtension(string? currentPath, string? preferredExtension)
    {
        var normalizedPreferred = DocumentFileFormatResolver.NormalizeExtension(preferredExtension ?? string.Empty);
        if (normalizedPreferred.Length > 0)
            return normalizedPreferred;

        return string.IsNullOrWhiteSpace(currentPath)
            ? DefaultSaveExtension
            : Path.GetExtension(currentPath);
    }
}

public sealed record DocumentOpenResult(
    TextDocument Document,
    string? SavedPath,
    bool OpenedAsTemplate,
    IDocumentFileAdapter Adapter,
    FileFormatDescriptor? Format);

public sealed record DocumentSnapshotOpenResult(TextDocument Document, string? TargetPath);

public sealed record DocumentSaveTarget(
    string Path,
    IDocumentFileAdapter Adapter,
    FileFormatDescriptor? Format);
