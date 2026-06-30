using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace FreeX.ToolsShared.Wpf;

public static class ExcelComAutomation
{
    private const string ExcelProcessName = "EXCEL";
    private const uint RpcCallRejectedHResult = 0x80010001u;
    private const uint RpcServerCallRetryLaterHResult = 0x8001010Au;
    private static readonly TimeSpan ExcelBusyRetryTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ExcelReadyTimeout = TimeSpan.FromSeconds(30);

    public static object CreateExcelApplication(
        string registrationMissingMessage,
        string activationNullMessage)
    {
        var excelType = Type.GetTypeFromProgID("Excel.Application")
            ?? throw new InvalidOperationException(registrationMissingMessage);
        return Activator.CreateInstance(excelType)
            ?? throw new InvalidOperationException(activationNullMessage);
    }

    public static object CreateExcelApplicationWithRetry(
        string registrationMissingMessage,
        string activationNullMessage,
        int maxAttempts,
        int retryDelayMilliseconds,
        string failureMessagePrefix,
        Action<dynamic>? configure = null)
    {
        var excelType = Type.GetTypeFromProgID("Excel.Application")
            ?? throw new InvalidOperationException(registrationMissingMessage);
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var excel = Activator.CreateInstance(excelType)
                    ?? throw new InvalidOperationException(activationNullMessage);
                configure?.Invoke((dynamic)excel);
                return excel;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                lastException = ex;
                Thread.Sleep(retryDelayMilliseconds);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        throw new InvalidOperationException(
            $"{failureMessagePrefix}: {lastException?.Message}",
            lastException);
    }

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

    public static void TrySetProperty(dynamic target, string propertyName, object value)
    {
        try
        {
            var property = target.GetType().InvokeMember(
                propertyName,
                System.Reflection.BindingFlags.SetProperty,
                null,
                target,
                new[] { value },
                CultureInfo.InvariantCulture);
            _ = property;
        }
        catch
        {
            // Optional automation flags are best-effort for comparison and smoke tooling.
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

    public static T InvokeWithComRetry<T>(Func<T> action, string operation)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (LooksLikeExcelBusy(ex))
            {
                last = ex;
                Thread.Sleep(Math.Min(attempt * 500, 2500));
            }
        }

        throw new InvalidOperationException(
            $"{operation} failed after Excel busy retries: {last?.Message}",
            last);
    }

    public static bool LooksLikeExcelBusy(Exception ex)
    {
        var hr = (uint)ex.HResult;
        return hr is RpcCallRejectedHResult or RpcServerCallRetryLaterHResult
            || ex.Message.Contains("0x80010001", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Call was rejected", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("server is busy", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeDeadServer(Exception ex)
    {
        var hr = (uint)ex.HResult;
        return hr is 0x800706BA
                  or 0x80010108
                  or 0x800706BE
            || ex.Message.Contains("RPC", StringComparison.OrdinalIgnoreCase);
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
        Process.GetProcessesByName(ExcelProcessName)
            .Select(process =>
            {
                using (process)
                    return process.Id;
            })
            .ToHashSet();

    public static HashSet<int> GetNewExcelProcessIds(HashSet<int> baselineExcelPids) =>
        GetExcelProcessIds()
            .Except(baselineExcelPids)
            .ToHashSet();

    public static void WaitForExcelProcessesToExit(
        IEnumerable<int> processIds,
        int timeoutMilliseconds)
    {
        var candidatePids = processIds.ToHashSet();
        if (candidatePids.Count == 0)
            return;

        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (!GetExcelProcessIds().Overlaps(candidatePids))
                return;

            Thread.Sleep(250);
        }
    }

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

        foreach (var process in Process.GetProcessesByName(ExcelProcessName))
        {
            using (process)
            {
                if (!baselineExcelPids.Contains(process.Id))
                    candidatePids.Add(process.Id);
            }
        }

        KillExcelProcesses(candidatePids);
    }

    public static void KillExcelProcesses(
        IEnumerable<int> processIds,
        bool logKilled = true,
        bool logFailures = true)
    {
        var candidatePids = processIds.ToHashSet();
        if (candidatePids.Count == 0)
            return;

        foreach (var pid in candidatePids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
                if (logKilled)
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
                if (logFailures)
                    Console.Error.WriteLine($"Failed to kill orphan EXCEL PID {pid}: {ex.Message}");
            }
        }
    }

    public static void ReleaseComObject(object? value)
    {
        if (value is null)
            return;

        try
        {
            if (Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }
        catch
        {
            // Cleanup is best-effort for comparison and smoke tooling.
        }
    }

    public static void CollectComReferences()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public static void TryCloseWorkbook(object? workbook)
    {
        if (workbook is null)
            return;

        try
        {
            ((dynamic)workbook).Close(false);
        }
        catch
        {
            // Best effort during error cleanup.
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
