using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

internal static class ExcelSmokeCom
{
    public static void TrySetAutomationSecurity(dynamic excelApp)
    {
        try
        {
            excelApp.AutomationSecurity = 3;
        }
        catch
        {
            // Older Excel builds can reject this property; DisplayAlerts=false still covers the smoke.
        }
    }

    public static HashSet<int> GetExcelProcessIds() =>
        Process.GetProcessesByName("EXCEL")
            .Select(process =>
            {
                using (process)
                    return process.Id;
            })
            .ToHashSet();

    public static int? TryGetExcelProcessId(object excel)
    {
        try
        {
            var hwnd = Convert.ToInt64(((dynamic)excel).Hwnd, CultureInfo.InvariantCulture);
            if (hwnd == 0)
                return null;

            _ = GetWindowThreadProcessId(new IntPtr(hwnd), out var processId);
            return processId == 0 ? null : processId;
        }
        catch
        {
            return null;
        }
    }

    public static void KillOrphanExcelProcesses(HashSet<int> baselineExcelPids, int? excelPid)
    {
        var candidatePids = new HashSet<int>();
        if (excelPid is { } trackedPid && !baselineExcelPids.Contains(trackedPid))
            candidatePids.Add(trackedPid);

        foreach (var process in Process.GetProcessesByName("EXCEL"))
        {
            using (process)
            {
                if (!baselineExcelPids.Contains(process.Id))
                    candidatePids.Add(process.Id);
            }
        }

        foreach (var pid in candidatePids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
                Console.WriteLine($"Killed orphan EXCEL PID {pid}.");
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
            catch (InvalidOperationException)
            {
                // Process already exited.
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to kill orphan EXCEL PID {pid}: {ex.Message}");
            }
        }
    }

    public static void ReleaseComObject(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
            return;

        try
        {
            Marshal.FinalReleaseComObject(value);
        }
        catch
        {
            // Cleanup best effort; orphaned Excel processes are handled separately.
        }
    }

    public static void CollectComReferences()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
}
