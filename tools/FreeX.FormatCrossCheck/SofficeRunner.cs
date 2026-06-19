using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace FreeX.FormatCrossCheck;

/// <summary>
/// Drives a headless LibreOffice (<c>soffice</c>) as the EXTERNAL cross-checker. LibreOffice opens a
/// FreeX-written interchange file and re-exports it to xlsx via its own import/export filters; if a
/// value/formula survives that path, FreeX's output was genuinely consumable by a real third-party
/// application (not just FreeX's own read-back).
///
/// Gotchas baked in here:
///   * soffice allows only ONE running instance per user profile. We hand every invocation a unique
///     throwaway profile via <c>-env:UserInstallation=file:///&lt;tmp&gt;</c> so concurrent/back-to-back
///     calls never hit the "already running" lock.
///   * We invoke <c>soffice.com</c> (the console front-end) when present so the process actually blocks
///     until conversion finishes; <c>soffice.exe</c> can detach. Either is accepted.
/// </summary>
internal sealed class SofficeRunner
{
    public string ExecutablePath { get; }

    private SofficeRunner(string exe) => ExecutablePath = exe;

    /// <summary>
    /// Locates soffice. Order: explicit env override (FREEX_SOFFICE), PATH (soffice.com/soffice),
    /// then the standard Windows install dirs. Returns null when LibreOffice is not installed.
    /// </summary>
    public static SofficeRunner? Locate()
    {
        var env = Environment.GetEnvironmentVariable("FREEX_SOFFICE");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return new SofficeRunner(env);

        foreach (var candidate in CandidatePaths())
        {
            if (File.Exists(candidate))
                return new SofficeRunner(candidate);
        }

        // Last resort: rely on PATH resolution.
        foreach (var name in new[] { "soffice.com", "soffice.exe", "soffice" })
        {
            var resolved = ResolveOnPath(name);
            if (resolved is not null)
                return new SofficeRunner(resolved);
        }

        return null;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var roots = new[]
        {
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            @"C:\Program Files",
            @"C:\Program Files (x86)",
        };
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)).Distinct())
        {
            yield return Path.Combine(root!, "LibreOffice", "program", "soffice.com");
            yield return Path.Combine(root!, "LibreOffice", "program", "soffice.exe");
        }
        // Common Linux / macOS locations so the tool is portable to CI if soffice is present.
        yield return "/usr/bin/soffice";
        yield return "/usr/local/bin/soffice";
        yield return "/snap/bin/libreoffice";
        yield return "/Applications/LibreOffice.app/Contents/MacOS/soffice";
    }

    private static string? ResolveOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator).Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            try
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    public sealed record ConvertResult(bool Success, string? OutputXlsxPath, string Diagnostics);

    /// <summary>
    /// Has LibreOffice open <paramref name="inputFile"/> and re-export it to xlsx in <paramref name="outDir"/>.
    /// A unique user-profile dir avoids the single-instance lock. Returns the produced xlsx path on success.
    ///
    /// <paramref name="inputFilter"/> pins the IMPORT filter. This matters for ambiguous extensions: an
    /// .html file is opened by default with the Writer/Web filter (which cannot export to xlsx); forcing
    /// "Calc HTML (StarCalc)" makes LibreOffice open it as a spreadsheet so the xlsx export succeeds.
    /// Pass null to let LibreOffice auto-detect (correct for xlsx/ods/xml/csv).
    /// </summary>
    public ConvertResult ConvertToXlsx(string inputFile, string outDir, string? inputFilter = null)
    {
        Directory.CreateDirectory(outDir);
        var profileDir = Path.Combine(Path.GetTempPath(), "fx-soffice-profile", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profileDir);

        // file:/// URI for the profile; backslashes -> forward slashes.
        var profileUri = "file:///" + profileDir.Replace('\\', '/').TrimStart('/');

        var psi = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--headless");
        psi.ArgumentList.Add("--norestore");
        psi.ArgumentList.Add("--nolockcheck");
        psi.ArgumentList.Add("--nologo");
        psi.ArgumentList.Add($"-env:UserInstallation={profileUri}");
        // --infilter must precede --convert-to; soffice rejects the pair (prints usage) if it follows.
        if (!string.IsNullOrEmpty(inputFilter))
            psi.ArgumentList.Add($"--infilter={inputFilter}");
        psi.ArgumentList.Add("--convert-to");
        psi.ArgumentList.Add("xlsx:Calc MS Excel 2007 XML");
        psi.ArgumentList.Add("--outdir");
        psi.ArgumentList.Add(outDir);
        psi.ArgumentList.Add(inputFile);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        try
        {
            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // 120s is generous; conversion of a single workbook is normally < 10s.
            if (!proc.WaitForExit(120_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return new ConvertResult(false, null, $"soffice timed out after 120s\n{stdout}\n{stderr}");
            }

            var expected = Path.Combine(outDir, Path.GetFileNameWithoutExtension(inputFile) + ".xlsx");
            if (File.Exists(expected))
                return new ConvertResult(true, expected, Trim(stdout, stderr));

            // soffice sometimes reports the produced path in stdout ("-> /path/out.xlsx using filter ...").
            var produced = FindProducedFromStdout(stdout.ToString(), outDir);
            if (produced is not null && File.Exists(produced))
                return new ConvertResult(true, produced, Trim(stdout, stderr));

            return new ConvertResult(false, null,
                $"soffice exit={proc.ExitCode}; no xlsx produced at {expected}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }
        catch (Exception ex)
        {
            return new ConvertResult(false, null, $"soffice launch failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(profileDir, recursive: true); } catch { }
        }
    }

    private static string? FindProducedFromStdout(string stdout, string outDir)
    {
        // soffice prints lines like: "convert <in> -> <out> using filter : Calc MS Excel 2007 XML"
        foreach (var line in stdout.Split('\n'))
        {
            var arrow = line.IndexOf("->", StringComparison.Ordinal);
            if (arrow < 0) continue;
            var after = line[(arrow + 2)..].Trim();
            var usingIdx = after.IndexOf(" using filter", StringComparison.OrdinalIgnoreCase);
            if (usingIdx >= 0) after = after[..usingIdx].Trim();
            if (after.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) && File.Exists(after))
                return after;
        }
        // Fallback: any .xlsx in outDir (single-file conversions only produce one).
        var xlsx = Directory.EnumerateFiles(outDir, "*.xlsx").FirstOrDefault();
        return xlsx;
    }

    private static string Trim(StringBuilder o, StringBuilder e)
    {
        var s = (o.ToString() + e.ToString()).Trim();
        return s.Length > 600 ? s[..600] + "..." : s;
    }
}
