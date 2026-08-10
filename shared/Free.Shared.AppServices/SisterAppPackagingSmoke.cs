namespace Free.Shared.AppServices;

/// <summary>
/// Shared command-line and report helpers for sister-app packaging smoke lanes.
/// </summary>
public static class SisterAppPackagingSmoke
{
    public const string Argument = "--packaging-smoke";

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

    public static void WriteReport(string? path, string content, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(error);

        if (string.IsNullOrWhiteSpace(path))
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
