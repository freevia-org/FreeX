namespace FreeW.App.Host.Editing;

/// <summary>
/// Opt-in timing diagnostics for the paged editor (commit, re-flow, background slices, hidden-editor
/// refresh). Off by default -- nothing is timed or formatted; the performance probe turns it on.
/// </summary>
internal static class PagedEditDiagnostics
{
    /// <summary>When set, the paged editor records where its re-flows and commits spend their time.</summary>
    internal static bool Enabled { get; set; }

    /// <summary>A started stopwatch while diagnostics are enabled, else null.</summary>
    internal static System.Diagnostics.Stopwatch? StartClock() =>
        Enabled ? System.Diagnostics.Stopwatch.StartNew() : null;
}
