using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Runs a test body on the headless UI thread, skipping only when this environment has no headless
/// drawing backend at all.
/// <para>
/// The 23 hand-rolled <c>OnUiThread</c> helpers this replaces each wrapped the dispatch in
/// <c>catch (Exception) { return false; }</c> with a "no headless drawing backend" comment — but that
/// catch does not distinguish a missing backend from a FAILED ASSERTION. Every one of the ~700
/// <c>if (!ran) return;</c> guards therefore turned an assertion failure inside the dispatched body into
/// a silently PASSING test: the exception was swallowed, <c>ran</c> came back false, and the test
/// returned before its remaining assertions ran. Same class of defect as the
/// <c>Dispatch</c> async-lambda overload binding that once let 154 tests pass without executing.
/// </para>
/// <para>
/// The backend is probed ONCE instead, so the original skip intent survives, and the body's own
/// exceptions — assertion failures included — propagate and fail the test.
/// </para>
/// </summary>
internal static class HeadlessUiThread
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    /// <summary>
    /// r360: the probe's failure is KEPT, not discarded. It catches everything, so a genuine
    /// regression in <c>DocumentView.LoadDocument</c> or <c>Measure</c> reads as "no backend" and
    /// every <c>if (!ran) return;</c> in this assembly -- over a thousand of them -- turns into a
    /// silent pass. That is the same swallow this class was written to remove, one level up.
    /// Holding the exception lets <c>R360_HeadlessBackendIsAvailableTests</c> fail ONCE, loudly, with
    /// the real cause attached, instead of the suite going green having run nothing.
    /// </summary>
    private static readonly Lazy<Exception?> BackendProbeFailure = new(() =>
    {
        try
        {
            Session.Dispatch(
                () =>
                {
                    var view = new DocumentView();
                    view.LoadDocument(TextDocument.CreateEmpty());
                    view.Measure(new Size(816, 200));
                },
                CancellationToken.None).GetAwaiter().GetResult();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    });

    /// <summary>The probe's exception, or <see langword="null"/> when the backend came up.</summary>
    internal static Exception? BackendFailure => BackendProbeFailure.Value;

    private static bool BackendAvailableValue => BackendProbeFailure.Value is null;

    /// <summary>
    /// Dispatches <paramref name="action"/> to the headless UI thread. Returns false — without running it
    /// — when no headless drawing backend exists here; otherwise returns true, and anything the action
    /// throws surfaces to the test.
    /// </summary>
    internal static async Task<bool> Run(Action action)
    {
        if (!BackendAvailableValue)
            return false;

        await Session.Dispatch(action, CancellationToken.None);
        return true;
    }

    /// <summary>
    /// The asynchronous counterpart of <see cref="Run"/>, for bodies that await. Uses the
    /// <c>Func&lt;Task&lt;T&gt;&gt;</c> dispatch overload deliberately: binding an async lambda to the
    /// <c>Action</c> overload makes it async void, which returns before the body finishes and lets a test
    /// pass without ever running its assertions.
    /// </summary>
    internal static async Task<bool> RunAsync(Func<Task> action)
    {
        if (!BackendAvailableValue)
            return false;

        await Session.Dispatch(
            async () =>
            {
                await action();
                return true;
            },
            CancellationToken.None);
        return true;
    }
}
