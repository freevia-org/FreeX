using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// Runs a test body on the headless UI thread, skipping only when this environment has no headless
/// drawing backend at all.
/// <para>
/// The hand-rolled <c>OnUiThread</c> helpers this replaces each wrapped the dispatch in
/// <c>catch (Exception) { return false; }</c> with a "headless drawing unavailable" comment — but that
/// catch cannot tell a missing backend from a FAILED ASSERTION, so every <c>if (!ran) return;</c> guard
/// turned an assertion failure inside the dispatched body into a silently PASSING test. Same defect,
/// and the same fix, as <c>FreeW.App.Avalonia.Tests.HeadlessUiThread</c>.
/// </para>
/// </summary>
internal static class HeadlessUiThread
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    /// <summary>
    /// r360: the probe's failure is KEPT, not discarded -- see the FreeW helper of the same name for
    /// the reasoning. It catches everything, so a real regression in window creation is
    /// indistinguishable from "this machine has no drawing backend", and every
    /// <c>if (!ran) return;</c> in this assembly then returns before asserting.
    /// <c>R360_HeadlessBackendIsAvailableTests</c> turns that into one loud failure carrying the real
    /// cause.
    /// </summary>
    private static readonly Lazy<Exception?> BackendProbeFailure = new(() =>
    {
        try
        {
            Session.Dispatch(
                () =>
                {
                    var window = new Window { Width = 200, Height = 120 };
                    window.Show();
                    window.Close();
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
    /// The asynchronous counterpart of <see cref="Run"/>. Uses the <c>Func&lt;Task&lt;T&gt;&gt;</c>
    /// dispatch overload deliberately: binding an async lambda to the <c>Action</c> overload makes it
    /// async void, which returns before the body finishes and lets a test pass without ever running its
    /// assertions.
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
