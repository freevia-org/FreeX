namespace FreeX.App.Services;

public static class AppStoragePathPlanner
{
    public const string ProductDirectoryName = "FreeX";
    public const string DiagnosticsDirectoryName = "Diagnostics";
    public const string OptionsFileName = "options.json";
    public const string DisableDiagnosticsEnvironmentVariable = "FREEX_DIAGNOSTICS";

    public static string GetDiagnosticsDirectory(IAppDiagnosticsPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);

        return pathProvider.GetDiagnosticsDirectory();
    }

    public static string GetOptionsFilePath(IApplicationDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);

        return Path.Combine(
            pathProvider.GetApplicationDataDirectory(),
            ProductDirectoryName,
            OptionsFileName);
    }

    public static string ResolveOptionsFilePath(
        IApplicationDataPathProvider pathProvider,
        string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        return GetOptionsFilePath(pathProvider);
    }

    public static string BuildLocalDiagnosticsNotice(IAppDiagnosticsPathProvider pathProvider) =>
        BuildLocalDiagnosticsNotice(GetDiagnosticsDirectory(pathProvider));

    public static string BuildLocalDiagnosticsNotice(string diagnosticsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticsDirectory);

        return $"FreeX writes local usage events and crash files to {diagnosticsDirectory}. These files stay on this computer unless you attach them to an issue. Crash exception messages and stack traces can occasionally contain sensitive values, so review files before sharing them. Start FreeX with {DisableDiagnosticsEnvironmentVariable}=0 to disable local diagnostics for that run.";
    }
}
