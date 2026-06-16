namespace Free.Shared.AppServices;

public static class AppStoragePathPlanner
{
    /// <summary>App-specific data folder name, sourced from the ambient <see cref="AppProduct"/>.</summary>
    public static string ProductDirectoryName => AppProduct.Current.ProductDirectoryName;
    public const string DiagnosticsDirectoryName = "Diagnostics";
    public const string OptionsFileName = "options.json";

    /// <summary>App-specific env var that disables local diagnostics, from the ambient <see cref="AppProduct"/>.</summary>
    public static string DisableDiagnosticsEnvironmentVariable => AppProduct.Current.DiagnosticsEnvironmentVariable;

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

        var productName = AppProduct.Current.ProductName;
        return $"{productName} writes local usage events and crash files to {diagnosticsDirectory}. These files stay on this computer unless you attach them to an issue. Crash exception messages and stack traces can occasionally contain sensitive values, so review files before sharing them. Start {productName} with {DisableDiagnosticsEnvironmentVariable}=0 to disable local diagnostics for that run.";
    }
}
