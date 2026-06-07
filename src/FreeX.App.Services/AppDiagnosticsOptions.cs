namespace FreeX.App.Services;

public sealed record AppDiagnosticsOptions(string DiagnosticsDirectory, bool IsEnabled)
{
    public static AppDiagnosticsOptions CreateDefault() =>
        CreateDefault(PlatformAppDiagnosticsPathProvider.Instance);

    public static AppDiagnosticsOptions CreateDefault(IAppDiagnosticsPathProvider pathProvider)
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
