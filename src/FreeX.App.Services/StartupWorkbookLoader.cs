using FreeX.Core.IO;

namespace FreeX.App.Services;

public sealed class StartupWorkbookLoader
{
    private readonly IReadOnlyList<IFileAdapter> _adapters;
    private readonly Func<string, bool, StartupWorkbookLoadResult> _fallbackFactory;
    private readonly WorkbookOpenService _openService;

    public StartupWorkbookLoader(
        IEnumerable<IFileAdapter>? adapters = null,
        Func<string, bool, StartupWorkbookLoadResult>? fallbackFactory = null,
        WorkbookOpenService? openService = null)
    {
        _adapters = (adapters ?? WorkbookFileAdapterCatalog.CreateDefaultAdapters()).ToList();
        _fallbackFactory = fallbackFactory ?? PortPreviewWorkbookFactory.Create;
        _openService = openService ?? new WorkbookOpenService();
    }

    public StartupWorkbookLoadResult Load(IReadOnlyList<string> startupArguments)
    {
        string? firstUnsupportedExtension = null;
        foreach (var filePath in startupArguments
                     .Select(argument => LocalFilePath.TryNormalize(argument, out var path) ? path : null)
                     .Where(path => path is not null && File.Exists(path)))
        {
            var extension = Path.GetExtension(filePath!);
            var adapter = FileFormatResolver.FindOpenAdapter(_adapters, extension, out var format);
            if (adapter is null || format is null)
            {
                firstUnsupportedExtension ??= extension;
                continue;
            }

            return Load(filePath!, extension, adapter, format);
        }

        return firstUnsupportedExtension is null
            ? _fallbackFactory("Showing sample workbook.", false)
            : _fallbackFactory($"Unsupported file type: {firstUnsupportedExtension}.", true);
    }

    private StartupWorkbookLoadResult Load(
        string filePath,
        string extension,
        IFileAdapter adapter,
        FileFormatDescriptor format)
    {
        try
        {
            var result = _openService
                .LoadAsync(filePath, adapter, extension, format)
                .GetAwaiter()
                .GetResult();
            result.Workbook.Name = Path.GetFileName(filePath);

            return new StartupWorkbookLoadResult(
                result.Workbook,
                result.Workbook.Name,
                $"Opened {extension}.",
                IsFallback: false,
                SourcePath: filePath,
                OpenedAsTemplate: result.OpenedAsTemplate,
                FeatureReport: result.FeatureReport,
                LoadWarnings: result.LoadWarnings,
                SourceFileAccessIdentity: WorkbookFileAccessIdentity.FromLocalPath(filePath));
        }
        // Deliberately broad. This runs at startup — a file-association double-click or a command-line
        // argument — before the shell has any window to host an error dialog, so anything escaping here
        // takes the whole app down before it is usable. The previous filter listed only container/IO
        // failures, but a structurally valid file with corrupt XML inside surfaces as FormatException,
        // XmlException, OverflowException or worse from deep in the parser, and a password-protected
        // workbook throws its own type again. Degrade to the fallback (empty) workbook for every one of
        // them; cancellation still propagates so a cancelled open is not mistaken for a corrupt file.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return _fallbackFactory($"Open failed: {ex.Message}", true);
        }
    }
}
