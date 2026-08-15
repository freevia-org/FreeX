using Free.Shared.AppServices.Printing;

namespace FreeP.App.Recording;

public interface ILinuxRecordingDeviceCatalog
{
    LinuxNarrationCaptureDiscovery Discover();
}

public interface ILinuxRecordingExecutableLocator
{
    string? FindExecutable(string executableName);
}

public sealed record LinuxRecordingProbeResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

public interface ILinuxRecordingProbeRunner
{
    LinuxRecordingProbeResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout);
}

public sealed class LinuxRecordingDeviceCatalog : ILinuxRecordingDeviceCatalog
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly ILinuxRecordingExecutableLocator _executableLocator;
    private readonly ILinuxRecordingProbeRunner _probeRunner;
    private readonly bool _isLinux;

    public LinuxRecordingDeviceCatalog()
        : this(
            new PathLinuxRecordingExecutableLocator(),
            new SystemLinuxRecordingProbeRunner(),
            OperatingSystem.IsLinux())
    {
    }

    public LinuxRecordingDeviceCatalog(
        ILinuxRecordingExecutableLocator executableLocator,
        ILinuxRecordingProbeRunner probeRunner,
        bool isLinux = true)
    {
        _executableLocator = executableLocator ?? throw new ArgumentNullException(nameof(executableLocator));
        _probeRunner = probeRunner ?? throw new ArgumentNullException(nameof(probeRunner));
        _isLinux = isLinux;
    }

    public LinuxNarrationCaptureDiscovery Discover()
    {
        if (!_isLinux)
            return LinuxNarrationCaptureDiscovery.Unavailable("Linux narration capture is only available on Linux.");

        var failures = new List<string>();
        var pipeWire = TryDiscoverPipeWire(failures);
        if (pipeWire is not null)
            return pipeWire;

        var pulseAudio = TryDiscoverPulseAudio(failures);
        if (pulseAudio is not null)
            return pulseAudio;

        var reason = failures.Count == 0
            ? "Install PipeWire pw-record (preferred) or PulseAudio parec/pactl to record narration."
            : string.Join(" ", failures);
        return LinuxNarrationCaptureDiscovery.Unavailable(reason);
    }

    private LinuxNarrationCaptureDiscovery? TryDiscoverPipeWire(List<string> failures)
    {
        var executable = _executableLocator.FindExecutable("pw-record");
        if (executable is null)
            return null;

        var result = _probeRunner.Run(executable, new[] { "--list-targets" }, ProbeTimeout);
        if (!result.Succeeded)
        {
            failures.Add(ProbeFailure("PipeWire microphone discovery", result));
            return null;
        }

        var devices = LinuxNarrationCapturePlanner.ParsePipeWireTargets(result.StandardOutput);
        if (devices.Count == 0)
        {
            failures.Add("PipeWire reported no available microphone targets.");
            return null;
        }

        return new LinuxNarrationCaptureDiscovery(
            new LinuxNarrationCaptureTool(
                LinuxNarrationCaptureToolKind.PipeWire,
                executable,
                "PipeWire pw-record"),
            devices,
            "Linux camera recording is not available in the narration adapter.");
    }

    private LinuxNarrationCaptureDiscovery? TryDiscoverPulseAudio(List<string> failures)
    {
        var recorder = _executableLocator.FindExecutable("parec");
        var controller = _executableLocator.FindExecutable("pactl");
        if (recorder is null || controller is null)
            return null;

        var sources = _probeRunner.Run(
            controller,
            new[] { "list", "short", "sources" },
            ProbeTimeout);
        if (!sources.Succeeded)
        {
            failures.Add(ProbeFailure("PulseAudio microphone discovery", sources));
            return null;
        }

        var defaultSource = _probeRunner.Run(
            controller,
            new[] { "get-default-source" },
            ProbeTimeout);
        var devices = LinuxNarrationCapturePlanner.ParsePulseAudioSources(
            sources.StandardOutput,
            defaultSource.Succeeded ? defaultSource.StandardOutput : null);
        if (devices.Count == 0)
        {
            failures.Add("PulseAudio reported no available microphone sources.");
            return null;
        }

        return new LinuxNarrationCaptureDiscovery(
            new LinuxNarrationCaptureTool(
                LinuxNarrationCaptureToolKind.PulseAudio,
                recorder,
                "PulseAudio parec"),
            devices,
            "Linux camera recording is not available in the narration adapter.");
    }

    private static string ProbeFailure(string operation, LinuxRecordingProbeResult result)
    {
        var detail = result.TimedOut
            ? "timed out"
            : FirstNonEmpty(result.StandardError, result.StandardOutput, $"exited with code {result.ExitCode}");
        return $"{operation} failed: {detail}.";
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.Select(value => value?.Trim())
            .First(value => !string.IsNullOrWhiteSpace(value))!;
}

public sealed class PathLinuxRecordingExecutableLocator : ILinuxRecordingExecutableLocator
{
    public string? FindExecutable(string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        if (Path.IsPathRooted(executableName))
            return File.Exists(executableName) ? executableName : null;

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(new[] { "/usr/bin", "/usr/local/bin" })
            .Distinct(StringComparer.Ordinal);
        foreach (var entry in pathEntries)
        {
            try
            {
                var candidate = Path.Combine(entry.Trim(), executableName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
            }
        }

        return null;
    }
}

public sealed class SystemLinuxRecordingProbeRunner : ILinuxRecordingProbeRunner
{
    private readonly IProcessRunner _processRunner;

    public SystemLinuxRecordingProbeRunner(IProcessRunner? processRunner = null) =>
        _processRunner = processRunner ?? new SystemProcessRunner();

    public LinuxRecordingProbeResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        try
        {
            var result = _processRunner.RunAsync(
                    new ProcessInvocation(fileName, arguments, Timeout: timeout))
                .GetAwaiter()
                .GetResult();
            return new LinuxRecordingProbeResult(
                result.ExitCode,
                result.StandardOutput,
                result.StandardError,
                result.TimedOut);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new LinuxRecordingProbeResult(-1, string.Empty, ex.Message);
        }
    }
}
