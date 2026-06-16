namespace Free.Shared.AppServices;

public interface IAppDiagnosticsPathProvider
{
    string GetDiagnosticsDirectory();
}

public sealed class PlatformAppDiagnosticsPathProvider : IAppDiagnosticsPathProvider
{
    private readonly Func<string> _localApplicationDataPathProvider;
    private readonly Func<bool> _isMacOsProvider;
    private readonly Func<string> _userProfilePathProvider;

    public PlatformAppDiagnosticsPathProvider()
        : this(
            OperatingSystem.IsMacOS,
            () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    public PlatformAppDiagnosticsPathProvider(
        Func<bool> isMacOsProvider,
        Func<string> userProfilePathProvider,
        Func<string> localApplicationDataPathProvider)
    {
        _isMacOsProvider = isMacOsProvider ?? throw new ArgumentNullException(nameof(isMacOsProvider));
        _userProfilePathProvider = userProfilePathProvider ?? throw new ArgumentNullException(nameof(userProfilePathProvider));
        _localApplicationDataPathProvider = localApplicationDataPathProvider ?? throw new ArgumentNullException(nameof(localApplicationDataPathProvider));
    }

    public static PlatformAppDiagnosticsPathProvider Instance { get; } = new();

    public string GetDiagnosticsDirectory()
    {
        if (_isMacOsProvider())
        {
            var userProfile = _userProfilePathProvider();
            if (!string.IsNullOrWhiteSpace(userProfile))
                return Path.Combine(userProfile, "Library", "Logs", AppStoragePathPlanner.ProductDirectoryName);
        }

        return Path.Combine(
            _localApplicationDataPathProvider(),
            AppStoragePathPlanner.ProductDirectoryName,
            AppStoragePathPlanner.DiagnosticsDirectoryName);
    }
}
