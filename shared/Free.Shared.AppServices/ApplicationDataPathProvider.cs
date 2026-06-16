namespace Free.Shared.AppServices;

public interface IApplicationDataPathProvider
{
    string GetApplicationDataDirectory();
}

public sealed class PlatformApplicationDataPathProvider : IApplicationDataPathProvider
{
    private readonly Func<string> _applicationDataPathProvider;
    private readonly Func<bool> _isMacOsProvider;
    private readonly Func<string> _userProfilePathProvider;

    public PlatformApplicationDataPathProvider()
        : this(
            OperatingSystem.IsMacOS,
            () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            () => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
    {
    }

    public PlatformApplicationDataPathProvider(
        Func<bool> isMacOsProvider,
        Func<string> userProfilePathProvider,
        Func<string> applicationDataPathProvider)
    {
        _isMacOsProvider = isMacOsProvider ?? throw new ArgumentNullException(nameof(isMacOsProvider));
        _userProfilePathProvider = userProfilePathProvider ?? throw new ArgumentNullException(nameof(userProfilePathProvider));
        _applicationDataPathProvider = applicationDataPathProvider ?? throw new ArgumentNullException(nameof(applicationDataPathProvider));
    }

    public static PlatformApplicationDataPathProvider Instance { get; } = new();

    public static PlatformApplicationDataPathProvider LocalInstance { get; } =
        new(
            OperatingSystem.IsMacOS,
            () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public string GetApplicationDataDirectory()
    {
        if (_isMacOsProvider())
        {
            var userProfile = _userProfilePathProvider();
            if (!string.IsNullOrWhiteSpace(userProfile))
                return System.IO.Path.Combine(userProfile, "Library", "Application Support");
        }

        return _applicationDataPathProvider();
    }
}
