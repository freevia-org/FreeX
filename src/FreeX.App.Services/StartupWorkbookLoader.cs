using Free.Shared.IO;
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
        var openablePaths = EnumerateOpenableFilePaths(startupArguments, out var firstUnsupportedExtension);
        if (openablePaths.Count > 0)
        {
            var filePath = openablePaths[0];
            var extension = Path.GetExtension(filePath);
            var adapter = FileFormatResolver.FindOpenAdapter(_adapters, extension, out var format);
            return Load(filePath, extension, adapter!, format!);
        }

        return firstUnsupportedExtension is null
            ? _fallbackFactory("Showing sample workbook.", false)
            : _fallbackFactory($"Unsupported file type: {firstUnsupportedExtension}.", true);
    }

    /// <summary>
    /// R133-avalonia-multi-file-startup-args: the startup-argument paths beyond the FIRST one that
    /// resolve to an existing, openable-format file -- i.e. every path <see cref="Load"/> itself did
    /// NOT open into the primary window. Mirrors the WPF host's R118 <c>PlanStartupFileOpens</c>:
    /// launching with more than one file argument (or dragging multiple files onto the taskbar icon,
    /// which the OS delivers as a single process launch with multiple path arguments) must open every
    /// one of them, each in its own window, instead of silently dropping every argument after the
    /// first. Callers open each returned path in its own new window (see <c>App.cs</c>).
    /// </summary>
    public IReadOnlyList<string> ResolveAdditionalOpenableFilePaths(IReadOnlyList<string> startupArguments) =>
        EnumerateOpenableFilePaths(startupArguments, out _).Skip(1).ToArray();

    private IReadOnlyList<string> EnumerateOpenableFilePaths(
        IReadOnlyList<string> startupArguments,
        out string? firstUnsupportedExtension)
    {
        var paths = new List<string>();
        string? firstUnsupported = null;
        foreach (var filePath in startupArguments
                     .Select(argument => LocalFilePath.TryNormalize(argument, out var path) ? path : null)
                     .Where(path => path is not null && File.Exists(path)))
        {
            var extension = FilePathPolicy.GetExtensionOrEmpty(filePath);
            var adapter = FileFormatResolver.FindOpenAdapter(_adapters, extension, out var format);
            if (adapter is null || format is null)
            {
                firstUnsupported ??= extension;
                continue;
            }

            paths.Add(filePath!);
        }

        firstUnsupportedExtension = firstUnsupported;
        return paths;
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
