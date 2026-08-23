using System.IO;
using FreeP.App.Ole.Windows;

namespace FreeP.App.Host.Tests;

public sealed class WindowsOleInPlaceEngineTests
{
    [Fact]
    public void CloseAndCommit_CommitsChangedPayloadThenDeletesTemporaryFiles()
    {
        string directory = CreateTempDirectory();
        string sourcePath = Path.Combine(directory, "payload.xlsx");
        byte[] original = [1, 2, 3];
        byte[] changed = [4, 5, 6, 7];
        File.WriteAllBytes(sourcePath, original);
        File.WriteAllBytes(sourcePath + ".stg", [8]);
        byte[]? committed = null;

        try
        {
            using var engine = new WindowsOleInPlaceEngine(
                sourcePath,
                original,
                bytes => committed = bytes);
            File.WriteAllBytes(sourcePath, changed);

            engine.CloseAndCommit();

            committed.Should().Equal(changed);
            File.Exists(sourcePath).Should().BeFalse();
            File.Exists(sourcePath + ".stg").Should().BeFalse();
            engine.IsClosed.Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void CloseAndCommit_DoesNotCommitUnchangedOrEmptyPayload()
    {
        string directory = CreateTempDirectory();
        byte[] original = [1, 2, 3];
        int commits = 0;

        try
        {
            string unchangedPath = Path.Combine(directory, "unchanged.xlsx");
            File.WriteAllBytes(unchangedPath, original);
            using (var unchanged = new WindowsOleInPlaceEngine(
                       unchangedPath,
                       original,
                       _ => commits++))
            {
                unchanged.CloseAndCommit();
            }

            string emptyPath = Path.Combine(directory, "empty.xlsx");
            File.WriteAllBytes(emptyPath, []);
            using (var empty = new WindowsOleInPlaceEngine(
                       emptyPath,
                       original,
                       _ => commits++))
            {
                empty.CloseAndCommit();
            }

            commits.Should().Be(0);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void CloseAndCommit_DeletesTemporaryFilesWhenCommitCallbackFails()
    {
        string directory = CreateTempDirectory();
        string sourcePath = Path.Combine(directory, "payload.docx");
        File.WriteAllBytes(sourcePath, [9, 8, 7]);
        File.WriteAllBytes(sourcePath + ".stg", [6]);

        try
        {
            using var engine = new WindowsOleInPlaceEngine(
                sourcePath,
                [1],
                _ => throw new InvalidOperationException("commit failed"));

            Action close = engine.CloseAndCommit;

            close.Should().NotThrow();
            File.Exists(sourcePath).Should().BeFalse();
            File.Exists(sourcePath + ".stg").Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void CloseAndCommit_IsIdempotent()
    {
        string directory = CreateTempDirectory();
        string sourcePath = Path.Combine(directory, "payload.pptx");
        File.WriteAllBytes(sourcePath, [2]);
        int commits = 0;

        try
        {
            using var engine = new WindowsOleInPlaceEngine(
                sourcePath,
                [1],
                _ => commits++);

            engine.CloseAndCommit();
            engine.CloseAndCommit();

            commits.Should().Be(1);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void TryStart_RejectsMissingHostWindowWithoutEvaluatingSize()
    {
        string directory = CreateTempDirectory();
        string sourcePath = Path.Combine(directory, "payload.xlsx");
        File.WriteAllBytes(sourcePath, [1]);
        bool sizeRequested = false;

        try
        {
            using var engine = new WindowsOleInPlaceEngine(sourcePath, [1], _ => { });

            bool started = engine.TryStart(
                IntPtr.Zero,
                () =>
                {
                    sizeRequested = true;
                    return new OleInPlaceSize(100, 80);
                });

            started.Should().BeFalse();
            sizeRequested.Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void TryCreatePayload_StagesAnOwnedCopyWithRequestedExtension()
    {
        byte[] payload = [3, 1, 4, 1, 5];

        WindowsOleInPlaceEngine.TryCreatePayload(
                "test-ole",
                "xlsx",
                payload,
                _ => { },
                out var engine)
            .Should().BeTrue();
        engine.Should().NotBeNull();

        using (engine)
        {
            engine!.SourcePath.Should().EndWith(".xlsx");
            File.ReadAllBytes(engine.SourcePath).Should().Equal(payload);
        }

        File.Exists(engine!.SourcePath).Should().BeFalse();
    }

    [Fact]
    public void RendererHosts_DelegateNativeOleOwnershipToWindowsEngine()
    {
        string root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        string[] hostPaths =
        [
            Path.Combine(root, "freep", "FreeP.App.Rendering.Wpf", "WpfOleInPlaceHost.cs"),
            Path.Combine(root, "freep", "FreeP.App.Avalonia", "AvaloniaOleInPlaceHost.cs"),
        ];

        foreach (string path in hostPaths)
        {
            string source = File.ReadAllText(path);
            source.Should().Contain("WindowsOleInPlaceEngine")
                .And.Contain("OleActivationService.BuildOleObjectUpdateCallback")
                .And.Contain("OleActivationService.BuildInlineOleObjectUpdateCallback")
                .And.NotContain("BuildCommitCallback")
                .And.NotContain("EmbeddedBytes = bytes")
                .And.NotContain("DllImport(")
                .And.NotContain("class OleSite")
                .And.NotContain("interface IOleObject")
                .And.NotContain("OleCreateFromFile")
                .And.NotContain("StgCreateDocfile");
        }
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "FreeP.Tests",
            nameof(WindowsOleInPlaceEngineTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
