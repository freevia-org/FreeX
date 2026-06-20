using System.Diagnostics;

namespace FreeX.ParityCompare;

/// <summary>
/// Orchestrates the two shells' <c>--parity-capture &lt;dir&gt;</c> runs.
///
/// Windows: build + run the WPF host exe natively.
/// Linux:   publish the Avalonia app self-contained linux-x64 and run it inside an
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
    /// Windows capture: build the WPF host (Release) then run
    /// <c>FreeX.App.Host.exe --parity-capture &lt;winDir&gt;</c>.
    /// </summary>
    public static void CaptureWindows(string repoRoot, string winDir)
    {
        Directory.CreateDirectory(winDir);
        var hostProj = Path.Combine(repoRoot, "src", "FreeX.App.Host", "FreeX.App.Host.csproj");
        if (!File.Exists(hostProj))
            throw new FileNotFoundException($"WPF host project not found: {hostProj}");

        Log("Building WPF host (Release)...");
        Run("dotnet", new[] { "build", hostProj, "-c", "Release" }, repoRoot);

        var exe = FindHostExe(repoRoot);
        Log($"Running WPF host capture -> {winDir}");
        Run(exe, new[] { "--parity-capture", winDir }, repoRoot);

        EnsureManifest(winDir, "windows");
    }

    private static string FindHostExe(string repoRoot)
    {
        var binDir = Path.Combine(repoRoot, "src", "FreeX.App.Host", "bin", "Release");
        if (Directory.Exists(binDir))
        {
            var exe = Directory.EnumerateFiles(binDir, "FreeX.App.Host.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (exe != null) return exe;
        }
        throw new FileNotFoundException(
            $"FreeX.App.Host.exe not found under {binDir}. Build the host first.");
    }

    /// <summary>
    /// Linux capture: publish Avalonia linux-x64 self-contained, then run under Docker+Xvfb.
    /// Mirrors tools/Run-LinuxAppInDocker.ps1 (Ubuntu 24.04, apt deps, LIBGL_ALWAYS_SOFTWARE,
    /// mounted /work dir). The container runs <c>./FreeX --parity-capture /work/out</c> under
    /// xvfb-run and the PNGs + manifest land back in <paramref name="linDir"/> via the mount.
    /// </summary>
    public static void CaptureLinux(string repoRoot, string linDir, string image = "ubuntu:24.04")
    {
        Directory.CreateDirectory(linDir);
        var publishDir = Path.Combine(linDir, "_publish-linux-x64");
        var outDir = Path.Combine(linDir, "out");
        Directory.CreateDirectory(outDir);

        var avaloniaProj = Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj");
        if (!File.Exists(avaloniaProj))
            throw new FileNotFoundException($"Avalonia project not found: {avaloniaProj}");

        Log("Publishing Avalonia linux-x64 self-contained...");
        Run("dotnet", new[]
        {
            "publish", avaloniaProj, "-c", "Release", "-f", "net10.0", "-r", "linux-x64",
            "--self-contained", "true", "-p:UseAppHost=true", "-p:PublishReadyToRun=false",
            "-p:PublishSingleFile=false", "-o", publishDir,
        }, repoRoot);

        if (!File.Exists(Path.Combine(publishDir, "FreeX")))
            throw new FileNotFoundException($"Published apphost not found at {publishDir}/FreeX.");

        var runScript = BuildContainerScript().Replace("\r\n", "\n");
        var scriptPath = Path.Combine(linDir, "run.sh");
        File.WriteAllText(scriptPath, runScript);

        // Mount linDir as /work: container sees _publish-linux-x64/, out/, run.sh.
        var mount = linDir.Replace('\\', '/') + ":/work";
        Log($"Running {image} (mount {mount})...");
        Run("docker", new[]
        {
            "run", "--rm", "-v", mount, image, "bash", "-c", "tr -d '\\r' < /work/run.sh | bash",
        }, repoRoot);

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
        "chmod +x FreeX",
        "echo '=== parity-capture (Avalonia, Xvfb) ==='",
        "xvfb-run -a --server-args=\"-screen 0 1120x720x24\" ./FreeX --parity-capture /work/out || echo \"parity-capture exit=$?\"",
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

    private static void Run(string fileName, string[] args, string workingDir)
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

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName}'");
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"'{fileName}' exited with code {proc.ExitCode}.");
    }
}
