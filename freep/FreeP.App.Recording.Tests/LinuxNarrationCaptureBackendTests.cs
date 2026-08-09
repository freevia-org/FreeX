using System.Buffers.Binary;
using FreeP.App.Compositor;
using FreeP.App.Recording;

namespace FreeP.App.Recording.Tests;

public sealed class LinuxNarrationCaptureBackendTests
{
    [Fact]
    public void Readiness_WithPipeWireMicrophoneExposesNarrationOnly()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-narration-tests-");
        using var backend = CreateBackend(temp.Path, new FakeProcessAdapter());

        backend.AdapterReadiness.CanCaptureNarration.Should().BeTrue();
        backend.AdapterReadiness.CanCaptureCamera.Should().BeFalse();
        backend.Capabilities.CanCaptureNarration.Should().BeTrue();
        backend.Capabilities.CanCaptureCamera.Should().BeFalse();
        backend.AdapterReadiness.Devices.Should().ContainSingle(device =>
            device.DeviceId == "52" && device.IsDefault && device.ContentType == "audio/wav");
    }

    [Fact]
    public void CompleteCapture_StopsRecorderAndReturnsPersistableWavPayload()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-narration-tests-");
        var processAdapter = new FakeProcessAdapter { PayloadOnStop = BuildWavePayload() };
        using var backend = CreateBackend(temp.Path, processAdapter);
        var started = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

        backend.BeginCapture(StartRequest(slideIndex: 1, started));
        var result = backend.CompleteCapture(CompleteRequest(slideIndex: 1, started));

        processAdapter.Commands.Should().ContainSingle();
        var command = processAdapter.Commands[0];
        command.ToolKind.Should().Be(LinuxNarrationCaptureToolKind.PipeWire);
        command.OutputPath.Should().EndWith(".wav");
        command.OutputPath.Should().Contain("freep-narration-slide-0002-");
        processAdapter.StopCount.Should().Be(1);
        result.Should().Match<SlideShowRecordingCaptureResult>(capture =>
            capture.IsCaptured &&
            !capture.IsDeferred &&
            capture.PackagePath == "ppt/media/freep-recordings/avalonia/slide-002-narration.wav" &&
            capture.SuggestedFileNameOverride == "slide-002-narration.wav" &&
            capture.ContentTypeOverride == "audio/wav" &&
            capture.ContentLengthBytes == BuildWavePayload().Length &&
            capture.ContentSha256.Length == 64 &&
            capture.PayloadBytes != null);
        File.Exists(command.OutputPath).Should().BeFalse("temporary capture files must be cleaned after materialization");
    }

    [Fact]
    public void CompleteCapture_RejectsRecorderThatExitedDuringStartup()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-narration-tests-");
        var processAdapter = new FakeProcessAdapter { ExitDuringStartup = true, StandardError = "server unavailable" };
        using var backend = CreateBackend(temp.Path, processAdapter);
        var started = DateTimeOffset.UtcNow;

        backend.BeginCapture(StartRequest(0, started));
        var result = backend.CompleteCapture(CompleteRequest(0, started));

        result.IsDeferred.Should().BeTrue();
        result.StatusText.Should().Contain("exited before").And.Contain("server unavailable");
        processAdapter.StopCount.Should().Be(0);
        processAdapter.Processes.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void CompleteCapture_PropagatesStartFailure()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-narration-tests-");
        var processAdapter = new FakeProcessAdapter { StartException = new InvalidOperationException("permission denied") };
        using var backend = CreateBackend(temp.Path, processAdapter);
        var started = DateTimeOffset.UtcNow;

        backend.BeginCapture(StartRequest(0, started));
        var result = backend.CompleteCapture(CompleteRequest(0, started));

        result.IsDeferred.Should().BeTrue();
        result.StatusText.Should().Contain("could not start").And.Contain("permission denied");
    }

    [Fact]
    public void BeginCapture_CleansUpLaunchedRecorder_WhenStartupProbeThrows()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-narration-tests-");
        var processAdapter = new FakeProcessAdapter
        {
            WaitForExitException = new InvalidOperationException("startup probe failed")
        };
        using var backend = CreateBackend(temp.Path, processAdapter);
        var started = DateTimeOffset.UtcNow;

        backend.BeginCapture(StartRequest(0, started));

        var process = processAdapter.Processes.Should().ContainSingle().Which;
        var outputPath = processAdapter.Commands.Single().OutputPath;
        processAdapter.CancelCount.Should().Be(1);
        process.DisposeCount.Should().Be(1);
        File.Exists(outputPath).Should().BeFalse();
        backend.CompleteCapture(CompleteRequest(0, started)).StatusText
            .Should().Contain("could not start").And.Contain("startup probe failed");
    }

    [Fact]
    public void CompleteCapture_RejectsForcedStopAndInvalidWavePayload()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-narration-tests-");
        var forcedAdapter = new FakeProcessAdapter
        {
            StopResult = new LinuxRecordingProcessStopResult(true, true, 137, string.Empty),
            PayloadOnStop = BuildWavePayload()
        };
        using var forcedBackend = CreateBackend(temp.Path, forcedAdapter);
        var started = DateTimeOffset.UtcNow;
        forcedBackend.BeginCapture(StartRequest(0, started));

        forcedBackend.CompleteCapture(CompleteRequest(0, started)).StatusText
            .Should().Contain("forced termination");

        var invalidAdapter = new FakeProcessAdapter { PayloadOnStop = "not-wave"u8.ToArray() };
        using var invalidBackend = CreateBackend(temp.Path, invalidAdapter);
        invalidBackend.BeginCapture(StartRequest(1, started));

        invalidBackend.CompleteCapture(CompleteRequest(1, started)).StatusText
            .Should().Contain("empty or invalid WAV");
    }

    [Fact]
    public void UnavailableToolsReturnDeferredWithoutStartingProcess()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-narration-tests-");
        var adapter = new FakeProcessAdapter();
        using var backend = new LinuxNarrationCaptureBackend(
            Metadata(temp.Path),
            new FakeDeviceCatalog(LinuxNarrationCaptureDiscovery.Unavailable("pw-record and parec are missing")),
            adapter);
        var started = DateTimeOffset.UtcNow;

        backend.BeginCapture(StartRequest(0, started));
        var result = backend.CompleteCapture(CompleteRequest(0, started));

        backend.Capabilities.CanCaptureNarration.Should().BeFalse();
        result.IsDeferred.Should().BeTrue();
        result.StatusText.Should().Contain("pw-record and parec are missing");
        adapter.Commands.Should().BeEmpty();
    }

    [Fact]
    public void CancelCapture_InterruptsDisposesAndDeletesTemporaryOutput()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-narration-tests-");
        var adapter = new FakeProcessAdapter { PayloadOnStart = BuildWavePayload() };
        using var backend = CreateBackend(temp.Path, adapter);
        var started = DateTimeOffset.UtcNow;

        backend.BeginCapture(StartRequest(0, started));
        var outputPath = adapter.Commands.Single().OutputPath;
        File.Exists(outputPath).Should().BeTrue();

        backend.CancelCapture(0);

        adapter.CancelCount.Should().Be(1);
        adapter.Processes.Single().DisposeCount.Should().Be(1);
        File.Exists(outputPath).Should().BeFalse();
        backend.CompleteCapture(CompleteRequest(0, started)).StatusText.Should().Contain("was not started");
    }

    [Fact]
    public void StartingSameSlideAgainCancelsPriorRecorder()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-narration-tests-");
        var adapter = new FakeProcessAdapter();
        using var backend = CreateBackend(temp.Path, adapter);
        var started = DateTimeOffset.UtcNow;

        backend.BeginCapture(StartRequest(0, started));
        backend.BeginCapture(StartRequest(0, started.AddSeconds(1)));

        adapter.CancelCount.Should().Be(1);
        adapter.Processes.Should().HaveCount(2);
        adapter.Processes[0].DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_CancelsEveryActiveRecorderAndIsIdempotent()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-narration-tests-");
        var adapter = new FakeProcessAdapter();
        var backend = CreateBackend(temp.Path, adapter);
        var started = DateTimeOffset.UtcNow;
        backend.BeginCapture(StartRequest(0, started));
        backend.BeginCapture(StartRequest(1, started));

        backend.Dispose();
        backend.Dispose();

        adapter.CancelCount.Should().Be(2);
        adapter.Processes.Should().OnlyContain(process => process.DisposeCount == 1);
        Action afterDispose = () => backend.BeginCapture(StartRequest(2, started));
        afterDispose.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void PreferredMicrophoneIsPassedToRecorderCommand()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-narration-tests-");
        var adapter = new FakeProcessAdapter();
        var discovery = AvailableDiscovery(
            Microphone("52", isDefault: true),
            Microphone("77", isDefault: false));
        using var backend = new LinuxNarrationCaptureBackend(
            Metadata(temp.Path) with { PreferredMicrophoneDeviceId = "77" },
            new FakeDeviceCatalog(discovery),
            adapter);

        backend.BeginCapture(StartRequest(0, DateTimeOffset.UtcNow));

        adapter.Commands.Single().Arguments.Should().Contain("--target=77");
    }

    private static LinuxNarrationCaptureBackend CreateBackend(
        string temporaryDirectory,
        FakeProcessAdapter adapter) =>
        new(
            Metadata(temporaryDirectory),
            new FakeDeviceCatalog(AvailableDiscovery(Microphone("52", isDefault: true))),
            adapter);

    private static LinuxRecordingHostMetadata Metadata(string temporaryDirectory) =>
        new(
            "Avalonia slideshow",
            "Avalonia Linux narration capture adapter",
            "ppt/media/freep-recordings/avalonia",
            TemporaryDirectory: temporaryDirectory);

    private static LinuxNarrationCaptureDiscovery AvailableDiscovery(
        params SlideShowRecordingCaptureDeviceDescriptor[] devices) =>
        new(
            new LinuxNarrationCaptureTool(
                LinuxNarrationCaptureToolKind.PipeWire,
                "/usr/bin/pw-record",
                "PipeWire pw-record"),
            devices,
            "Linux camera recording is unavailable.");

    private static SlideShowRecordingCaptureDeviceDescriptor Microphone(string id, bool isDefault) =>
        new(
            SlideShowRecordingCaptureDeviceKind.Microphone,
            id,
            id,
            isDefault,
            IsAvailable: true,
            "audio/wav");

    private static SlideShowRecordingCaptureStartRequest StartRequest(
        int slideIndex,
        DateTimeOffset started) =>
        new(
            SlideShowRecordingMediaArtifactKind.NarrationAudio,
            slideIndex,
            started,
            $"slide-{slideIndex + 1:D3}-narration.m4a",
            "audio/mp4");

    private static SlideShowRecordingCaptureRequest CompleteRequest(
        int slideIndex,
        DateTimeOffset started) =>
        new(
            SlideShowRecordingMediaArtifactKind.NarrationAudio,
            slideIndex,
            started,
            started.AddSeconds(2),
            2000,
            $"slide-{slideIndex + 1:D3}-narration.m4a",
            "audio/mp4");

    private static byte[] BuildWavePayload()
    {
        const int dataLength = 8;
        var payload = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(payload);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), (uint)(payload.Length - 8));
        "WAVE"u8.CopyTo(payload.AsSpan(8));
        "fmt "u8.CopyTo(payload.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(22, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24, 4), 16000);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(28, 4), 32000);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(32, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(34, 2), 16);
        "data"u8.CopyTo(payload.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(40, 4), dataLength);
        for (var index = 44; index < payload.Length; index++)
            payload[index] = (byte)(index - 43);
        return payload;
    }

    private sealed class FakeDeviceCatalog(LinuxNarrationCaptureDiscovery discovery)
        : ILinuxRecordingDeviceCatalog
    {
        public LinuxNarrationCaptureDiscovery Discover() => discovery;
    }

    private sealed class FakeProcessAdapter : ILinuxRecordingProcessAdapter
    {
        private readonly Dictionary<ILinuxRecordingChildProcess, LinuxNarrationCaptureCommand> _commandsByProcess = new();

        public List<LinuxNarrationCaptureCommand> Commands { get; } = new();

        public List<FakeChildProcess> Processes { get; } = new();

        public Exception? StartException { get; init; }

        public bool ExitDuringStartup { get; init; }

        public Exception? WaitForExitException { get; init; }

        public string StandardError { get; init; } = string.Empty;

        public byte[]? PayloadOnStart { get; init; }

        public byte[]? PayloadOnStop { get; init; }

        public LinuxRecordingProcessStopResult StopResult { get; init; } =
            new(true, false, 0, string.Empty);

        public int StopCount { get; private set; }

        public int CancelCount { get; private set; }

        public ILinuxRecordingChildProcess Start(LinuxNarrationCaptureCommand command)
        {
            if (StartException is not null)
                throw StartException;

            Commands.Add(command);
            if (PayloadOnStart is not null)
                File.WriteAllBytes(command.OutputPath, PayloadOnStart);
            var process = new FakeChildProcess(
                ExitDuringStartup,
                StandardError,
                WaitForExitException);
            Processes.Add(process);
            _commandsByProcess[process] = command;
            return process;
        }

        public LinuxRecordingProcessStopResult Stop(
            ILinuxRecordingChildProcess process,
            TimeSpan gracefulTimeout)
        {
            StopCount++;
            if (PayloadOnStop is not null)
                File.WriteAllBytes(_commandsByProcess[process].OutputPath, PayloadOnStop);
            ((FakeChildProcess)process).MarkExited(StopResult.ExitCode);
            return StopResult;
        }

        public void Cancel(ILinuxRecordingChildProcess process, TimeSpan gracefulTimeout)
        {
            CancelCount++;
            ((FakeChildProcess)process).MarkExited(130);
        }
    }

    private sealed class FakeChildProcess : ILinuxRecordingChildProcess
    {
        private readonly bool _exitsDuringStartup;
        private bool _hasExited;
        private int? _exitCode;
        private bool _startupWaitPending = true;
        private bool _disposed;

        private readonly Exception? _waitForExitException;

        public FakeChildProcess(
            bool exitsDuringStartup,
            string standardError,
            Exception? waitForExitException)
        {
            _exitsDuringStartup = exitsDuringStartup;
            _hasExited = exitsDuringStartup;
            _exitCode = exitsDuringStartup ? 1 : null;
            StandardError = standardError;
            _waitForExitException = waitForExitException;
        }

        public int ProcessId => 42;

        public bool HasExited => _hasExited;

        public int? ExitCode => _exitCode;

        public string StandardError { get; }

        public int DisposeCount { get; private set; }

        public bool WaitForExit(TimeSpan timeout)
        {
            if (_waitForExitException is not null)
                throw _waitForExitException;

            if (_startupWaitPending)
            {
                _startupWaitPending = false;
                return _exitsDuringStartup;
            }
            return _hasExited;
        }

        public void SendInterrupt()
        {
        }

        public void Kill() => MarkExited(137);

        public void MarkExited(int? exitCode)
        {
            _hasExited = true;
            _exitCode = exitCode;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            DisposeCount++;
        }
    }

}
