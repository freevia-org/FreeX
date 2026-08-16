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
    private const string PdfImportMimeType = "application/pdf";

    private readonly IReadOnlyList<IDocumentFileAdapter> _adapters;
    private readonly IReadOnlyList<IDocumentFileAdapter> _pdfImportAdapters;

    public DocumentPersistenceWorkflow(
        IReadOnlyList<IDocumentFileAdapter>? adapters = null,
        IReadOnlyList<IDocumentFileAdapter>? pdfImportAdapters = null)
    {
        _adapters = adapters ?? DocumentFileAdapterCatalog.CreateDefaultAdapters();
        _pdfImportAdapters = pdfImportAdapters ?? DocumentFileAdapterCatalog.CreatePdfImportAdapters();
    }

    public IReadOnlyList<IDocumentFileAdapter> Adapters => _adapters;

    public IReadOnlyList<IDocumentFileAdapter> PdfImportAdapters => _pdfImportAdapters;

    public IReadOnlyList<FileFormatDescriptor> SaveFormats =>
        _adapters.SelectMany(adapter => adapter.Formats).Where(format => format.CanSave).ToArray();

    public IReadOnlyList<DocumentFormatCapabilityRow> BuildFormatCapabilityRows(bool includeXpsExport = true) =>
        DocumentFormatCapabilityPlanner.BuildCapabilities(
            _adapters.SelectMany(adapter => adapter.Formats),
            _pdfImportAdapters.SelectMany(adapter => adapter.Formats),
            DocumentFormatCapabilityPlanner.BuildFixedLayoutExportFormats(includeXpsExport));

    public bool CanOpenPath(string path) =>
        DocumentFileFormatResolver.FindOpenAdapter(
            _adapters,
            FilePathPolicy.GetExtensionOrEmpty(path),
            out _) is not null;

    public bool TryGetSaveFormat(string extension, out FileFormatDescriptor? format) =>
        DocumentFileFormatResolver.FindSaveAdapter(_adapters, extension, out format) is not null;

    public bool TryGetSaveFormat(int filterIndex, out FileFormatDescriptor? format)
    {
        format = SaveFormats.ElementAtOrDefault(filterIndex - 1);
        return format is not null;
    }

    public FileOpenDialogPlan BuildOpenDialogPlan(string allSupportedName = DocumentFileDialogRequestPlanner.AllSupportedDocumentsName) =>
        DocumentFileDialogRequestPlanner.BuildOpenDialogPlan(_adapters, allSupportedName);

    public FileOpenDialogPlan BuildPdfImportDialogPlan(string allSupportedName = "PDF documents") =>
        DocumentFileDialogRequestPlanner.BuildOpenDialogPlan(_pdfImportAdapters, allSupportedName);

    public FileOpenPickerPlan BuildPdfImportPickerPlan()
    {
        var plan = DocumentFileDialogRequestPlanner.BuildOpenPickerPlan(_pdfImportAdapters);
        // The command already scopes the picker to PDF, so omit the redundant aggregate row.
        return new FileOpenPickerPlan(
            plan.FileTypes
                .Skip(1)
                .Select(type => type with { MimeTypes = [PdfImportMimeType] })
                .ToArray());
    }

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

        var extension = FilePathPolicy.GetExtensionOrEmpty(path);
        var adapter = DocumentFileFormatResolver.FindOpenAdapter(_adapters, extension, out var format)
            ?? throw new InvalidOperationException($"FreeW has no reader for \"{extension}\" files.");

        using var stream = File.OpenRead(path);
        var document = adapter.Load(stream);
        LinkedImagePreviewResolver.ResolveLocalPreviews(document, path);
        var opensAsTemplate = format?.OpensAsTemplate == true;
        var savedPath = opensAsTemplate ? null : path;
        // r137-freew-persistence-external-modification: capture the write time we observed at open
        // so a later Save can detect a second writer (another FreeW/Word instance, a sync client, a
        // colleague on a shared path) changing the file in between -- see Save's own comment and
        // DocumentExternallyModifiedException. A template open starts life as a brand-new, not-yet-
        // saved document (SavedPath is already null for the same reason), so there is no "source"
        // file to compare a future save against here.
        var sourceLastWriteTimeUtc = opensAsTemplate ? (DateTime?)null : File.GetLastWriteTimeUtc(path);
        return new DocumentOpenResult(document, savedPath, opensAsTemplate, adapter, format, sourceLastWriteTimeUtc);
    }

    public DocumentSnapshotOpenResult OpenSnapshot(string snapshotPath, string? originalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);

        using var stream = File.OpenRead(snapshotPath);
        var document = DocxReader.Read(stream);
        LinkedImagePreviewResolver.ResolveLocalPreviews(document, originalPath ?? snapshotPath);
        return new DocumentSnapshotOpenResult(document, originalPath);
    }

    public DocumentImportResult ImportPdfText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var extension = FilePathPolicy.GetExtensionOrEmpty(path);
        var adapter = DocumentFileFormatResolver.FindOpenAdapter(_pdfImportAdapters, extension, out var format)
            ?? throw new InvalidOperationException(
                $"FreeW can import text only from \".pdf\" files, not \"{extension}\".");

        using var stream = File.OpenRead(path);
        return new DocumentImportResult(adapter.Load(stream), adapter, format);
    }

    public bool TryResolveCurrentSaveTarget(string path, out DocumentSaveTarget target) =>
        TryResolveSaveTarget(path, filterIndex: 0, out target);

    public bool TryResolveSaveTarget(string path, int filterIndex, out DocumentSaveTarget target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var chosenExtension = FilePathPolicy.GetExtensionOrEmpty(path);
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

        var selectedFormat = SaveFormats.ElementAtOrDefault(filterIndex - 1);
        var format = selectedFormat is not null && AdapterOwnsFormat(adapter, selectedFormat)
            ? selectedFormat
            : DocumentFileFormatResolver.FindSaveAdapter(_adapters, chosenExtension, out var fallbackFormat) is not null
                ? fallbackFormat
                : null;
        target = new DocumentSaveTarget(path, adapter, format);
        return true;
    }

    public DocumentSaveCompatibilityPlan BuildSaveCompatibilityPlan(TextDocument document, DocumentSaveTarget target) =>
        DocumentSaveCompatibilityPlanner.Build(document, target);

    public void Save(TextDocument document, DocumentSaveTarget target, DateTime? expectedLastWriteTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);

        // r137-freew-persistence-external-modification: port of FreeX's WorkbookSaveService.SaveAsync
        // check. FreeW previously had NO external-modification detection at all (unlike FreeX): if the
        // caller passes the write time it captured at open (DocumentOpenResult.SourceLastWriteTimeUtc)
        // and the file on disk now has a different write time, someone else (another FreeW/Word
        // instance, a sync client, a colleague on a shared path) changed it since we read it, and
        // writing over it here would silently discard their changes with zero warning. This is a
        // best-effort check-then-act (not a held file lock, matching FreeX's own caveat), but it
        // catches the common "someone else saved while I was still editing" case. Callers that don't
        // pass expectedLastWriteTimeUtc (the default) get the pre-existing unchecked behavior.
        if (expectedLastWriteTimeUtc is { } expectedWriteTimeUtc &&
            File.Exists(target.Path) &&
            File.GetLastWriteTimeUtc(target.Path) != expectedWriteTimeUtc)
        {
            throw new DocumentExternallyModifiedException(target.Path);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(target.Path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var temporaryFile = AtomicFileWriter.CreateTempLease(target.Path);
        using (var stream = temporaryFile.OpenWrite())
            target.Adapter.Save(document, stream);

        AtomicFileWriter.ReplaceTarget(temporaryFile.Path, target.Path);
        temporaryFile.Commit();
    }

    private static string ResolveSaveExtension(string? currentPath, string? preferredExtension)
    {
        var normalizedPreferred = DocumentFileFormatResolver.NormalizeExtension(preferredExtension ?? string.Empty);
        if (normalizedPreferred.Length > 0)
            return normalizedPreferred;

        return FilePathPolicy.TryGetExtension(currentPath, out var currentExtension)
            ? currentExtension
            : DefaultSaveExtension;
    }

    private static bool AdapterOwnsFormat(IDocumentFileAdapter adapter, FileFormatDescriptor selectedFormat) =>
        adapter.Formats.Any(format =>
            format.CanSave &&
            string.Equals(format.FormatName, selectedFormat.FormatName, StringComparison.Ordinal) &&
            string.Equals(
                DocumentFileFormatResolver.NormalizeExtension(format.Extension),
                DocumentFileFormatResolver.NormalizeExtension(selectedFormat.Extension),
                StringComparison.OrdinalIgnoreCase));
}

public sealed record DocumentOpenResult(
    TextDocument Document,
    string? SavedPath,
    bool OpenedAsTemplate,
    IDocumentFileAdapter Adapter,
    FileFormatDescriptor? Format,
    DateTime? SourceLastWriteTimeUtc = null);

public sealed record DocumentSnapshotOpenResult(TextDocument Document, string? TargetPath);

public sealed record DocumentImportResult(
    TextDocument Document,
    IDocumentFileAdapter Adapter,
    FileFormatDescriptor? Format);

public sealed record DocumentSaveTarget(
    string Path,
    IDocumentFileAdapter Adapter,
    FileFormatDescriptor? Format);

/// <summary>
/// Thrown by <see cref="DocumentPersistenceWorkflow.Save"/> when the caller passed the file's write
/// time from open (<c>expectedLastWriteTimeUtc</c>, sourced from
/// <see cref="DocumentOpenResult.SourceLastWriteTimeUtc"/>) and the target file on disk has since
/// been modified by someone else -- a second FreeW/Word instance, or a colleague on a shared path.
/// Hosts should catch this the same way FreeX's hosts catch
/// <c>WorkbookExternallyModifiedException</c> and prompt the user (overwrite anyway / reload /
/// save-as) instead of silently clobbering the other writer's changes.
/// </summary>
public sealed class DocumentExternallyModifiedException(string path)
    : Exception($"'{path}' was modified by another program since it was opened.")
{
    public string Path { get; } = path;
}
