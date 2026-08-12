using System.Diagnostics;
using Free.ToolsShared;

namespace FreeX.ParityCompare;

/// <summary>
/// Orchestrates the two shells' <c>--parity-capture &lt;dir&gt;</c> runs.
///
/// Windows: build + run the WPF capture host natively.
/// Linux:   publish the Avalonia capture host self-contained linux-x64 and run it inside an
///          Ubuntu 24.04 Docker container under Xvfb (reusing the approach in
///          <c>tools/Run-LinuxAppInDocker.ps1</c>), copying the PNGs + manifest back.
///
/// All steps shell out via <see cref="Process"/>; nothing here is exercised by the unit
/// tests (which feed synthetic manifests directly into the Core comparison engine).
/// </summary>
public static class CaptureRunner
{
    private static void Log(string m) => Console.WriteLine($"[capture] {m}");

    /// <summary>
    /// Windows capture: build the WPF capture host (Release) then run
    /// <c>FreeX.ParityCapture.Wpf.exe --parity-capture &lt;winDir&gt;</c>.
    /// </summary>
    public static void CaptureWindows(string repoRoot, string winDir)
    {
        Directory.CreateDirectory(winDir);
        var hostProj = Path.Combine(repoRoot, "tools", "FreeX.ParityCapture.Wpf", "FreeX.ParityCapture.Wpf.csproj");
        if (!File.Exists(hostProj))
            throw new FileNotFoundException($"WPF capture host project not found: {hostProj}");

        Log("Building WPF capture host (Release)...");
        Run("dotnet", new[] { "build", hostProj, "-c", "Release" }, repoRoot);

        var exe = FindHostExe(repoRoot);
        Log($"Running WPF capture host -> {winDir}");
        Run(VisualEvidenceProcessPlan.Create(
            exe,
            repoRoot,
            ["--parity-capture", winDir],
            TimeSpan.FromMinutes(30),
            "capture process tree"));

        EnsureManifest(winDir, "windows");
    }

    private static string FindHostExe(string repoRoot)
    {
        var binDir = Path.Combine(repoRoot, "tools", "FreeX.ParityCapture.Wpf", "bin", "Release");
        if (Directory.Exists(binDir))
        {
            var exe = Directory.EnumerateFiles(binDir, "FreeX.ParityCapture.Wpf.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (exe != null) return exe;
        }
        throw new FileNotFoundException(
            $"FreeX.ParityCapture.Wpf.exe not found under {binDir}. Build the capture host first.");
    }

    /// <summary>
    /// Linux capture: publish Avalonia linux-x64 self-contained, then run under Docker+Xvfb.
    /// Mirrors tools/Run-LinuxAppInDocker.ps1 (Ubuntu 24.04, apt deps, LIBGL_ALWAYS_SOFTWARE,
    /// mounted /work dir). The container runs <c>./FreeX.ParityCapture.Avalonia --parity-capture /work/out</c> under
    /// xvfb-run and the PNGs + manifest land back in <paramref name="linDir"/> via the mount.
    /// </summary>
    public static void CaptureLinux(string repoRoot, string linDir, string image = "ubuntu:24.04")
    {
        Directory.CreateDirectory(linDir);
        var publishDir = Path.Combine(linDir, "_publish-linux-x64");
        var outDir = Path.Combine(linDir, "out");
        Directory.CreateDirectory(outDir);

        var avaloniaProj = Path.Combine(repoRoot, "tools", "FreeX.ParityCapture.Avalonia", "FreeX.ParityCapture.Avalonia.csproj");
        if (!File.Exists(avaloniaProj))
            throw new FileNotFoundException($"Avalonia capture host project not found: {avaloniaProj}");

        Log("Publishing Avalonia linux-x64 self-contained...");
        Run("dotnet", new[]
        {
            "publish", avaloniaProj, "-c", "Release", "-f", "net10.0", "-r", "linux-x64",
            "--self-contained", "true", "-p:UseAppHost=true", "-p:PublishReadyToRun=false",
            "-p:PublishSingleFile=false", "-o", publishDir,
        }, repoRoot);

        if (!File.Exists(Path.Combine(publishDir, "FreeX.ParityCapture.Avalonia")))
            throw new FileNotFoundException($"Published apphost not found at {publishDir}/FreeX.ParityCapture.Avalonia.");

        var runScript = BuildContainerScript().Replace("\r\n", "\n");
        var scriptPath = Path.Combine(linDir, "run.sh");
        File.WriteAllText(scriptPath, runScript);

        // Mount linDir as /work: container sees _publish-linux-x64/, out/, run.sh.
        var mount = linDir.Replace('\\', '/') + ":/work";
        const string containerName = "freex-parity-capture-linux";
        TryRemoveContainer(containerName); // clear any stale container from a previous aborted run
        Log($"Running {image} (mount {mount})...");
        // A named container + timeout is a safety net: if the captured app ever fails to exit, the
        // run is force-killed (and the container removed) instead of hanging forever. The PNGs +
        // manifest are written before the app shuts down, so the comparison can still proceed.
        Run("docker", new[]
        {
            "run", "--rm", "--name", containerName, "-v", mount, image, "bash", "-c", "tr -d '\\r' < /work/run.sh | bash",
        }, repoRoot, timeout: TimeSpan.FromMinutes(10), killContainerName: containerName);

        EnsureManifest(outDir, "linux");
        Log($"Linux capture PNGs + manifest in {outDir}");
    }

    private static string BuildContainerScript() => string.Join('\n', new[]
    {
        "#!/usr/bin/env bash",
        "set -u",
        "export DEBIAN_FRONTEND=noninteractive",
        "export LIBGL_ALWAYS_SOFTWARE=1",
        "apt-get update -qq >/dev/null",
        "apt-get install -y -qq \\",
        "  libfontconfig1 libice6 libsm6 libx11-6 libx11-xcb1 libxext6 libxrender1 \\",
        "  libgl1 libegl1 libicu74 libssl3 zlib1g xvfb fonts-dejavu fonts-noto-cjk procps >/dev/null",
        "cp -a /work/_publish-linux-x64 /opt/freex",
        "cd /opt/freex",
        "chmod +x FreeX.ParityCapture.Avalonia",
        "echo '=== parity-capture (Avalonia, Xvfb) ==='",
        "xvfb-run -a --server-args=\"-screen 0 1120x720x24\" ./FreeX.ParityCapture.Avalonia --parity-capture /work/out || echo \"parity-capture exit=$?\"",
        "echo '=== captured files ==='",
        "ls -la /work/out || echo '(no out dir)'",
    });

    /// <summary>
    /// Verify a capture dir has a manifest.json; if a shell wrote PNGs but no manifest
    /// (older capture contract), synthesize a minimal one so the comparison can proceed.
    /// </summary>
    private static void EnsureManifest(string dir, string platform)
    {
        var manifestPath = Path.Combine(dir, "manifest.json");
        if (File.Exists(manifestPath)) return;

        var pngs = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.png").Select(Path.GetFileName).Where(n => n != null).ToList()
            : new List<string?>();
        if (pngs.Count == 0)
        {
            Console.Error.WriteLine($"[capture] WARNING: no manifest.json and no PNGs in {dir} — capture may have failed.");
            return;
        }

        Log($"No manifest.json in {dir}; synthesizing one from {pngs.Count} PNG(s).");
        var surfaces = pngs.Select(p =>
        {
            var id = Path.GetFileNameWithoutExtension(p!);
            var kind = id.Contains('.') ? id[..id.IndexOf('.')] : "other";
            return $"    {{ \"id\": \"{id}\", \"kind\": \"{kind}\", \"png\": \"{p}\", \"captured\": true }}";
        });
        var json = "{\n" +
                   $"  \"platform\": \"{platform}\",\n" +
                   $"  \"shell\": \"{(platform == "windows" ? "wpf" : "avalonia")}\",\n" +
                   "  \"surfaces\": [\n" + string.Join(",\n", surfaces) + "\n  ]\n}\n";
        File.WriteAllText(manifestPath, json);
    }

    private static void Run(
        string fileName,
        string[] args,
        string workingDir,
        TimeSpan? timeout = null,
        string? killContainerName = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Run(psi, fileName, timeout, killContainerName, "process tree");
    }

    private static void Run(VisualEvidenceProcessPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var psi = new ProcessStartInfo
        {
            FileName = plan.Executable,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            Arguments = plan.Arguments,
        };
        Run(
            psi,
            plan.Executable,
            plan.Timeout,
            killContainerName: null,
            plan.TimedOutProcessTreeDescription);
    }

    private static void Run(
        ProcessStartInfo psi,
        string fileName,
        TimeSpan? timeout,
        string? killContainerName,
        string timedOutProcessTreeDescription)
    {
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName}'");

        if (timeout is { } limit)
        {
            if (!proc.WaitForExit((int)limit.TotalMilliseconds))
            {
                Console.Error.WriteLine(
                    $"[capture] '{fileName}' did not exit within {limit.TotalMinutes:0.#} min — force-killing.");
                if (killContainerName is not null)
                    TryRemoveContainer(killContainerName);
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                throw new TimeoutException(
                    $"'{fileName}' did not exit within {limit}; its {timedOutProcessTreeDescription} was stopped.");
            }
        }
        else
        {
            proc.WaitForExit();
        }

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"'{fileName}' exited with code {proc.ExitCode}.");
    }

    /// <summary>Best-effort <c>docker rm -f &lt;name&gt;</c> so a named container can't linger.</summary>
    private static void TryRemoveContainer(string name)
    {
        try
        {
            var psi = new ProcessStartInfo("docker")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("rm");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(name);
            using var proc = Process.Start(psi);
            proc?.WaitForExit(15000);
        }
        catch
        {
            // No docker / no such container — nothing to clean up.
        }
    }
}
