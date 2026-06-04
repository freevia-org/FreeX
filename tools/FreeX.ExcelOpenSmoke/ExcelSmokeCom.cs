using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal static class ExcelSmokeCom
{
    private const uint RpcCallRejectedHResult = 0x80010001u;
    private const uint RpcServerCallRetryLaterHResult = 0x8001010Au;
    private static readonly TimeSpan ExcelBusyRetryTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ExcelReadyTimeout = TimeSpan.FromSeconds(30);

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

    public static IDisposable RegisterExcelBusyMessageFilter()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            return NullDisposable.Instance;

        try
        {
            var filter = new ExcelBusyMessageFilter();
            var hr = CoRegisterMessageFilter(filter, out var previousFilter);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            return new ExcelBusyMessageFilterRegistration(filter, previousFilter);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Excel COM message filter registration failed: {ex.Message}");
            return NullDisposable.Instance;
        }
    }

    public static T WithExcelBusyRetry<T>(Func<T> action, string operation)
    {
        var deadline = DateTimeOffset.UtcNow + ExcelBusyRetryTimeout;
        var delayMilliseconds = 250;

        while (true)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (IsTransientExcelBusyException(ex))
            {
                if (DateTimeOffset.UtcNow >= deadline)
                    throw new InvalidDataException(
                        $"{operation} did not complete because Excel stayed busy for {ExcelBusyRetryTimeout.TotalSeconds:0} second(s).",
                        ex);

                Thread.Sleep(delayMilliseconds);
                delayMilliseconds = Math.Min(delayMilliseconds * 2, 1000);
            }
        }
    }

    public static void WaitForExcelReady(object excelApp)
    {
        var deadline = DateTimeOffset.UtcNow + ExcelReadyTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                if (Convert.ToBoolean(((dynamic)excelApp).Ready, CultureInfo.InvariantCulture))
                    return;
            }
            catch (COMException ex) when (IsTransientExcelBusyException(ex))
            {
                // Excel can reject Ready while it finishes opening/reopening; retry until bounded timeout.
            }

            Thread.Sleep(250);
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

    private static bool IsTransientExcelBusyException(Exception ex) =>
        ex switch
        {
            COMException comException => IsTransientExcelBusyHResult(comException.HResult),
            InvalidDataException { InnerException: { } inner } => IsTransientExcelBusyException(inner),
            _ => false
        };

    private static bool IsTransientExcelBusyHResult(int hresult)
    {
        var unsigned = (uint)hresult;
        return unsigned is RpcCallRejectedHResult or RpcServerCallRetryLaterHResult;
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

    private sealed class ExcelBusyMessageFilterRegistration(
        ExcelBusyMessageFilter filter,
        IOleMessageFilter? previousFilter)
        : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            try
            {
                _ = CoRegisterMessageFilter(previousFilter, out _);
            }
            catch
            {
                // Best-effort cleanup; Excel process cleanup still runs after smoke validation.
            }

            GC.KeepAlive(filter);
        }
    }

    private sealed class ExcelBusyMessageFilter : IOleMessageFilter
    {
        private const int ServerCallIsHandled = 0;
        private const int ServerCallRetryLater = 2;
        private const int PendingMessageWaitDefaultProcess = 2;
        private const int RetryImmediately = 99;
        private const int CancelCall = -1;

        public int HandleInComingCall(
            int callType,
            IntPtr taskCaller,
            int tickCount,
            IntPtr interfaceInfo) =>
            ServerCallIsHandled;

        public int RetryRejectedCall(
            IntPtr taskCallee,
            int tickCount,
            int rejectType) =>
            rejectType == ServerCallRetryLater && tickCount < ExcelBusyRetryTimeout.TotalMilliseconds
                ? RetryImmediately
                : CancelCall;

        public int MessagePending(
            IntPtr taskCallee,
            int tickCount,
            int pendingType) =>
            PendingMessageWaitDefaultProcess;
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        private NullDisposable()
        {
        }

        public void Dispose()
        {
        }
    }

    [ComImport]
    [Guid("00000016-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleMessageFilter
    {
        [PreserveSig]
        int HandleInComingCall(
            int callType,
            IntPtr taskCaller,
            int tickCount,
            IntPtr interfaceInfo);

        [PreserveSig]
        int RetryRejectedCall(
            IntPtr taskCallee,
            int tickCount,
            int rejectType);

        [PreserveSig]
        int MessagePending(
            IntPtr taskCallee,
            int tickCount,
            int pendingType);
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterMessageFilter(
        IOleMessageFilter? newFilter,
        out IOleMessageFilter? oldFilter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
}
