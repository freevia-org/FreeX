namespace FreeX.App.UI;

/// <summary>
/// Reporting seam for faults caught inside the grid's render pass.
/// <para>
/// An exception escaping <c>OnRender</c> is fatal in WPF, and because the render pass re-runs on
/// every paint a content-driven fault (a malformed chart, a bad image) would crash the app over and
/// over with no way back. The render pass therefore catches such faults and degrades that layer
/// instead. Swallowing them silently would hide real bugs, so each distinct fault is routed here;
/// the host wires <see cref="Handler"/> to the app diagnostics/crash pipeline at startup so the
/// fault still lands in the local crash report and (when configured) the remote tracker.
/// </para>
/// <para>
/// Reports are de-duplicated per distinct fault: a persistent bad chart faults on every paint, and
/// an unthrottled report would spam thousands of diagnostics files. The first occurrence of each
/// distinct (stage, exception type, originating frame) is reported and later repeats are dropped.
/// </para>
/// </summary>
public static class GridRenderFaultReporter
{
    private const int MaxDistinctReports = 20;

    private static readonly object Gate = new();
    private static readonly HashSet<string> ReportedSignatures = new(StringComparer.Ordinal);

    /// <summary>
    /// Receives (exception, stage) for the first occurrence of each distinct render fault.
    /// Set once at startup by the host; must not throw.
    /// </summary>
    public static Action<Exception, string>? Handler { get; set; }

    /// <summary>Reports a caught render fault, de-duplicated by its signature.</summary>
    public static void Report(Exception exception, string stage)
    {
        if (exception is null)
            return;

        var handler = Handler;
        if (handler is null)
            return;

        if (!ShouldReport(exception, stage))
            return;

        try
        {
            handler(exception, stage);
        }
        catch
        {
            // Diagnostics must never turn a degraded render into a crash.
        }
    }

    /// <summary>Resets the de-duplication state. Test hook.</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            ReportedSignatures.Clear();
        }
    }

    private static bool ShouldReport(Exception exception, string stage)
    {
        // The originating frame keeps two different faults from the same stage distinguishable
        // while still collapsing the same fault repeating across paints.
        var firstFrame = exception.StackTrace?.Split('\n', 2)[0].Trim() ?? string.Empty;
        var signature = $"{stage}|{exception.GetType().FullName}|{firstFrame}";

        lock (Gate)
        {
            if (ReportedSignatures.Count >= MaxDistinctReports)
                return false;

            return ReportedSignatures.Add(signature);
        }
    }
}
