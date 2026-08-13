using System.Diagnostics;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class DesktopPathLauncherTests
{
    private const string FilePath = "/workspace/output/report.pdf";
    private const string DirectoryPath = "/workspace/output";

    [Fact]
    public void OpenFile_NormalizesAndLaunchesOnlyAnExistingFile()
    {
        var fileSystem = FakeFileSystem.WithFile(FilePath, DirectoryPath);
        var launcher = new RecordingProcessLauncher();

        var result = DesktopPathLauncher.OpenFile(
            " report.pdf ",
            fileSystem,
            launcher,
            DesktopPathPlatform.Windows);

        result.Outcome.Should().Be(DesktopPathLaunchOutcome.Launched);
        result.Target.Should().Be(new DesktopPathLaunchTarget(
            FilePath,
            DesktopPathItemKind.File,
            FilePath,
            DesktopPathItemKind.File,
            DesktopPathLaunchMode.Open));
        result.Target!.LaunchUri.AbsoluteUri.Should().Be("file:///workspace/output/report.pdf");
        launcher.StartInfos.Should().ContainSingle();
        launcher.StartInfos[0].FileName.Should().Be(FilePath);
        launcher.StartInfos[0].UseShellExecute.Should().BeTrue();
    }

    [Fact]
    public void RevealFile_ValidatesTheFileAndLaunchesItsContainingDirectory()
    {
        var fileSystem = FakeFileSystem.WithFile(FilePath, DirectoryPath);
        var launcher = new RecordingProcessLauncher();

        var result = DesktopPathLauncher.RevealFile(
            "report.pdf",
            fileSystem,
            launcher,
            DesktopPathPlatform.MacOS);

        result.Outcome.Should().Be(DesktopPathLaunchOutcome.Launched);
        result.Target!.SourceKind.Should().Be(DesktopPathItemKind.File);
        result.Target.LaunchKind.Should().Be(DesktopPathItemKind.Directory);
        result.Target.LaunchPath.Should().Be(DirectoryPath);
        result.Target.Mode.Should().Be(DesktopPathLaunchMode.Reveal);
        launcher.StartInfos.Should().ContainSingle();
        launcher.StartInfos[0].FileName.Should().Be("open");
        launcher.StartInfos[0].ArgumentList.Should().Equal(DirectoryPath);
    }

    [Fact]
    public void OpenDirectory_DoesNotTreatAnExistingFileAsAFolder()
    {
        var fileSystem = FakeFileSystem.WithFile(FilePath, DirectoryPath);
        var launcher = new RecordingProcessLauncher();

        var result = DesktopPathLauncher.OpenDirectory(
            "report.pdf",
            fileSystem,
            launcher,
            DesktopPathPlatform.Windows);

        result.Outcome.Should().Be(DesktopPathLaunchOutcome.Missing);
        launcher.StartInfos.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("missing.pdf")]
    public void OpenFile_MissingPathDoesNotReachTheLauncher(string? path)
    {
        var launcher = new RecordingProcessLauncher();

        var result = DesktopPathLauncher.OpenFile(
            path!,
            FakeFileSystem.Empty,
            launcher,
            DesktopPathPlatform.Windows);

        result.Outcome.Should().Be(DesktopPathLaunchOutcome.Missing);
        launcher.StartInfos.Should().BeEmpty();
    }

    [Fact]
    public void OpenFile_NoProcessBoundaryReportsUnavailable()
    {
        var result = DesktopPathLauncher.OpenFile(
            "report.pdf",
            FakeFileSystem.WithFile(FilePath, DirectoryPath),
            processLauncher: null,
            platform: DesktopPathPlatform.Windows);

        result.Outcome.Should().Be(DesktopPathLaunchOutcome.LauncherUnavailable);
        result.Target!.LaunchPath.Should().Be(FilePath);
    }

    [Fact]
    public void OpenFile_ProcessRejectionAndExceptionReportFailure()
    {
        var fileSystem = FakeFileSystem.WithFile(FilePath, DirectoryPath);
        var rejected = DesktopPathLauncher.OpenFile(
            "report.pdf",
            fileSystem,
            new RecordingProcessLauncher(shouldStart: false),
            DesktopPathPlatform.Windows);
        var exception = new InvalidOperationException("boom");
        var failed = DesktopPathLauncher.OpenFile(
            "report.pdf",
            fileSystem,
            new RecordingProcessLauncher(error: exception),
            DesktopPathPlatform.Windows);

        rejected.Outcome.Should().Be(DesktopPathLaunchOutcome.LaunchFailed);
        rejected.Error.Should().BeNull();
        failed.Outcome.Should().Be(DesktopPathLaunchOutcome.LaunchFailed);
        failed.Error.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task OpenFileAsync_UsesTheValidatedTargetAndNormalizesOutcomes()
    {
        DesktopPathLaunchTarget? launchedTarget = null;
        var launched = await DesktopPathLauncher.OpenFileAsync(
            "report.pdf",
            target =>
            {
                launchedTarget = target;
                return Task.FromResult(true);
            },
            FakeFileSystem.WithFile(FilePath, DirectoryPath));
        var unavailable = await DesktopPathLauncher.OpenFileAsync(
            "report.pdf",
            launchAsync: null,
            fileSystem: FakeFileSystem.WithFile(FilePath, DirectoryPath));

        launched.Outcome.Should().Be(DesktopPathLaunchOutcome.Launched);
        launchedTarget!.LaunchPath.Should().Be(FilePath);
        unavailable.Outcome.Should().Be(DesktopPathLaunchOutcome.LauncherUnavailable);
    }

    [Fact]
    public void ProcessPlansPreservePlatformShellAndMacWaitSemantics()
    {
        var target = new DesktopPathLaunchTarget(
            FilePath,
            DesktopPathItemKind.File,
            FilePath,
            DesktopPathItemKind.File,
            DesktopPathLaunchMode.Open);

        var windows = DesktopPathLauncher.CreateProcessStartInfo(target, DesktopPathPlatform.Windows);
        var mac = DesktopPathLauncher.CreateProcessStartInfo(
            target,
            DesktopPathPlatform.MacOS,
            waitForApplicationExit: true);
        var linux = DesktopPathLauncher.CreateProcessStartInfo(target, DesktopPathPlatform.Linux);

        windows.FileName.Should().Be(FilePath);
        windows.UseShellExecute.Should().BeTrue();
        mac.FileName.Should().Be("open");
        mac.ArgumentList.Should().Equal("-W", FilePath);
        linux.FileName.Should().Be("xdg-open");
        linux.ArgumentList.Should().Equal(FilePath);
    }

    [Fact]
    public void OpenFileProcessPlanUsesTheValidatedFileSystemSeam()
    {
        var info = DesktopPathLauncher.CreateOpenFileProcessStartInfo(
            "report.pdf",
            platform: DesktopPathPlatform.Linux,
            fileSystem: FakeFileSystem.WithFile(FilePath, DirectoryPath));
        var missing = () => DesktopPathLauncher.CreateOpenFileProcessStartInfo(
            "missing.pdf",
            platform: DesktopPathPlatform.Linux,
            fileSystem: FakeFileSystem.Empty);

        info.FileName.Should().Be("xdg-open");
        info.ArgumentList.Should().Equal(FilePath);
        missing.Should().Throw<FileNotFoundException>();
    }

    private sealed class FakeFileSystem : IDesktopPathFileSystem
    {
        private readonly HashSet<string> _files;
        private readonly HashSet<string> _directories;

        private FakeFileSystem(IEnumerable<string> files, IEnumerable<string> directories)
        {
            _files = files.ToHashSet(StringComparer.Ordinal);
            _directories = directories.ToHashSet(StringComparer.Ordinal);
        }

        public static FakeFileSystem Empty { get; } = new([], []);

        public static FakeFileSystem WithFile(string filePath, string directoryPath) =>
            new([filePath], [directoryPath]);

        public string GetFullPath(string path) => path.Trim() switch
        {
            "report.pdf" => FilePath,
            "output" => DirectoryPath,
            var normalized => normalized
        };

        public bool FileExists(string path) => _files.Contains(path);
        public bool DirectoryExists(string path) => _directories.Contains(path);
        public string? GetDirectoryName(string path) => path == FilePath ? DirectoryPath : null;
    }

    private sealed class RecordingProcessLauncher : IDesktopPathProcessLauncher
    {
        private readonly bool _shouldStart;
        private readonly Exception? _error;

        public RecordingProcessLauncher(bool shouldStart = true, Exception? error = null)
        {
            _shouldStart = shouldStart;
            _error = error;
        }

        public List<ProcessStartInfo> StartInfos { get; } = [];

        public bool TryStart(ProcessStartInfo startInfo)
        {
            StartInfos.Add(startInfo);
            if (_error is not null)
                throw _error;
            return _shouldStart;
        }
    }
}

public sealed class DesktopPathLauncherOwnershipTests
{
    [Fact]
    public void ProductHostsDelegateLocalPathLaunchPolicyToTheSharedOwner()
    {
        var freeWHost = Read("freew", "FreeW.App.Host", "MainWindow.cs");
        var freeWAvalonia = Read("freew", "FreeW.App.Avalonia", "MainWindow.cs");
        var freeXHost = Read("src", "FreeX.App.Host", "MainWindow.PrintExport.cs");
        var freeXAvaloniaOptions = Read("src", "FreeX.App.Avalonia", "MainWindow.ExportOptions.cs");
        var freeXAvaloniaMain = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var freePOle = Read("freep", "FreeP.App.Presentation", "OleActivationService.cs");

        freeWHost.Should().Contain("DesktopPathLauncher.RevealFile(documentPath)");
        freeWHost.Should().NotContain("new ProcessStartInfo(folder)");
        freeWAvalonia.Should().Contain("DesktopPathLauncher.OpenDirectory(folder)");
        freeWAvalonia.Should().NotContain("FileName = folder");
        freeXHost.Should().Contain("DesktopPathLauncher.OpenFile(path)");
        freeXHost.Should().NotContain("Process.Start(new ProcessStartInfo");
        freeXAvaloniaOptions.Should().Contain("DesktopPathLauncher.OpenFileAsync(");
        freeXAvaloniaOptions.Should().NotContain("LaunchUriAsync(new Uri(Path.GetFullPath(path)))");
        freeXAvaloniaMain.Should().Contain("DesktopPathLauncher.RevealFileAsync(");
        freeXAvaloniaMain.Should().NotContain("LaunchDirectoryInfoAsync(new DirectoryInfo(folderPath))");
        freePOle.Should().Contain("DesktopPathLauncher.CreateOpenFileProcessStartInfo(");
        freePOle.Should().NotContain("\"xdg-open\"");
        freePOle.Should().NotContain("new ProcessStartInfo { FileName = path");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(RepositoryFileLocator.Find(parts));
}
