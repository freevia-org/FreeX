using FluentAssertions;
using FreeW.App.Avalonia;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r296: closes -- and corrects -- a gap this program recorded twice and then carried forward
/// without re-checking.
///
/// <para>r282 noted that FreeW dialog failures were silent. r283 fixed it by giving
/// <see cref="AvaloniaUiTaskGuard"/> an app-wide fallback reporter that <c>MainWindow</c> installs.
/// r295 nevertheless still listed "five call sites pass no explicit reporter" as an open gap. They
/// do pass none -- and since r283 that is FINE, because the guard falls back. The note was stale,
/// not the code.</para>
///
/// <para>r283's tests drove <c>ObserveAsync</c>. The five call sites call <c>Run</c>, the
/// fire-and-forget overload, so that is what these pin: the same reporting, reached the way the real
/// callers reach it. A recorded gap is a note to re-examine, which is the lesson r292 wrote down and
/// r295 then failed to apply to its own list.</para>
/// </summary>
public sealed class R296_UiTaskGuardRunReportsThroughTheFallbackTests : IDisposable
{
    private readonly Action<Exception>? _previous = AvaloniaUiTaskGuard.FallbackFailureReporter;

    public void Dispose() => AvaloniaUiTaskGuard.FallbackFailureReporter = _previous;

    /// <summary>
    /// Run is fire-and-forget, so the assertion waits for the reporter rather than assuming the
    /// continuation has already run -- a bare assert here would pass or fail on timing.
    /// </summary>
    private static async Task<Exception?> CaptureAsync(Action<TaskCompletionSource<Exception>> act)
    {
        var reported = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        act(reported);

        var completed = await Task.WhenAny(reported.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        return ReferenceEquals(completed, reported.Task) ? await reported.Task : null;
    }

    [Fact]
    public async Task RunWithNoReporterStillReachesTheFallback()
    {
        var failure = await CaptureAsync(reported =>
        {
            AvaloniaUiTaskGuard.FallbackFailureReporter = ex => reported.TrySetResult(ex);
            AvaloniaUiTaskGuard.Run(() => throw new InvalidOperationException("boom"));
        });

        failure.Should().NotBeNull(
            "the dialog call sites use this overload and pass no onFailure; before r283 the guard "
            + "caught the exception and dropped it, and the button silently did nothing");
        failure!.Message.Should().Be("boom");
    }

    [Fact]
    public async Task RunWithAnExplicitReporterDoesNotAlsoHitTheFallback()
    {
        Exception? fallback = null;

        var failure = await CaptureAsync(reported =>
        {
            AvaloniaUiTaskGuard.FallbackFailureReporter = ex => fallback = ex;
            AvaloniaUiTaskGuard.Run(
                () => throw new InvalidOperationException("boom"),
                ex => reported.TrySetResult(ex));
        });

        failure.Should().NotBeNull();
        fallback.Should().BeNull("reporting twice would show the user the same failure twice");
    }

    [Fact]
    public async Task RunDoesNotReportACancelledOperation()
    {
        var failure = await CaptureAsync(reported =>
        {
            AvaloniaUiTaskGuard.FallbackFailureReporter = ex => reported.TrySetResult(ex);
            AvaloniaUiTaskGuard.Run(() => throw new OperationCanceledException());
        });

        failure.Should().BeNull("dismissing a picker is a normal path, not an error to display");
    }
}
