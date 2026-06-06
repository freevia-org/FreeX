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
        var filePath = startupArguments.FirstOrDefault(argument =>
            !string.IsNullOrWhiteSpace(argument) &&
            File.Exists(argument));

        if (filePath is null)
            return _fallbackFactory("Showing sample workbook.", false);

        var extension = Path.GetExtension(filePath);
        var adapter = FileFormatResolver.FindOpenAdapter(_adapters, extension, out var format);
        if (adapter is null || format is null)
            return _fallbackFactory($"Unsupported file type: {extension}.", true);

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
                LoadWarnings: result.LoadWarnings);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or WorkbookTooLargeException)
        {
            return _fallbackFactory($"Open failed: {ex.Message}", true);
        }
    }
}
