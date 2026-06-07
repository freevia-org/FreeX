namespace FreeX.App.Services;

public sealed record AppDiagnosticsOptions(string DiagnosticsDirectory, bool IsEnabled)
{
    public static AppDiagnosticsOptions CreateDefault() =>
        CreateDefault(PlatformApplicationDataPathProvider.LocalInstance);

    public static AppDiagnosticsOptions CreateDefault(IApplicationDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);

        var disabled = string.Equals(
            Environment.GetEnvironmentVariable(AppStoragePathPlanner.DisableDiagnosticsEnvironmentVariable),
            "0",
            StringComparison.OrdinalIgnoreCase);

        return new AppDiagnosticsOptions(
            AppStoragePathPlanner.GetDiagnosticsDirectory(pathProvider),
            IsEnabled: !disabled);
    }
}
