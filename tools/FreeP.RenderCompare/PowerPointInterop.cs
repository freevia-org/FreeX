using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace FreeP.RenderCompare;

/// <summary>
/// Drives Microsoft PowerPoint via COM to export presentation slides to PNG.
/// Mirrors the pattern used in FreeX.ChartInteropCompare (Excel COM automation).
///
/// PowerPoint COM constants used:
///   msoFalse = 0, msoTrue = -1
///   ppAlertsNone = 2
///   ppFixedFormatTypePDF = 2  (not used here)
///   Slide.Export(path, filterName, scaleWidth, scaleHeight) -> exports to image
/// </summary>
internal static class PowerPointInterop
{
    private const string PowerPointProcessName = "POWERPNT";
    internal const string ProgId = "PowerPoint.Application";

    // msoFalse / msoTrue
    private const int MsoFalse = 0;
    private const int MsoTrue  = -1;

    // PpAlerts enum: ppAlertsNone = 2
    private const int PpAlertsNone = 2;

    /// <summary>
    /// Export every slide of <paramref name="pptxPath"/> to PNG files in <paramref name="outDir"/>.
    /// Files are named slide-01.png, slide-02.png, etc.
    /// </summary>
    /// <returns>0 on success, 1 on failure.</returns>
    internal static int ExportSlidesToPng(string pptxPath, string outDir, int width, int height) =>
        ExportSlidesToPngDetailed(pptxPath, outDir, width, height).ExitCode;

    internal static PowerPointComAvailability CheckAvailability(
        Func<string, Type?>? resolveProgId = null,
        DateTimeOffset? checkedAtUtc = null,
        string? machineName = null)
    {
        resolveProgId ??= Type.GetTypeFromProgID;
        checkedAtUtc ??= DateTimeOffset.UtcNow;
        machineName ??= Environment.MachineName;

        try
        {
            var type = resolveProgId(ProgId);
            return type is null
                ? PowerPointComAvailability.Unavailable(
                    ProgId,
                    checkedAtUtc.Value,
                    machineName,
                    $"COM ProgID '{ProgId}' is not registered. Install desktop Microsoft PowerPoint to generate authoritative baselines.")
                : PowerPointComAvailability.Available(ProgId, checkedAtUtc.Value, machineName);
        }
        catch (Exception ex)
        {
            return PowerPointComAvailability.Unavailable(
                ProgId,
                checkedAtUtc.Value,
                machineName,
                $"COM ProgID '{ProgId}' probe failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static PowerPointExportResult ExportSlidesToPngDetailed(string pptxPath, string outDir, int width, int height)
    {
        var ownedPids = GetPowerPointProcessIds();

        dynamic? app = null;
        dynamic? presentation = null;

        try
        {
            app = CreatePowerPointApplication();

            Console.WriteLine("  PowerPoint started.");

            presentation = OpenPresentation(app, pptxPath);

            var slideCount = (int)presentation.Slides.Count;
            Console.WriteLine($"  Slides: {slideCount}");

            var errors = 0;
            for (var i = 1; i <= slideCount; i++)
            {
                var outPath = Path.Combine(outDir, $"slide-{i:D2}.png");
                try
                {
                    dynamic slide = presentation.Slides.Item(i);
                    // Slide.Export(PathName, FilterName, ScaleWidth, ScaleHeight)
                    slide.Export(outPath, "PNG", width, height);
                    Console.WriteLine($"  [ok] slide {i:D2}/{slideCount} -> {Path.GetFileName(outPath)}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  [fail] slide {i}: {ex.GetType().Name}: {ex.Message}");
                    errors++;
                }
            }

            ClosePresentation(ref presentation);
            QuitApplication(ref app);
            WaitForPowerPointToExit(ownedPids, timeoutMs: 15_000);

            Console.WriteLine($"  Export complete. {slideCount - errors}/{slideCount} slides exported.");
            return errors > 0
                ? PowerPointExportResult.Failed(PowerPointExportFailureKind.ExportFailed, slideCount - errors, slideCount)
                : PowerPointExportResult.Success(slideCount);
        }
        catch (PowerPointPrerequisiteException ex)
        {
            Console.Error.WriteLine($"PowerPoint prerequisite unavailable: {ex.Message}");
            Console.Error.WriteLine("  This is a machine prerequisite failure, not a FreeP WPF/Avalonia render or image-diff failure.");
            return PowerPointExportResult.Failed(PowerPointExportFailureKind.ComUnavailable, 0, 0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PowerPoint export failed: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine("  PowerPoint automation failed; FreeP WPF/Avalonia render status is reported separately by compare modes.");
            return PowerPointExportResult.Failed(PowerPointExportFailureKind.ExportFailed, 0, 0);
        }
        finally
        {
            // Belt-and-suspenders cleanup: always try to close + quit in finally
            ClosePresentation(ref presentation);
            QuitApplication(ref app);
            WaitForPowerPointToExit(ownedPids, timeoutMs: 10_000);
            KillPowerPointProcesses(ownedPids);
        }
    }

    // -----------------------------------------------------------------------
    // PowerPoint COM lifecycle
    // -----------------------------------------------------------------------

    private static dynamic CreatePowerPointApplication()
    {
        var type = Type.GetTypeFromProgID(ProgId)
            ?? throw new PowerPointPrerequisiteException($"COM ProgID '{ProgId}' is not registered. Is PowerPoint installed?");

        Exception? lastEx = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var instance = Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException("PowerPoint.Application COM activation returned null.");

                dynamic app = instance;

                // Suppress all user-facing dialogs
                app.DisplayAlerts = PpAlertsNone;

                // Note: app.Visible = false raises "Invalid request" on some PP builds.
                // Window visibility is controlled per-presentation via WithWindow=msoFalse
                // passed to Presentations.Open/Add.

                return app;
            }
            catch (Exception ex) when (attempt < 3)
            {
                lastEx = ex;
                Console.Error.WriteLine($"  COM activation attempt {attempt} failed: {ex.Message}  Retrying...");
                Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                lastEx = ex;
            }
        }

        throw new PowerPointPrerequisiteException(
            $"PowerPoint.Application COM activation failed after retries: {lastEx?.Message}", lastEx);
    }

    private static dynamic OpenPresentation(dynamic app, string pptxPath)
    {
        // Presentations.Open(FileName, ReadOnly, Untitled, WithWindow)
        //   ReadOnly  = msoTrue  (-1) — open read-only so no "save changes?" prompt
        //   Untitled  = msoFalse (0)  — keep original file name
        //   WithWindow = msoFalse (0) — do NOT show the presentation window
        return app.Presentations.Open(
            pptxPath,
            MsoTrue,   // ReadOnly
            MsoFalse,  // Untitled
            MsoFalse); // WithWindow
    }

    private static void ClosePresentation(ref dynamic? presentation)
    {
        if (presentation is null)
            return;

        try
        {
            presentation.Close();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: presentation.Close() threw: {ex.Message}");
        }
        finally
        {
            ReleaseComObject(presentation);
            presentation = null;
        }
    }

    private static void QuitApplication(ref dynamic? app)
    {
        if (app is null)
            return;

        try
        {
            app.Quit();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: app.Quit() threw: {ex.Message}");
        }
        finally
        {
            ReleaseComObject(app);
            app = null;
        }
    }

    // -----------------------------------------------------------------------
    // Process-level cleanup (mirrors ChartInteropCompare pattern)
    // -----------------------------------------------------------------------

    private static HashSet<int> GetPowerPointProcessIds() =>
        Process.GetProcessesByName(PowerPointProcessName)
               .Select(p => p.Id)
               .ToHashSet();

    private static void WaitForPowerPointToExit(HashSet<int> ownedPids, int timeoutMs)
    {
        if (ownedPids.Count == 0)
            return;

        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var anyRunning = Process.GetProcessesByName(PowerPointProcessName)
                                    .Any(p => ownedPids.Contains(p.Id));
            if (!anyRunning)
                return;

            Thread.Sleep(300);
        }
    }

    private static void KillPowerPointProcesses(HashSet<int> ownedPids)
    {
        if (ownedPids.Count == 0)
            return;

        foreach (var p in Process.GetProcessesByName(PowerPointProcessName))
        {
            if (!ownedPids.Contains(p.Id))
                continue;

            try
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5_000);
                Console.WriteLine($"  Killed orphan POWERPNT PID {p.Id}.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Failed to kill POWERPNT PID {p.Id}: {ex.Message}");
            }
        }
    }

    private static void ReleaseComObject(object? obj)
    {
        if (obj is null)
            return;

        try
        {
            if (Marshal.IsComObject(obj))
                Marshal.FinalReleaseComObject(obj);
        }
        catch (Exception)
        {
            // Best effort; the process-kill safety net handles lingering instances.
        }
    }
}

internal sealed record PowerPointExportResult(
    int ExitCode,
    PowerPointExportFailureKind FailureKind,
    int ExportedSlides,
    int TotalSlides)
{
    internal static PowerPointExportResult Success(int totalSlides) =>
        new(0, PowerPointExportFailureKind.None, totalSlides, totalSlides);

    internal static PowerPointExportResult Failed(PowerPointExportFailureKind failureKind, int exportedSlides, int totalSlides) =>
        new(1, failureKind, exportedSlides, totalSlides);
}

internal sealed record PowerPointComAvailability(
    string ProgId,
    bool IsRegistered,
    string MachineName,
    DateTimeOffset CheckedAtUtc,
    string? UnavailableReason)
{
    internal static PowerPointComAvailability Available(string progId, DateTimeOffset checkedAtUtc, string machineName) =>
        new(progId, IsRegistered: true, machineName, checkedAtUtc, UnavailableReason: null);

    internal static PowerPointComAvailability Unavailable(
        string progId,
        DateTimeOffset checkedAtUtc,
        string machineName,
        string unavailableReason) =>
        new(progId, IsRegistered: false, machineName, checkedAtUtc, unavailableReason);
}

internal enum PowerPointExportFailureKind
{
    None,
    ComUnavailable,
    ExportFailed
}

internal sealed class PowerPointPrerequisiteException : InvalidOperationException
{
    internal PowerPointPrerequisiteException(string message)
        : base(message)
    {
    }

    internal PowerPointPrerequisiteException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
