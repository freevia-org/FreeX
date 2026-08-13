using FreeX.ParityCompare;
using FreeX.ParityCompare.Core;
using Free.ToolsShared;

// ---------------------------------------------------------------------------
// FreeX.ParityCompare — cross-platform visual parity runner.
//
// Runs BOTH shells' `--parity-capture <dir>` modes (WPF host natively on Windows,
// Avalonia self-contained linux-x64 under Docker/Xvfb), pairs the captured surface
// PNGs by id, computes a mean-pixel-diff metric, and emits an HTML/JSON/MD report.
//
//   dotnet run --project tools/FreeX.ParityCompare -- [flags]
//
// Flags:
//   --out <dir>        Report output dir (default: artifacts/parity-report)
//   --win-only         Capture + compare against Windows only (skip Linux/Docker)
//   --linux-only       Capture Linux only (skip Windows)
//   --skip-capture     Don't run either shell; compare existing capture dirs
//   --win-dir <dir>    Capture dir for Windows PNGs+manifest (default: <out>/capture-win)
//   --lin-dir <dir>    Capture dir for Linux PNGs+manifest  (default: <out>/capture-linux/out)
//   --threshold <pct>  Hard grid-fidelity fail threshold (default: 5.0)
//   --docker-image <i> Docker image for the Linux run (default: ubuntu:24.04)
// ---------------------------------------------------------------------------

var opts = CliOptions.Parse(args);
if (opts.ShowHelp)
{
    Console.WriteLine(CliOptions.HelpText);
    return 0;
}

string repoRoot = RepositoryRootLocator.Find(AppContext.BaseDirectory, "FreeX.slnx")
    ?? Directory.GetCurrentDirectory();

string outDir = Path.GetFullPath(opts.OutDir ?? Path.Combine(repoRoot, "artifacts", "parity-report"));
string winDir = Path.GetFullPath(opts.WinDir ?? Path.Combine(outDir, "capture-win"));
// Linux PNGs land in the Docker-mounted out/ subdir.
string linDir = Path.GetFullPath(opts.LinDir ?? Path.Combine(outDir, "capture-linux", "out"));
string imagesDir = Path.Combine(outDir, "images");
Directory.CreateDirectory(outDir);
Directory.CreateDirectory(imagesDir);

Console.WriteLine("=== FreeX cross-platform parity compare ===");
Console.WriteLine($"repo root : {repoRoot}");
Console.WriteLine($"report    : {outDir}");
Console.WriteLine($"win dir   : {winDir}");
Console.WriteLine($"linux dir : {linDir}");
Console.WriteLine($"mode      : {(opts.SkipCapture ? "skip-capture" : opts.WinOnly ? "win-only" : opts.LinuxOnly ? "linux-only" : "both")}");
Console.WriteLine();

// -------------------------------------------------------------------
// 1 + 2. Capture
// -------------------------------------------------------------------
if (!opts.SkipCapture)
{
    if (!opts.LinuxOnly)
    {
        try { CaptureRunner.CaptureWindows(repoRoot, winDir); }
        catch (Exception ex) { Console.Error.WriteLine($"Windows capture failed: {ex.Message}"); if (opts.WinOnly) return 3; }
    }
    if (!opts.WinOnly)
    {
        try { CaptureRunner.CaptureLinux(repoRoot, Path.GetDirectoryName(linDir)!, opts.DockerImage); }
        catch (Exception ex) { Console.Error.WriteLine($"Linux capture failed: {ex.Message}"); if (opts.LinuxOnly) return 3; }
    }
}

// -------------------------------------------------------------------
// 3. Pair + compare
// -------------------------------------------------------------------
var winManifest = LoadManifest(winDir, "windows", "wpf");
var linManifest = LoadManifest(linDir, "linux", "avalonia");

// H8: A capture side that was supposed to run but produced no surfaces (empty or absent manifest)
// must fail the gate explicitly. Otherwise every surface would be classified as WindowsOnly/LinuxOnly,
// IsHardRegression would find nothing, and the tool would vacuously report PASS when one shell
// rendered absolutely nothing.
if (!opts.SkipCapture)
{
    if (!opts.LinuxOnly && winManifest.Surfaces.Count == 0)
    {
        Console.Error.WriteLine("FATAL: Windows capture produced no surfaces — manifest is empty or absent. Parity gate cannot be evaluated.");
        return 3;
    }
    if (!opts.WinOnly && linManifest.Surfaces.Count == 0)
    {
        Console.Error.WriteLine("FATAL: Linux capture produced no surfaces — manifest is empty or absent. Parity gate cannot be evaluated.");
        return 3;
    }
}

var engine = new ParityComparisonEngine();
var comparison = engine.Compare(winManifest, linManifest, winDir, linDir, imagesDir, opts.Threshold);
var nameBoxContract = opts.WinOnly || opts.LinuxOnly
    ? new NameBoxDropdownPairContractResult(true, [])
    : NameBoxDropdownPairContract.Validate(winManifest, linManifest, winDir, linDir);

// -------------------------------------------------------------------
// 4. Report
// -------------------------------------------------------------------
var funcMatrix = Path.Combine(repoRoot, "docs", "parity", "functional-parity.md");
var htmlPath = ParityReport.WriteAll(comparison, outDir, funcMatrix);

Console.WriteLine();
Console.WriteLine($"surfaces      : {comparison.TotalSurfaces}");
Console.WriteLine($"present both  : {comparison.BothCount}");
Console.WriteLine($"win-only      : {comparison.WindowsOnlyCount}");
Console.WriteLine($"linux-only    : {comparison.LinuxOnlyCount}");
Console.WriteLine($"grid (hard)   : {comparison.HardSurfaceCount} surface(s), threshold {opts.Threshold:0.##}%");
Console.WriteLine($"chrome        : {comparison.ChromeSurfaceCount} surface(s) (expected diff — informational)");
Console.WriteLine($"hard regress. : {comparison.HardRegressions.Count}");
foreach (var r in comparison.HardRegressions)
    Console.WriteLine($"   REGRESSION {r.Id}  diff={r.DiffPercent:0.00}%");
if (comparison.LargeChromeDiffs.Count > 0)
{
    Console.WriteLine($"chrome >20%   : {comparison.LargeChromeDiffs.Count} (informational)");
    foreach (var r in comparison.LargeChromeDiffs)
        Console.WriteLine($"   chrome-diff {r.Id}  diff={r.DiffPercent:0.00}%");
}
Console.WriteLine($"report        : {htmlPath}");
Console.WriteLine($"name-box pair : {(nameBoxContract.IsValid ? "PASS" : "FAIL")}");
foreach (var failure in nameBoxContract.Failures)
    Console.WriteLine($"   NAME-BOX CONTRACT {failure}");
Console.WriteLine(comparison.Passed && nameBoxContract.IsValid ? "RESULT: PASS" : "RESULT: FAIL");

return comparison.Passed && nameBoxContract.IsValid ? 0 : 1;

// -------------------------------------------------------------------
static CaptureManifest LoadManifest(string dir, string platform, string shell)
{
    var path = Path.Combine(dir, "manifest.json");
    if (File.Exists(path))
    {
        try { return CaptureManifest.Load(path); }
        catch (Exception ex) { Console.Error.WriteLine($"Failed to parse {path}: {ex.Message}"); }
    }
    else
    {
        Console.Error.WriteLine($"No manifest at {path} — treating {platform} capture as empty.");
    }
    return new CaptureManifest { Platform = platform, Shell = shell };
}
