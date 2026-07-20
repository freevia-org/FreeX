using FreeP.App.Recording;

namespace FreeP.App.Recording.Tests;

public sealed class LinuxRecordingProcessAdapterTests
{
    private static LinuxNarrationCaptureCommand Command =>
        new(
            "pw-record",
            new[] { "--rate=16000", "/tmp/narration.wav" },
            "/tmp/narration.wav",
            LinuxNarrationCaptureToolKind.PipeWire);

    [Fact]
    public void Start_ForwardsExactCommandToProcessFactory()
    {
        var process = new FakeChildProcess();
        var factory = new FakeChildProcessFactory(process);
        var adapter = new LinuxRecordingProcessAdapter(factory);
        var command = Command;

        adapter.Start(command).Should().BeSameAs(process);

        factory.Commands.Should().ContainSingle().Which.Should().BeSameAs(command);
    }

    [Fact]
    public void Stop_SendsInterruptAndAcceptsGracefulExit()
    {
        var process = new FakeChildProcess(waitResults: new[] { true }, exitCode: 130);
        var adapter = new LinuxRecordingProcessAdapter(new FakeChildProcessFactory(process));

        var result = adapter.Stop(process, TimeSpan.FromSeconds(1));

        process.InterruptCount.Should().Be(1);
        process.KillCount.Should().Be(0);
        result.Should().Match<LinuxRecordingProcessStopResult>(stop =>
            stop.Exited &&
            !stop.WasForced &&
            stop.ExitCode == 130 &&
            stop.HasExpectedRecorderExitCode);
    }

    [Fact]
    public void Stop_ForcesTerminationAfterGracefulTimeout()
    {
        var process = new FakeChildProcess(waitResults: new[] { false, true }, exitCode: 137);
        var adapter = new LinuxRecordingProcessAdapter(new FakeChildProcessFactory(process));

        var result = adapter.Stop(process, TimeSpan.FromMilliseconds(10));

        process.InterruptCount.Should().Be(1);
        process.KillCount.Should().Be(1);
        result.Exited.Should().BeTrue();
        result.WasForced.Should().BeTrue();
        result.HasExpectedRecorderExitCode.Should().BeFalse();
    }

    [Fact]
    public void Stop_AlreadyExitedDoesNotSignalProcess()
    {
        var process = new FakeChildProcess(hasExited: true, exitCode: 1, standardError: "server disconnected");
        var adapter = new LinuxRecordingProcessAdapter(new FakeChildProcessFactory(process));

        var result = adapter.Stop(process, TimeSpan.FromSeconds(1));

        process.InterruptCount.Should().Be(0);
        process.KillCount.Should().Be(0);
        result.StandardError.Should().Be("server disconnected");
    }

    [Fact]
    public void Cancel_InterruptsAndDoesNotKillWhenProcessExits()
    {
        var process = new FakeChildProcess(waitResults: new[] { true }, exitCode: 130);
        var adapter = new LinuxRecordingProcessAdapter(new FakeChildProcessFactory(process));

        adapter.Cancel(process, TimeSpan.FromSeconds(1));

        process.InterruptCount.Should().Be(1);
        process.KillCount.Should().Be(0);
    }

    [Fact]
    public void Cancel_KillsWhenRecorderIgnoresInterrupt()
    {
        var process = new FakeChildProcess(waitResults: new[] { false, true }, exitCode: 137);
        var adapter = new LinuxRecordingProcessAdapter(new FakeChildProcessFactory(process));

        adapter.Cancel(process, TimeSpan.FromMilliseconds(10));

        process.InterruptCount.Should().Be(1);
        process.KillCount.Should().Be(1);
    }

    [Fact]
    public void ChildDisposalIsIdempotent()
    {
        var process = new FakeChildProcess();

        process.Dispose();
        process.Dispose();

        process.DisposeCount.Should().Be(1);
    }

    private sealed class FakeChildProcessFactory(ILinuxRecordingChildProcess process)
        : ILinuxRecordingChildProcessFactory
    {
        public List<LinuxNarrationCaptureCommand> Commands { get; } = new();

        public ILinuxRecordingChildProcess Start(LinuxNarrationCaptureCommand command)
        {
            Commands.Add(command);
            return process;
        }
    }

    private sealed class FakeChildProcess : ILinuxRecordingChildProcess
    {
        private readonly Queue<bool> _waitResults;
        private readonly int? _exitCode;
        private bool _hasExited;
        private bool _disposed;

        public FakeChildProcess(
            IEnumerable<bool>? waitResults = null,
            bool hasExited = false,
            int? exitCode = 0,
            string standardError = "")
        {
            _waitResults = new Queue<bool>(waitResults ?? Array.Empty<bool>());
            _hasExited = hasExited;
            _exitCode = exitCode;
            StandardError = standardError;
        }

        public int ProcessId => 42;

        public bool HasExited => _hasExited;

        public int? ExitCode => _hasExited ? _exitCode : null;

        public string StandardError { get; }

        public int InterruptCount { get; private set; }

        public int KillCount { get; private set; }

        public int DisposeCount { get; private set; }

        public bool WaitForExit(TimeSpan timeout)
        {
            var result = _waitResults.Count > 0 && _waitResults.Dequeue();
            if (result)
                _hasExited = true;
            return result;
        }

        public void SendInterrupt() => InterruptCount++;

        public void Kill()
        {
            KillCount++;
            _hasExited = true;
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
