using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class OleActivationServiceTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.OleActivationServiceTests-");

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public void Planner_ResolvesPayloadAndSafeFilenameForPackagedObject()
    {
        var plan = OleActivationPlanner.TryBuild(new OleObjectInfo
        {
            EmbeddedBytes = [1, 2, 3],
            FileName = "..\\outside\\Budget.xlsx",
            EmbeddedExtension = "xlsx",
        });

        plan.Should().NotBeNull();
        plan!.Payload.Should().Equal(1, 2, 3);
        plan.FileName.Should().Be("Budget.xlsx");
        plan.Extension.Should().Be("xlsx");
    }

    [Fact]
    public async Task TryActivate_UsesInjectedTempAndLauncher_AndCommitsOnExit()
    {
        var temp = new FakeTempStore();
        var launcher = new FakeLauncher();
        var ole = new OleObjectInfo
        {
            EmbeddedBytes = [1, 2, 3],
            FileName = "Budget.xlsx",
            EmbeddedExtension = "xlsx",
        };

        OleActivationService.TryActivate(
                OleActivationPlanner.TryBuild(ole),
                bytes => ole.EmbeddedBytes = bytes,
                temp,
                launcher)
            .Should().BeTrue();

        temp.Plan!.FileName.Should().Be("Budget.xlsx");
        File.ReadAllBytes(launcher.Path).Should().Equal(1, 2, 3);
        File.WriteAllBytes(launcher.Path, [9, 8]);
        launcher.Process.Complete();
        await temp.DisposedTask;

        ole.EmbeddedBytes.Should().Equal(9, 8);
        temp.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task TryActivate_DetachedLauncherCleansUpWithoutEditBack()
    {
        var temp = new FakeTempStore();
        var launcher = new FakeLauncher();
        launcher.Process.SupportsEditBack = false;
        var ole = new OleObjectInfo { EmbeddedBytes = [1, 2, 3], EmbeddedExtension = "xlsx" };

        OleActivationService.TryActivate(
                OleActivationPlanner.TryBuild(ole),
                bytes => ole.EmbeddedBytes = bytes,
                temp,
                launcher)
            .Should().BeTrue();
        File.WriteAllBytes(launcher.Path, [9, 8]);
        launcher.Process.Complete();
        await temp.DisposedTask;

        ole.EmbeddedBytes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void OpenEmbeddedCommand_PrefersActiveInlineObject()
    {
        bool inlineOpened = false;
        bool slideOpened = false;

        OleActivationPlanner.TryOpenInlineFirst(
            () =>
            {
                inlineOpened = true;
                return true;
            },
            () =>
            {
                slideOpened = true;
                return true;
            }).Should().BeTrue();

        inlineOpened.Should().BeTrue();
        slideOpened.Should().BeFalse();
    }

    [Fact]
    public void OpenEmbeddedCommand_FallsBackToSlideObject()
    {
        bool slideOpened = false;

        OleActivationPlanner.TryOpenInlineFirst(
            () => false,
            () =>
            {
                slideOpened = true;
                return true;
            }).Should().BeTrue();

        slideOpened.Should().BeTrue();
    }

    [Fact]
    public void Coordinator_RejectsIneligibleShapesBeforeInvokingHostRoutes()
    {
        var calls = 0;
        var shape = new SlideShape { Kind = SlideShapeKind.Picture };

        OleActivationCoordinator.TryActivate(
                shape,
                _ => { calls++; return true; },
                _ => { calls++; return true; },
                _ => { calls++; return true; })
            .Should().BeFalse();

        calls.Should().Be(0);
    }

    [Fact]
    public void Coordinator_UsesInPlaceInjectedAndDefaultFallbackOrder()
    {
        var calls = new List<string>();
        var shape = new SlideShape
        {
            Kind = SlideShapeKind.Ole,
            OleObject = new OleObjectInfo { EmbeddedBytes = [1, 2, 3] }
        };

        OleActivationCoordinator.TryActivate(
                shape,
                _ => { calls.Add("in-place"); return false; },
                ole => { calls.Add("injected"); return false; },
                ole =>
                {
                    ole.Should().BeSameAs(shape.OleObject);
                    calls.Add("default");
                    return true;
                })
            .Should().BeTrue();

        calls.Should().Equal("in-place", "injected", "default");
    }

    [Fact]
    public void Coordinator_StopsAfterSuccessfulInPlaceActivation()
    {
        var fallbackCalls = 0;
        var shape = new SlideShape
        {
            Kind = SlideShapeKind.Ole,
            OleObject = new OleObjectInfo { EmbeddedBytes = [1] }
        };

        OleActivationCoordinator.TryActivate(
                shape,
                _ => true,
                _ => { fallbackCalls++; return true; },
                _ => { fallbackCalls++; return true; })
            .Should().BeTrue();

        fallbackCalls.Should().Be(0);
    }

    [Fact]
    public void TryActivate_EmptyPayload_ReturnsFalse()
    {
        OleActivationService.TryActivate(new OleObjectInfo()).Should().BeFalse();
    }

    [Theory]
    [InlineData("payload.exe")]
    [InlineData("payload.ps1")]
    [InlineData("payload.sh")]
    public void TryActivate_RejectsExecutableAndScriptExtensions(string fileName)
    {
        var temp = new FakeTempStore();
        var launcher = new FakeLauncher();

        OleActivationService.TryActivate(
                new OleActivationPlan([1, 2, 3], fileName),
                _ => { },
                temp,
                launcher)
            .Should().BeFalse();
        temp.MaterializeCalls.Should().Be(0);
        launcher.LaunchCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(".XLSX", "xlsx")]
    [InlineData("docx", "docx")]
    [InlineData("../../payload", "bin")]
    [InlineData("", "bin")]
    public void ResolveExtension_NormalizesEmbeddedExtension(string extension, string expected)
    {
        OleActivationService.ResolveExtension(new OleObjectInfo
        {
            EmbeddedExtension = extension,
        }).Should().Be(expected);
    }

    [Fact]
    public void ResolveExtension_UsesContentTypeWhenExtensionIsUnknown()
    {
        OleActivationService.ResolveExtension(new OleObjectInfo
        {
            EmbeddedExtension = "bin",
            EmbeddedContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        }).Should().Be("xlsx");
    }

    [Theory]
    [InlineData("Embedded.xlsx", "xlsx")]
    [InlineData("Embedded", "xlsx")]
    [InlineData("Embedded.bin", "xlsx")]
    public void ResolveExtension_UsesInlineFileNameThenClassName(
        string fileName,
        string expected)
    {
        OleActivationService.ResolveExtension(new InlineOleObjectInfo
        {
            FileName = fileName,
            ClassName = "Excel.Sheet.12",
        }).Should().Be(expected);
    }

    [Fact]
    public void TryCommitEditedPayload_ReplacesChangedBytes()
    {
        string path = Path.Combine(_temporaryDirectory.Path, "changed.bin");
        try
        {
            byte[] original = [1, 2, 3];
            File.WriteAllBytes(path, [4, 5, 6, 7]);
            var ole = new OleObjectInfo { EmbeddedBytes = original.ToArray() };

            OleActivationService.TryCommitEditedPayload(ole, path, original)
                .Should().BeTrue();
            ole.EmbeddedBytes.Should().Equal(4, 5, 6, 7);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void TryCommitEditedPayload_LeavesModelUntouchedForUnchangedOrEmptyPayload()
    {
        string unchangedPath = Path.Combine(_temporaryDirectory.Path, "unchanged.bin");
        string emptyPath = Path.Combine(_temporaryDirectory.Path, "empty.bin");
        try
        {
            byte[] original = [1, 2, 3];
            var unchanged = new OleObjectInfo { EmbeddedBytes = original.ToArray() };
            File.WriteAllBytes(unchangedPath, original);
            OleActivationService.TryCommitEditedPayload(unchanged, unchangedPath, original)
                .Should().BeFalse();
            unchanged.EmbeddedBytes.Should().Equal(original);

            var empty = new OleObjectInfo { EmbeddedBytes = original.ToArray() };
            File.WriteAllBytes(emptyPath, []);
            OleActivationService.TryCommitEditedPayload(empty, emptyPath, original)
                .Should().BeFalse();
            empty.EmbeddedBytes.Should().Equal(original);
        }
        finally
        {
            try { File.Delete(unchangedPath); } catch { }
            try { File.Delete(emptyPath); } catch { }
        }
    }

    [Fact]
    public void TryCommitEditedPayload_UpdatesInlineObjectBytes()
    {
        string path = Path.Combine(_temporaryDirectory.Path, "inline.bin");
        try
        {
            byte[] original = [1, 2, 3];
            File.WriteAllBytes(path, [8, 9]);
            var inline = new InlineOleObjectInfo
            {
                EmbeddedBytes = original.ToArray(),
                FileName = "Embedded.xlsx",
                ClassName = "Excel.Sheet.12",
            };

            OleActivationService.TryCommitEditedPayload(inline, path, original)
                .Should().BeTrue();
            inline.EmbeddedBytes.Should().Equal(8, 9);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private sealed class FakeTempStore : IOleActivationTempFileStore
    {
        public OleActivationPlan? Plan { get; private set; }
        public bool Disposed { get; private set; }
        public int MaterializeCalls { get; private set; }
        public Task DisposedTask => _disposed.Task;
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IOleActivationTempFile Materialize(OleActivationPlan plan)
        {
            Plan = plan;
            MaterializeCalls++;
            var path = Path.Combine(Path.GetTempPath(), $"freep-ole-fake-{Guid.NewGuid():N}", plan.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, plan.Payload);
            return new FakeTempFile(path, () =>
            {
                Disposed = true;
                _disposed.TrySetResult();
            });
        }
    }

    private sealed class FakeTempFile : IOleActivationTempFile
    {
        private readonly Action _onDispose;
        public FakeTempFile(string path, Action onDispose) { Path = path; _onDispose = onDispose; }
        public string Path { get; }
        public byte[] ReadAllBytes() => File.ReadAllBytes(Path);
        public void Dispose()
        {
            _onDispose();
            try { Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, true); } catch { }
        }
    }

    private sealed class FakeLauncher : IOleActivationLauncher
    {
        public FakeProcess Process { get; } = new();
        public string Path { get; private set; } = string.Empty;
        public int LaunchCalls { get; private set; }
        public IOleActivationProcess Launch(string path) { Path = path; LaunchCalls++; return Process; }
    }

    private sealed class FakeProcess : IOleActivationProcess
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ExitTask => _exit.Task;
        public bool SupportsEditBack { get; set; } = true;
        public void Complete() => _exit.TrySetResult();
        public void Dispose() { }
    }
}
