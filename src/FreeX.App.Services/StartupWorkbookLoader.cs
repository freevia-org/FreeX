using FreeX.Core.IO;

namespace FreeX.App.Services;

public sealed class StartupWorkbookLoader
{
    private readonly IReadOnlyList<IFileAdapter> _adapters;
    private readonly Func<string, bool, StartupWorkbookLoadResult> _fallbackFactory;

    public StartupWorkbookLoader(
        IEnumerable<IFileAdapter>? adapters = null,
        Func<string, bool, StartupWorkbookLoadResult>? fallbackFactory = null)
    {
        _adapters = (adapters ?? WorkbookFileAdapterCatalog.CreateDefaultAdapters()).ToList();
        _fallbackFactory = fallbackFactory ?? PortPreviewWorkbookFactory.Create;
    }

    public StartupWorkbookLoadResult Load(IReadOnlyList<string> startupArguments)
    {
        var filePath = startupArguments.FirstOrDefault(argument =>
            !string.IsNullOrWhiteSpace(argument) &&
            File.Exists(argument));

        if (filePath is null)
            return _fallbackFactory("Showing sample workbook.", false);

        var extension = Path.GetExtension(filePath);
        var adapter = FileFormatResolver.FindOpenAdapter(_adapters, extension, out _);
        if (adapter is null)
            return _fallbackFactory($"Unsupported file type: {extension}.", true);

        try
        {
            using var stream = File.OpenRead(filePath);
            var workbook = adapter.Load(stream);
            var displayName = Path.GetFileNameWithoutExtension(filePath);
            workbook.Name = Path.GetFileName(filePath);
            WorkbookOpenNormalizer.ApplyTextWorkbookSheetName(workbook, extension, displayName);

            return new StartupWorkbookLoadResult(
                workbook,
                workbook.Name,
                $"Opened {extension}.",
                IsFallback: false,
                SourcePath: filePath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        {
            return _fallbackFactory($"Open failed: {ex.Message}", true);
        }
    }
}
