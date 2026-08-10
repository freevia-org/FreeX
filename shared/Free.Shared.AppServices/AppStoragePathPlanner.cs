namespace Free.Shared.AppServices;

public static class AppStoragePathPlanner
{
    /// <summary>App-specific data folder name, sourced from the ambient <see cref="AppProduct"/>.</summary>
    public static string ProductDirectoryName => AppProduct.Current.ProductDirectoryName;
    public const string DiagnosticsDirectoryName = "Diagnostics";
    public const string OptionsFileName = "options.json";
    public const string RecentColorsFileName = "recent-colors.json";

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
            GetApplicationDataDirectory(pathProvider),
            OptionsFileName);
    }

    public static string GetApplicationDataDirectory(IApplicationDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);

        return Path.Combine(
            pathProvider.GetApplicationDataDirectory(),
            ProductDirectoryName);
    }

    public static string GetApplicationDataDirectoryLabelOrFallback(
        IApplicationDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);

        try
        {
            return GetApplicationDataDirectory(pathProvider);
        }
        catch
        {
            return $"%LOCALAPPDATA%\\{ProductDirectoryName}";
        }
    }

    public static string GetApplicationDataDirectoryLabelOrFallback(
        IApplicationDataPathProvider pathProvider,
        string optionsStorePath)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        ArgumentNullException.ThrowIfNull(optionsStorePath);

        try
        {
            var configuredDirectory = Path.GetDirectoryName(optionsStorePath);
            if (!string.IsNullOrWhiteSpace(configuredDirectory))
                return configuredDirectory;
        }
        catch
        {
            // Fall through to the platform data-directory policy.
        }

        return GetApplicationDataDirectoryLabelOrFallback(pathProvider);
    }

    public static string GetOptionsFilePathLabelOrFallback(IApplicationDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);

        try
        {
            return GetOptionsFilePath(pathProvider);
        }
        catch
        {
            return $"%LOCALAPPDATA%\\{ProductDirectoryName}";
        }
    }

    public static string ResolveOptionsFilePath(
        IApplicationDataPathProvider pathProvider,
        string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        return GetOptionsFilePath(pathProvider);
    }

    public static string GetRecentColorsFilePath(IApplicationDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);

        return Path.Combine(
            pathProvider.GetApplicationDataDirectory(),
            ProductDirectoryName,
            RecentColorsFileName);
    }

    public static string ResolveRecentColorsFilePath(
        IApplicationDataPathProvider pathProvider,
        string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        return GetRecentColorsFilePath(pathProvider);
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
