using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class FileOperationCancellationOwnershipTests
{
    [Fact]
    public void FreeX_hosts_delegate_file_operation_cancellation_lifetime_to_shared_session()
    {
        var wpfMain = Read("src", "FreeX.App.Host", "MainWindow.xaml.cs");
        var wpfBackstage = Read("src", "FreeX.App.Host", "MainWindow.Backstage.cs");
        var avaloniaMain = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var avaloniaLifecycle = Read(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.WindowManagement.cs");

        foreach (var source in new[] { wpfMain, avaloniaMain })
        {
            source.Should().Contain(
                "private readonly FileOperationCancellationSession _fileOperationCancellationSession = new();");
            source.Should().NotContain("CancellationTokenSource");
        }

        foreach (var source in new[] { wpfBackstage, avaloniaMain })
        {
            source.Should().Contain("_fileOperationCancellationSession.Begin()");
            source.Should().Contain("_fileOperationCancellationSession.CancelCurrent()");
            source.Should().Contain("operationCancellation.Token");
            source.Should().NotContain("BeginFileOperationCancellation");
            source.Should().NotContain("ClearFileOperationCancellation");
        }

        wpfMain.Should().Contain("_fileOperationCancellationSession.Dispose();");
        avaloniaLifecycle.Should().Contain("_fileOperationCancellationSession.Dispose();");
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(RepositoryFileLocator.Find(segments));
}
