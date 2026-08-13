using System.Diagnostics;

namespace Free.Shared.AppServices;

/// <summary>Outcome of attempting to open a local file-system path through the desktop shell.</summary>
public enum DesktopPathLaunchOutcome
{
    Launched,
    Missing,
    LauncherUnavailable,
    LaunchFailed
}

/// <summary>Whether the shell should open the item itself or its containing folder.</summary>
public enum DesktopPathLaunchMode
{
    Open,
    Reveal
}

/// <summary>The expected local file-system item kind.</summary>
public enum DesktopPathItemKind
{
    File,
    Directory
}

/// <summary>Desktop family used to construct a native shell-open command.</summary>
public enum DesktopPathPlatform
{
    Windows,
    MacOS,
    Linux,
    Unsupported
}

/// <summary>A normalized, validated local path and the item the shell should open.</summary>
public sealed record DesktopPathLaunchTarget(
    string SourcePath,
    DesktopPathItemKind SourceKind,
    string LaunchPath,
    DesktopPathItemKind LaunchKind,
    DesktopPathLaunchMode Mode)
{
    public Uri LaunchUri => new UriBuilder(Uri.UriSchemeFile, string.Empty)
    {
        Path = LaunchPath
    }.Uri;
}

/// <summary>A typed shell-launch result with enough context for existing host failure UI.</summary>
public sealed record DesktopPathLaunchResult(
    DesktopPathLaunchOutcome Outcome,
    DesktopPathLaunchTarget? Target = null,
    Exception? Error = null);

/// <summary>Injectable file-system boundary for path normalization and existence checks.</summary>
public interface IDesktopPathFileSystem
{
    string GetFullPath(string path);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string? GetDirectoryName(string path);
}

/// <summary>Injectable process boundary for the default desktop shell adapter.</summary>
public interface IDesktopPathProcessLauncher
{
    bool TryStart(ProcessStartInfo startInfo);
}

/// <summary>
/// Shared policy for opening local files and folders. Product hosts retain native UI and lifecycle
/// concerns while this owner normalizes paths, distinguishes files from folders, and builds the
/// platform shell handoff consistently.
/// </summary>
public static class DesktopPathLauncher
{
    private static readonly IDesktopPathFileSystem SystemFileSystem = new PhysicalDesktopPathFileSystem();
    private static readonly IDesktopPathProcessLauncher SystemProcessLauncher = new DesktopShellProcessLauncher();

    public static DesktopPathLaunchResult OpenFile(string path) =>
        OpenFile(path, SystemFileSystem, SystemProcessLauncher, DetectPlatform());

    public static DesktopPathLaunchResult OpenDirectory(string path) =>
        OpenDirectory(path, SystemFileSystem, SystemProcessLauncher, DetectPlatform());

    public static DesktopPathLaunchResult RevealFile(string path) =>
        RevealFile(path, SystemFileSystem, SystemProcessLauncher, DetectPlatform());

    public static DesktopPathLaunchResult OpenFile(
        string path,
        IDesktopPathFileSystem fileSystem,
        IDesktopPathProcessLauncher? processLauncher,
        DesktopPathPlatform platform) =>
        LaunchWithProcess(path, DesktopPathItemKind.File, DesktopPathLaunchMode.Open, fileSystem, processLauncher, platform);

    public static DesktopPathLaunchResult OpenDirectory(
        string path,
        IDesktopPathFileSystem fileSystem,
        IDesktopPathProcessLauncher? processLauncher,
        DesktopPathPlatform platform) =>
        LaunchWithProcess(path, DesktopPathItemKind.Directory, DesktopPathLaunchMode.Open, fileSystem, processLauncher, platform);

    public static DesktopPathLaunchResult RevealFile(
        string path,
        IDesktopPathFileSystem fileSystem,
        IDesktopPathProcessLauncher? processLauncher,
        DesktopPathPlatform platform) =>
        LaunchWithProcess(path, DesktopPathItemKind.File, DesktopPathLaunchMode.Reveal, fileSystem, processLauncher, platform);

    public static Task<DesktopPathLaunchResult> OpenFileAsync(
        string path,
        Func<DesktopPathLaunchTarget, Task<bool>>? launchAsync,
        IDesktopPathFileSystem? fileSystem = null) =>
        LaunchAsync(path, DesktopPathItemKind.File, DesktopPathLaunchMode.Open, fileSystem ?? SystemFileSystem, launchAsync);

    public static Task<DesktopPathLaunchResult> OpenDirectoryAsync(
        string path,
        Func<DesktopPathLaunchTarget, Task<bool>>? launchAsync,
        IDesktopPathFileSystem? fileSystem = null) =>
        LaunchAsync(path, DesktopPathItemKind.Directory, DesktopPathLaunchMode.Open, fileSystem ?? SystemFileSystem, launchAsync);

    public static Task<DesktopPathLaunchResult> RevealFileAsync(
        string path,
        Func<DesktopPathLaunchTarget, Task<bool>>? launchAsync,
        IDesktopPathFileSystem? fileSystem = null) =>
        LaunchAsync(path, DesktopPathItemKind.File, DesktopPathLaunchMode.Reveal, fileSystem ?? SystemFileSystem, launchAsync);

    /// <summary>
    /// Builds the generic file-open command used by lifecycle-aware consumers such as OLE activation.
    /// The caller remains responsible for starting, awaiting, and disposing the process.
    /// </summary>
    public static ProcessStartInfo CreateOpenFileProcessStartInfo(
        string path,
        bool waitForApplicationExit = false,
        DesktopPathPlatform? platform = null,
        IDesktopPathFileSystem? fileSystem = null)
    {
        fileSystem ??= SystemFileSystem;
        var fullPath = fileSystem.GetFullPath(path);
        if (!fileSystem.FileExists(fullPath))
            throw new FileNotFoundException("The desktop shell target does not exist.", fullPath);

        var target = new DesktopPathLaunchTarget(
            fullPath,
            DesktopPathItemKind.File,
            fullPath,
            DesktopPathItemKind.File,
            DesktopPathLaunchMode.Open);
        return CreateProcessStartInfo(target, platform ?? DetectPlatform(), waitForApplicationExit);
    }

    public static ProcessStartInfo CreateProcessStartInfo(
        DesktopPathLaunchTarget target,
        DesktopPathPlatform platform,
        bool waitForApplicationExit = false)
    {
        ArgumentNullException.ThrowIfNull(target);

        switch (platform)
        {
            case DesktopPathPlatform.Windows:
                return new ProcessStartInfo
                {
                    FileName = target.LaunchPath,
                    UseShellExecute = true
                };

            case DesktopPathPlatform.MacOS:
            {
                var info = new ProcessStartInfo
                {
                    FileName = "open",
                    UseShellExecute = false
                };
                if (waitForApplicationExit)
                    info.ArgumentList.Add("-W");
                info.ArgumentList.Add(target.LaunchPath);
                return info;
            }

            case DesktopPathPlatform.Linux:
            {
                var info = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false
                };
                info.ArgumentList.Add(target.LaunchPath);
                return info;
            }

            default:
                throw new PlatformNotSupportedException("No desktop path launcher is available on this platform.");
        }
    }

    public static DesktopPathPlatform DetectPlatform() =>
        OperatingSystem.IsWindows()
            ? DesktopPathPlatform.Windows
            : OperatingSystem.IsMacOS()
                ? DesktopPathPlatform.MacOS
                : OperatingSystem.IsLinux()
                    ? DesktopPathPlatform.Linux
                    : DesktopPathPlatform.Unsupported;

    private static DesktopPathLaunchResult LaunchWithProcess(
        string path,
        DesktopPathItemKind sourceKind,
        DesktopPathLaunchMode mode,
        IDesktopPathFileSystem fileSystem,
        IDesktopPathProcessLauncher? processLauncher,
        DesktopPathPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (!TryResolveTarget(path, sourceKind, mode, fileSystem, out var target, out var failure))
            return failure;

        if (processLauncher is null || platform == DesktopPathPlatform.Unsupported)
            return new DesktopPathLaunchResult(DesktopPathLaunchOutcome.LauncherUnavailable, target);

        try
        {
            var startInfo = CreateProcessStartInfo(target, platform);
            return processLauncher.TryStart(startInfo)
                ? new DesktopPathLaunchResult(DesktopPathLaunchOutcome.Launched, target)
                : new DesktopPathLaunchResult(DesktopPathLaunchOutcome.LaunchFailed, target);
        }
        catch (Exception exception)
        {
            return new DesktopPathLaunchResult(DesktopPathLaunchOutcome.LaunchFailed, target, exception);
        }
    }

    private static async Task<DesktopPathLaunchResult> LaunchAsync(
        string path,
        DesktopPathItemKind sourceKind,
        DesktopPathLaunchMode mode,
        IDesktopPathFileSystem fileSystem,
        Func<DesktopPathLaunchTarget, Task<bool>>? launchAsync)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (!TryResolveTarget(path, sourceKind, mode, fileSystem, out var target, out var failure))
            return failure;

        if (launchAsync is null)
            return new DesktopPathLaunchResult(DesktopPathLaunchOutcome.LauncherUnavailable, target);

        try
        {
            return await launchAsync(target).ConfigureAwait(false)
                ? new DesktopPathLaunchResult(DesktopPathLaunchOutcome.Launched, target)
                : new DesktopPathLaunchResult(DesktopPathLaunchOutcome.LaunchFailed, target);
        }
        catch (Exception exception)
        {
            return new DesktopPathLaunchResult(DesktopPathLaunchOutcome.LaunchFailed, target, exception);
        }
    }

    private static bool TryResolveTarget(
        string path,
        DesktopPathItemKind sourceKind,
        DesktopPathLaunchMode mode,
        IDesktopPathFileSystem fileSystem,
        out DesktopPathLaunchTarget target,
        out DesktopPathLaunchResult failure)
    {
        target = null!;
        failure = null!;

        if (string.IsNullOrWhiteSpace(path))
        {
            failure = new DesktopPathLaunchResult(DesktopPathLaunchOutcome.Missing);
            return false;
        }

        string sourcePath;
        try
        {
            sourcePath = fileSystem.GetFullPath(path.Trim());
        }
        catch (Exception exception)
        {
            failure = new DesktopPathLaunchResult(DesktopPathLaunchOutcome.LaunchFailed, Error: exception);
            return false;
        }

        bool sourceExists;
        try
        {
            sourceExists = sourceKind == DesktopPathItemKind.File
                ? fileSystem.FileExists(sourcePath)
                : fileSystem.DirectoryExists(sourcePath);
        }
        catch (Exception exception)
        {
            failure = new DesktopPathLaunchResult(DesktopPathLaunchOutcome.LaunchFailed, Error: exception);
            return false;
        }

        if (!sourceExists)
        {
            failure = new DesktopPathLaunchResult(DesktopPathLaunchOutcome.Missing);
            return false;
        }

        var launchPath = sourcePath;
        var launchKind = sourceKind;
        if (mode == DesktopPathLaunchMode.Reveal)
        {
            try
            {
                launchPath = fileSystem.GetDirectoryName(sourcePath) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(launchPath) || !fileSystem.DirectoryExists(launchPath))
                {
                    failure = new DesktopPathLaunchResult(DesktopPathLaunchOutcome.Missing);
                    return false;
                }
            }
            catch (Exception exception)
            {
                failure = new DesktopPathLaunchResult(DesktopPathLaunchOutcome.LaunchFailed, Error: exception);
                return false;
            }

            launchKind = DesktopPathItemKind.Directory;
        }

        target = new DesktopPathLaunchTarget(sourcePath, sourceKind, launchPath, launchKind, mode);
        return true;
    }

    private sealed class PhysicalDesktopPathFileSystem : IDesktopPathFileSystem
    {
        public string GetFullPath(string path) => Path.GetFullPath(path);
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);
    }

    private sealed class DesktopShellProcessLauncher : IDesktopPathProcessLauncher
    {
        public bool TryStart(ProcessStartInfo startInfo) => Process.Start(startInfo) is not null;
    }
}
