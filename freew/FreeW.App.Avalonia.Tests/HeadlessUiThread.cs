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
/// <c>Dispatch(async () =&gt; …)</c> async-void binding that once let 154 tests pass without executing.
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

    private static readonly Lazy<bool> BackendAvailable = new(() =>
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
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    });

    /// <summary>
    /// Dispatches <paramref name="action"/> to the headless UI thread. Returns false — without running it
    /// — when no headless drawing backend exists here; otherwise returns true, and anything the action
    /// throws surfaces to the test.
    /// </summary>
    internal static async Task<bool> Run(Action action)
    {
        if (!BackendAvailable.Value)
            return false;

        await Session.Dispatch(action, CancellationToken.None);
        return true;
    }
}
