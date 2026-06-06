using FreeX.Core.IO;

namespace FreeX.App.Services;

public sealed record WorkbookStartupSmokeResult(bool Success, string Message)
{
    public int ExitCode => Success ? 0 : 1;
}

public sealed class WorkbookStartupSmokeService
{
    private const double SmokeViewportHeight = 240;
    private const double SmokeViewportWidth = 320;

    private readonly StartupWorkbookLoader _loader;
    private readonly WorkbookSessionFactory _sessionFactory;

    public WorkbookStartupSmokeService(
        StartupWorkbookLoader? loader = null,
        WorkbookSessionFactory? sessionFactory = null)
    {
        _loader = loader ?? new StartupWorkbookLoader();
        _sessionFactory = sessionFactory ?? new WorkbookSessionFactory();
    }

    public WorkbookStartupSmokeResult Run(IReadOnlyList<string> startupArguments)
    {
        try
        {
            var expectedPath = startupArguments.FirstOrDefault(argument => !string.IsNullOrWhiteSpace(argument));
            if (expectedPath is not null && !File.Exists(expectedPath))
                return new WorkbookStartupSmokeResult(false, $"Packaging smoke failed: file not found: {expectedPath}");

            var source = _loader.Load(startupArguments);
            if (expectedPath is not null &&
                (source.IsFallback ||
                 string.IsNullOrWhiteSpace(source.SourcePath) ||
                 !PathsMatch(expectedPath, source.SourcePath)))
            {
                return new WorkbookStartupSmokeResult(false, $"Packaging smoke failed: requested file was not opened: {expectedPath}");
            }

            var session = _sessionFactory.Create(source, SmokeViewportHeight, SmokeViewportWidth);

            if (session.Workbook.Sheets.Count == 0)
                return new WorkbookStartupSmokeResult(false, "Packaging smoke failed: workbook has no sheets.");
            if (session.Viewport.RowMetrics.Count == 0 || session.Viewport.ColMetrics.Count == 0)
                return new WorkbookStartupSmokeResult(false, "Packaging smoke failed: viewport is empty.");

            return new WorkbookStartupSmokeResult(
                true,
                $"Packaging smoke opened {session.DisplayName} on {session.ActiveSheet.Name} with {session.Viewport.RowMetrics.Count} rows and {session.Viewport.ColMetrics.Count} columns.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or WorkbookTooLargeException)
        {
            return new WorkbookStartupSmokeResult(false, $"Packaging smoke failed: {ex.Message}");
        }
    }

    private static bool PathsMatch(string expectedPath, string actualPath)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(expectedPath),
                Path.GetFullPath(actualPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

public static class PackagingSmokeCommand
{
    public const string Argument = "--packaging-smoke";

    public static bool TryRun(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!args.Any(arg => string.Equals(arg, Argument, StringComparison.OrdinalIgnoreCase)))
        {
            exitCode = 0;
            return false;
        }

        var startupArguments = args
            .Where(arg => !string.Equals(arg, Argument, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var result = new WorkbookStartupSmokeService().Run(startupArguments);
        var writer = result.Success ? output : error;
        writer.WriteLine(result.Message);
        exitCode = result.ExitCode;
        return true;
    }
}
