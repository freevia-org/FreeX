using Free.Shared.AppServices.Printing;
using FreeP.App.Compositor;
using FreeP.App.Recording;

namespace FreeP.App.Recording.Tests;

public sealed class LinuxNarrationCapturePlannerTests
{
    [Fact]
    public void ParsePipeWireTargets_PreservesDefaultAndStableDeviceIds()
    {
        const string output = """
            Available targets ("*" denotes default):
              41: Built-in Audio Monitor
            * 52: [USB Studio Microphone]
              52: duplicate
            """;

        var devices = LinuxNarrationCapturePlanner.ParsePipeWireTargets(output);

        devices.Should().HaveCount(2);
        devices[0].Should().Match<SlideShowRecordingCaptureDeviceDescriptor>(device =>
            device.DeviceId == "41" &&
            device.DisplayName == "Built-in Audio Monitor" &&
            !device.IsDefault &&
            device.IsAvailable &&
            device.ContentType == "audio/wav");
        devices[1].Should().Match<SlideShowRecordingCaptureDeviceDescriptor>(device =>
            device.DeviceId == "52" &&
            device.DisplayName == "USB Studio Microphone" &&
            device.IsDefault);
    }

    [Fact]
    public void ParsePulseAudioSources_UsesNamedDefaultSource()
    {
        const string output = """
            1	alsa_input.pci-0000_00_1f.3.analog-stereo	module-alsa-card.c	s16le 2ch 48000Hz	RUNNING
            2	alsa_input.usb-Blue_Mic-00.mono-fallback	module-alsa-card.c	s16le 1ch 48000Hz	IDLE
            """;

        var devices = LinuxNarrationCapturePlanner.ParsePulseAudioSources(
            output,
            "alsa_input.usb-Blue_Mic-00.mono-fallback\n");

        devices.Should().HaveCount(2);
        devices.Single(device => device.IsDefault).DeviceId
            .Should().Be("alsa_input.usb-Blue_Mic-00.mono-fallback");
        devices[1].DisplayName.Should().Be("mono fallback");
    }

    [Fact]
    public void ParseSources_WithoutDeclaredDefault_UsesFirstAvailableDevice()
    {
        LinuxNarrationCapturePlanner.ParsePipeWireTargets("  17: Desk microphone\n  18: USB microphone")
            .Single(device => device.IsDefault)
            .DeviceId.Should().Be("17");
    }

    [Fact]
    public void SelectMicrophone_PrefersExplicitSelectionThenHostDefault()
    {
        var devices = new[]
        {
            Microphone("default", isDefault: true),
            Microphone("selected", isDefault: false)
        };

        LinuxNarrationCapturePlanner.SelectMicrophone(devices, "selected").DeviceId
            .Should().Be("selected");
        LinuxNarrationCapturePlanner.SelectMicrophone(devices, "missing").DeviceId
            .Should().Be("default");
    }

    [Fact]
    public void BuildCaptureCommand_PipeWireUsesMonoPcmWavAndSelectedTarget()
    {
        var command = LinuxNarrationCapturePlanner.BuildCaptureCommand(
            new LinuxNarrationCaptureTool(
                LinuxNarrationCaptureToolKind.PipeWire,
                "/usr/bin/pw-record",
                "PipeWire"),
            Microphone("52", isDefault: false),
            "/tmp/narration.wav");

        command.FileName.Should().Be("/usr/bin/pw-record");
        command.Arguments.Should().Equal(
            "--rate=16000",
            "--channels=1",
            "--channel-map=mono",
            "--format=s16",
            "--target=52",
            "/tmp/narration.wav");
        command.ToolKind.Should().Be(LinuxNarrationCaptureToolKind.PipeWire);
    }

    [Fact]
    public void BuildCaptureCommand_PipeWireDefaultLetsSessionManagerSelectTarget()
    {
        var command = LinuxNarrationCapturePlanner.BuildCaptureCommand(
            new LinuxNarrationCaptureTool(
                LinuxNarrationCaptureToolKind.PipeWire,
                "pw-record",
                "PipeWire"),
            Microphone("52", isDefault: true),
            "/tmp/default.wav");

        command.Arguments.Should().NotContain(argument => argument.StartsWith("--target=", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildCaptureCommand_PulseAudioUsesWavAndSelectedSource()
    {
        var command = LinuxNarrationCapturePlanner.BuildCaptureCommand(
            new LinuxNarrationCaptureTool(
                LinuxNarrationCaptureToolKind.PulseAudio,
                "/usr/bin/parec",
                "PulseAudio"),
            Microphone("alsa_input.usb-mic", isDefault: true),
            "/tmp/narration.wav");

        command.Arguments.Should().Equal(
            "--file-format=wav",
            "--format=s16le",
            "--rate=16000",
            "--channels=1",
            "--channel-map=mono",
            "--device=alsa_input.usb-mic",
            "/tmp/narration.wav");
    }

    [Fact]
    public void Discovery_PrefersPipeWireAndDoesNotProbePulseAudio()
    {
        var locator = new FakeExecutableLocator(
            ("pw-record", "/usr/bin/pw-record"),
            ("parec", "/usr/bin/parec"),
            ("pactl", "/usr/bin/pactl"));
        var runner = new FakeProbeRunner();
        runner.Add("/usr/bin/pw-record", "--list-targets", new(0, "* 52: USB microphone", string.Empty));

        var discovery = new LinuxRecordingDeviceCatalog(locator, runner).Discover();

        discovery.IsAvailable.Should().BeTrue();
        discovery.Tool!.Kind.Should().Be(LinuxNarrationCaptureToolKind.PipeWire);
        runner.Invocations.Should().ContainSingle();
    }

    [Fact]
    public void Discovery_FallsBackToPulseAudioWhenPipeWireSessionIsUnavailable()
    {
        var locator = new FakeExecutableLocator(
            ("pw-record", "/usr/bin/pw-record"),
            ("parec", "/usr/bin/parec"),
            ("pactl", "/usr/bin/pactl"));
        var runner = new FakeProbeRunner();
        runner.Add("/usr/bin/pw-record", "--list-targets", new(1, string.Empty, "cannot connect"));
        runner.Add(
            "/usr/bin/pactl",
            "list short sources",
            new(0, "4\talsa_input.usb-mic\tdriver\ts16le 1ch 48000Hz\tRUNNING", string.Empty));
        runner.Add("/usr/bin/pactl", "get-default-source", new(0, "alsa_input.usb-mic\n", string.Empty));

        var discovery = new LinuxRecordingDeviceCatalog(locator, runner).Discover();

        discovery.IsAvailable.Should().BeTrue();
        discovery.Tool!.Kind.Should().Be(LinuxNarrationCaptureToolKind.PulseAudio);
        discovery.Devices.Should().ContainSingle(device =>
            device.DeviceId == "alsa_input.usb-mic" && device.IsDefault);
    }

    [Fact]
    public void Discovery_WithoutSupportedToolsReportsActionableUnavailableReason()
    {
        var discovery = new LinuxRecordingDeviceCatalog(
            new FakeExecutableLocator(),
            new FakeProbeRunner()).Discover();

        discovery.IsAvailable.Should().BeFalse();
        discovery.Tool.Should().BeNull();
        discovery.UnavailableReason.Should().Contain("pw-record").And.Contain("parec");
    }

    [Fact]
    public void Discovery_OutsideLinuxDoesNotProbeHostTools()
    {
        var runner = new FakeProbeRunner();

        var discovery = new LinuxRecordingDeviceCatalog(
            new FakeExecutableLocator(("pw-record", "/usr/bin/pw-record")),
            runner,
            isLinux: false).Discover();

        discovery.IsAvailable.Should().BeFalse();
        discovery.UnavailableReason.Should().Contain("only available on Linux");
        runner.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void SystemProbeRunner_DelegatesTimeoutAndMapsSharedProcessResult()
    {
        var processRunner = new CapturingProcessRunner(
            new ProcessResult(-1, "partial output", "", TimedOut: true));

        var result = new SystemLinuxRecordingProbeRunner(processRunner).Run(
            "/usr/bin/pw-record",
            ["--list-targets"],
            TimeSpan.FromSeconds(3));

        result.TimedOut.Should().BeTrue();
        result.StandardOutput.Should().Be("partial output");
        processRunner.Invocation.Should().BeEquivalentTo(new ProcessInvocation(
            "/usr/bin/pw-record",
            new[] { "--list-targets" },
            Timeout: TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void SystemProbeRunner_ContainsNoSecondNativeProcessImplementation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Recording",
            "Recording",
            "LinuxRecordingDeviceCatalog.cs"));

        source.Should().Contain("processRunner ?? new SystemProcessRunner()");
        source.Should().Contain("Timeout: timeout");
        source.Should().NotContain("using System.Diagnostics;");
        source.Should().NotContain("new ProcessStartInfo");
    }

    private static SlideShowRecordingCaptureDeviceDescriptor Microphone(string id, bool isDefault) =>
        new(
            SlideShowRecordingCaptureDeviceKind.Microphone,
            id,
            id,
            isDefault,
            IsAvailable: true,
            "audio/wav");

    private sealed class FakeExecutableLocator(params (string Name, string Path)[] executables)
        : ILinuxRecordingExecutableLocator
    {
        private readonly Dictionary<string, string> _executables =
            executables.ToDictionary(item => item.Name, item => item.Path, StringComparer.Ordinal);

        public string? FindExecutable(string executableName) =>
            _executables.GetValueOrDefault(executableName);
    }

    private sealed class FakeProbeRunner : ILinuxRecordingProbeRunner
    {
        private readonly Dictionary<string, LinuxRecordingProbeResult> _results = new(StringComparer.Ordinal);

        public List<string> Invocations { get; } = new();

        public void Add(string fileName, string arguments, LinuxRecordingProbeResult result) =>
            _results[Key(fileName, arguments)] = result;

        public LinuxRecordingProbeResult Run(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout)
        {
            var invocation = Key(fileName, string.Join(' ', arguments));
            Invocations.Add(invocation);
            return _results.GetValueOrDefault(
                invocation,
                new LinuxRecordingProbeResult(1, string.Empty, "missing fake probe"));
        }

        private static string Key(string fileName, string arguments) =>
            fileName + " " + arguments;
    }

    private sealed class CapturingProcessRunner(ProcessResult result) : IProcessRunner
    {
        public ProcessInvocation? Invocation { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Invocation = invocation;
            return Task.FromResult(result);
        }
    }
}
