using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

// Drives desktop Excel via COM to inventory a workbook and read its computed cell values. One Excel
// instance is reused for the whole batch and torn down by Shutdown(); orphan EXCEL PIDs are killed.
internal static class ExcelInspector
{
    private const string ExcelProcessName = "EXCEL";
    private const int MaxCellsPerSheet = 250_000; // guard against pathological used ranges

    private static object? _excel;
    private static HashSet<int> _ownedPids = [];

    public static void Inspect(string path, FileResult result, FidelityOptions options)
    {
        object? workbookObject = null;
        try
        {
            dynamic workbook = OpenWithRetry(path);
            workbookObject = workbook;

            var inv = new Inventory();
            try { inv.NamedRanges = (int)workbook.Names.Count; } catch { }
            try { inv.Charts += (int)workbook.Charts.Count; } catch { } // chart sheets

            var worksheetCount = (int)workbook.Worksheets.Count;
            inv.Sheets = worksheetCount;
            for (var s = 1; s <= worksheetCount; s++)
            {
                object? wsObject = null;
                object? chartObjectsRcw = null;
                object? pivotTablesRcw = null;
                object? listObjectsRcw = null;
                object? hyperlinksRcw = null;
                object? commentsRcw = null;
                try
                {
                    dynamic ws = workbook.Worksheets.Item(s);
                    wsObject = ws;
                    string sheetName = Convert.ToString(ws.Name, CultureInfo.InvariantCulture) ?? $"Sheet{s}";

                    try
                    {
                        var co = ws.ChartObjects();
                        chartObjectsRcw = co;
                        inv.Charts += InvokeWithComRetry(() => (int)co.Count, "ChartObjects.Count");
                    }
                    catch { }
                    try
                    {
                        var pt = ws.PivotTables();
                        pivotTablesRcw = pt;
                        inv.PivotTables += InvokeWithComRetry(() => (int)pt.Count, "PivotTables.Count");
                    }
                    catch { }
                    try
                    {
                        var lo = ws.ListObjects;
                        listObjectsRcw = lo;
                        inv.Tables += InvokeWithComRetry(() => (int)lo.Count, "ListObjects.Count");
                    }
                    catch { }
                    try
                    {
                        var hl = ws.Hyperlinks;
                        hyperlinksRcw = hl;
                        inv.Hyperlinks += InvokeWithComRetry(() => (int)hl.Count, "Hyperlinks.Count");
                    }
                    catch { }
                    try
                    {
                        var cm = ws.Comments;
                        commentsRcw = cm;
                        inv.Comments += InvokeWithComRetry(() => (int)cm.Count, "Comments.Count");
                    }
                    catch { }

                    var cells = ReadSheetValues(ws);
                    var key = result.ExcelCells.ContainsKey(sheetName) ? $"{sheetName}#{s}" : sheetName;
                    result.ExcelCells[key] = cells;
                }
                finally
                {
                    // Release per-sheet RCWs so they don't accumulate across the batch and destabilize the
                    // shared Excel instance. Pattern mirrors TryGetExcelPivotTableRange in SheetGridImageCompare.
                    try { if (commentsRcw is not null && Marshal.IsComObject(commentsRcw)) Marshal.FinalReleaseComObject(commentsRcw); } catch { }
                    try { if (hyperlinksRcw is not null && Marshal.IsComObject(hyperlinksRcw)) Marshal.FinalReleaseComObject(hyperlinksRcw); } catch { }
                    try { if (listObjectsRcw is not null && Marshal.IsComObject(listObjectsRcw)) Marshal.FinalReleaseComObject(listObjectsRcw); } catch { }
                    try { if (pivotTablesRcw is not null && Marshal.IsComObject(pivotTablesRcw)) Marshal.FinalReleaseComObject(pivotTablesRcw); } catch { }
                    try { if (chartObjectsRcw is not null && Marshal.IsComObject(chartObjectsRcw)) Marshal.FinalReleaseComObject(chartObjectsRcw); } catch { }
                    try { if (wsObject is not null && Marshal.IsComObject(wsObject)) Marshal.FinalReleaseComObject(wsObject); } catch { }
                }
            }

            result.Excel = inv;
            workbook.Close(false);
        }
        catch
        {
            TryCloseWorkbook(workbookObject);
            throw;
        }
        finally
        {
            // Release the workbook RCW so COM/Excel memory does not accumulate across the batch (the
            // accumulation is what eventually crashes the shared instance).
            try { if (workbookObject is not null && Marshal.IsComObject(workbookObject)) Marshal.FinalReleaseComObject(workbookObject); } catch { }
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    // Opens a workbook, recreating Excel if the shared instance has died. The first Open after activation
    // can race COM-server init ("Unable to get the Open property" / RPC busy); a long batch can also crash
    // the instance ("RPC server is unavailable", 0x800706BA), after which every later Open fails until we
    // spawn a fresh one. Positional args only — named-argument binding uses locale-sensitive GetIDsOfNames
    // and fails on a non-English Excel UI. Open(Filename, UpdateLinks=0, ReadOnly=true).
    private static dynamic OpenWithRetry(string path)
    {
        Exception? last = null;
        // The first Open on a cold Excel can fail ("Unable to get the Open property") for several seconds
        // even after the cheap warm-up, so allow generous patience here.
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            try
            {
                dynamic app = GetOrCreateExcel();
                // Open(Filename, UpdateLinks=0). Read-write (not ReadOnly): some files Excel wants to
                // repair-on-open cannot be opened read-only. We never save, and Close(false) discards.
                return app.Workbooks.Open(path, 0);
            }
            catch (Exception ex)
            {
                last = ex;
                if (LooksLikeDeadServer(ex))
                    ResetExcel();
                System.Threading.Thread.Sleep(Math.Min(attempt * 800, 2500));
            }
        }
        throw new InvalidOperationException($"Workbooks.Open failed after retries: {last?.Message}", last);
    }

    private static bool LooksLikeDeadServer(Exception ex)
    {
        var hr = (uint)ex.HResult;
        return hr is 0x800706BA   // RPC server is unavailable
                  or 0x80010108   // RPC_E_DISCONNECTED
                  or 0x800706BE   // RPC call failed
            || ex.Message.Contains("RPC", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<(int Row, int Col), CellVal> ReadSheetValues(dynamic ws)
    {
        var cells = new Dictionary<(int, int), CellVal>();
        object? usedObject = null;
        try
        {
            dynamic used;
            try { used = InvokeWithComRetry(() => ws.UsedRange, "UsedRange"); } catch { return cells; }
            usedObject = used;
            int rows = InvokeWithComRetry(() => (int)used.Rows.Count, "UsedRange.Rows.Count");
            int cols = InvokeWithComRetry(() => (int)used.Columns.Count, "UsedRange.Columns.Count");
            if (rows <= 0 || cols <= 0 || (long)rows * cols > MaxCellsPerSheet)
                return cells;

            int top = InvokeWithComRetry(() => (int)used.Row, "UsedRange.Row");
            int left = InvokeWithComRetry(() => (int)used.Column, "UsedRange.Column");
            object value2 = InvokeWithComRetry(() => (object)used.Value2, "UsedRange.Value2");
            if (value2 is null)
                return cells;

            if (value2 is object[,] grid)
            {
                // Excel's Value2 array is 1-based [1..rows, 1..cols].
                for (var r = 1; r <= rows; r++)
                for (var c = 1; c <= cols; c++)
                {
                    var cell = Normalize(grid[r, c]);
                    if (!cell.IsEmpty)
                        cells[(top + r - 1, left + c - 1)] = cell;
                }
            }
            else
            {
                var single = Normalize(value2);
                if (!single.IsEmpty)
                    cells[(top, left)] = single;
            }

            return cells;
        }
        finally
        {
            try { if (usedObject is not null && Marshal.IsComObject(usedObject)) Marshal.FinalReleaseComObject(usedObject); } catch { }
        }
    }

    private static T InvokeWithComRetry<T>(Func<T> action, string operation)
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
                System.Threading.Thread.Sleep(Math.Min(attempt * 500, 2500));
            }
        }

        throw new InvalidOperationException($"{operation} failed after Excel busy retries: {last?.Message}", last);
    }

    private static bool LooksLikeExcelBusy(Exception ex)
    {
        var hr = (uint)ex.HResult;
        return hr is 0x80010001 // RPC_E_CALL_REJECTED
                  or 0x8001010A // RPC_E_SERVERCALL_RETRYLATER
            || ex.Message.Contains("0x80010001", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Call was rejected", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("server is busy", StringComparison.OrdinalIgnoreCase);
    }

    private static CellVal Normalize(object? raw)
    {
        // Excel surfaces error cells through Value2 as CVErr codes (large negative Int32s, e.g.
        // -2146826281 = #DIV/0!). Without this they would be read as ordinary numbers and mismatch against
        // FreeX's error values — the dominant false positive on formula fixtures.
        if (TryExcelError(raw, out var symbol))
            return CellVal.FromError(symbol);

        return raw switch
        {
            null => CellVal.Blank,
            double d => CellVal.FromNumber(d),
            bool b => CellVal.FromBool(b),
            string s => CellVal.FromText(s),
            int i => CellVal.FromNumber(i),
            _ => CellVal.FromError(Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "#ERR"),
        };
    }

    private static bool TryExcelError(object? raw, out string symbol)
    {
        symbol = "";
        long code;
        if (raw is int i) code = i;
        else if (raw is double d && d <= -2_146_826_240 && d == Math.Floor(d)) code = (long)d;
        else return false;

        symbol = code switch
        {
            -2146826288 => "#NULL!",
            -2146826281 => "#DIV/0!",
            -2146826273 => "#VALUE!",
            -2146826265 => "#REF!",
            -2146826259 => "#NAME?",
            -2146826252 => "#NUM!",
            -2146826246 => "#N/A",
            -2146826245 => "#GETTING_DATA",
            _ => "",
        };
        return symbol.Length > 0;
    }

    private static object GetOrCreateExcel()
    {
        if (_excel is not null)
            return _excel;

        var baseline = GetExcelPids();
        var excelType = Type.GetTypeFromProgID("Excel.Application")
            ?? throw new InvalidOperationException("Excel.Application COM registration not found — is desktop Excel installed?");
        var excel = Activator.CreateInstance(excelType)
            ?? throw new InvalidOperationException("Excel.Application activation returned null.");
        dynamic app = excel;
        app.Visible = false;
        app.DisplayAlerts = false;
        TrySet(app, "EnableEvents", false);
        TrySet(app, "AutomationSecurity", 3);
        TrySet(app, "AskToUpdateLinks", false);
        TrySet(app, "ScreenUpdating", false);
        _excel = excel;
        _ownedPids.UnionWith(GetExcelPids().Except(baseline)); // accumulate across recreations for cleanup

        // Warm up: poll a cheap member until the COM server stops reporting "busy". The very first
        // Workbooks.Open on a cold process can still fail ("Unable to get the Open property"); Program
        // retries any such skip at the end of the batch, once Excel is fully warm.
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try { _ = (int)app.Workbooks.Count; break; }
            catch { System.Threading.Thread.Sleep(500); }
        }
        return excel;
    }

    // Tear down a dead/unresponsive Excel instance so the next GetOrCreateExcel spawns a fresh one.
    private static void ResetExcel()
    {
        var dead = _excel;
        _excel = null;
        if (dead is not null)
        {
            try { ((dynamic)dead).Quit(); } catch { }
            try { if (Marshal.IsComObject(dead)) Marshal.FinalReleaseComObject(dead); } catch { }
        }
        foreach (var p in Process.GetProcessesByName(ExcelProcessName).Where(p => _ownedPids.Contains(p.Id)))
        {
            try { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } catch { }
        }
    }

    public static void Shutdown()
    {
        if (_excel is not null)
        {
            try { ((dynamic)_excel).Quit(); } catch { }
            try { if (Marshal.IsComObject(_excel)) Marshal.FinalReleaseComObject(_excel); } catch { }
            _excel = null;
        }

        var deadline = Environment.TickCount64 + 3000;
        while (Environment.TickCount64 < deadline && Process.GetProcessesByName(ExcelProcessName).Any(p => _ownedPids.Contains(p.Id)))
            System.Threading.Thread.Sleep(200);
        foreach (var p in Process.GetProcessesByName(ExcelProcessName).Where(p => _ownedPids.Contains(p.Id)))
        {
            try { p.Kill(entireProcessTree: true); p.WaitForExit(5000); } catch { }
        }
    }

    private static HashSet<int> GetExcelPids() =>
        Process.GetProcessesByName(ExcelProcessName).Select(p => p.Id).ToHashSet();

    private static void TrySet(dynamic app, string name, object value)
    {
        try
        {
            app.GetType().InvokeMember(name, System.Reflection.BindingFlags.SetProperty, null, app,
                new[] { value }, CultureInfo.InvariantCulture);
        }
        catch { }
    }

    private static void TryCloseWorkbook(object? workbook)
    {
        if (workbook is null) return;
        try { ((dynamic)workbook).Close(false); } catch { }
    }
}
