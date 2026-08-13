namespace Free.Shared.AppServices;

public enum SisterAppPackagingSmokeOutputTarget
{
    StandardOutput,
    StandardError,
}

public sealed record SisterAppPackagingSmokeResult(
    int ExitCode,
    SisterAppPackagingSmokeOutputTarget OutputTarget,
    string ConsoleOutput,
    string? ReportContent = null);

public delegate SisterAppPackagingSmokeResult SisterAppPackagingSmokeBody(
    IReadOnlyList<string> startupArguments);

public delegate SisterAppPackagingSmokeResult SisterAppPackagingSmokeExceptionHandler(
    Exception exception);

/// <summary>
/// Shared command envelope for sister-app packaging smoke lanes.
/// </summary>
public static class SisterAppPackagingSmoke
{
    public const string Argument = "--packaging-smoke";

    public static bool TryRun(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        SisterAppPackagingSmokeBody execute,
        out int exitCode) =>
        TryRun(args, output, error, execute, exceptionHandler: null, out exitCode);

    public static bool TryRun(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        SisterAppPackagingSmokeBody execute,
        SisterAppPackagingSmokeExceptionHandler? exceptionHandler,
        out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(execute);

        if (!HasArgument(args))
        {
            exitCode = 0;
            return false;
        }

        var reportPath = FindReportPath(args);
        try
        {
            var result = execute(RemoveArgumentTokens(args))
                ?? throw new InvalidOperationException("Packaging smoke body returned no result.");
            ApplyResult(result, reportPath, output, error);
            exitCode = result.ExitCode;
        }
        catch (Exception exception) when (exceptionHandler is not null)
        {
            var result = exceptionHandler(exception)
                ?? throw new InvalidOperationException("Packaging smoke exception handler returned no result.");
            ApplyResult(result, reportPath, output, error);
            exitCode = result.ExitCode;
        }

        return true;
    }

    private static void ApplyResult(
        SisterAppPackagingSmokeResult result,
        string? reportPath,
        TextWriter output,
        TextWriter error)
    {
        WriteReport(reportPath, result.ReportContent, error);
        var writer = result.OutputTarget switch
        {
            SisterAppPackagingSmokeOutputTarget.StandardOutput => output,
            SisterAppPackagingSmokeOutputTarget.StandardError => error,
            _ => throw new InvalidOperationException(
                $"Unsupported packaging smoke output target '{result.OutputTarget}'."),
        };
        writer.Write(result.ConsoleOutput);
        writer.Flush();
    }

    public static bool HasArgument(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.Any(IsPackagingSmokeArgument);
    }

    public static string[] RemoveArgumentTokens(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.Where(arg => !IsPackagingSmokeArgument(arg)).ToArray();
    }

    public static string? FindReportPath(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (var i = 0; i < args.Count - 1; i++)
        {
            if (IsPackagingSmokeArgument(args[i]))
                return args[i + 1];
        }

        return null;
    }

    public static void WriteReport(string? path, string? content, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (string.IsNullOrWhiteSpace(path) || content is null)
            return;

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, content);
        }
        catch (Exception ex)
        {
            error.WriteLine($"packaging-smoke: failed to write report: {ex.Message}");
        }
    }

    private static bool IsPackagingSmokeArgument(string arg) =>
        string.Equals(arg, Argument, StringComparison.OrdinalIgnoreCase);
}
