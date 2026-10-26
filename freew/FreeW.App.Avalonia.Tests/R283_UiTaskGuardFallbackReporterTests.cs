using FluentAssertions;
using FreeW.App.Avalonia;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r283: closes the residual r282 recorded rather than leaving it named-but-open.
///
/// <para>r282 routed four FreeW dialog funnels onto <see cref="AvaloniaUiTaskGuard"/>, which removed
/// the duplicated swallowing but did not make anything visible: a dialog has no status bar, so those
/// call sites pass no <c>onFailure</c> and the guard caught the exception and dropped it. The fix was
/// left open because inventing an error surface unverified would have been worse than saying so.</para>
///
/// <para>The surface that already exists is the shell's status line. The guard now falls back to an
/// app-wide reporter, and <c>MainWindow</c> installs its own status-line writer into it, so a failure
/// in a dialog is reported with the same wording as one raised in the shell.</para>
/// </summary>
public sealed class R283_UiTaskGuardFallbackReporterTests : IDisposable
{
    private readonly Action<Exception>? _previous = AvaloniaUiTaskGuard.FallbackFailureReporter;

    public void Dispose() => AvaloniaUiTaskGuard.FallbackFailureReporter = _previous;

    [Fact]
    public async Task AFailureWithNoCallerReporterReachesTheFallback()
    {
        Exception? reported = null;
        AvaloniaUiTaskGuard.FallbackFailureReporter = ex => reported = ex;

        await AvaloniaUiTaskGuard.ObserveAsync(() => throw new InvalidOperationException("boom"));

        reported.Should().NotBeNull(
            "a dialog supplies no onFailure, and before this the guard caught the exception and "
            + "dropped it -- the user saw a button that did nothing");
        reported!.Message.Should().Be("boom");
    }

    /// <summary>
    /// A caller that DOES report must keep owning the failure; the fallback is for the sites that
    /// have nowhere of their own to write, not a second reporter stapled onto every call.
    /// </summary>
    [Fact]
    public async Task AnExplicitReporterStillWinsAndTheFallbackDoesNotAlsoFire()
    {
        Exception? explicitly = null;
        Exception? fallback = null;
        AvaloniaUiTaskGuard.FallbackFailureReporter = ex => fallback = ex;

        await AvaloniaUiTaskGuard.ObserveAsync(
            () => throw new InvalidOperationException("boom"),
            ex => explicitly = ex);

        explicitly.Should().NotBeNull();
        fallback.Should().BeNull("the caller handled it, so reporting twice would double the message");
    }

    /// <summary>
    /// Cancellation is not a failure -- closing a picker must not paint an error into the status bar.
    /// </summary>
    [Fact]
    public async Task CancellationDoesNotReachTheFallback()
    {
        Exception? reported = null;
        AvaloniaUiTaskGuard.FallbackFailureReporter = ex => reported = ex;

        await AvaloniaUiTaskGuard.ObserveAsync(() => throw new OperationCanceledException());

        reported.Should().BeNull("dismissing a dialog is a normal path, not something to report");
    }

    /// <summary>
    /// The guard runs at a dispatcher boundary, so it must survive a reporter that itself throws
    /// rather than turning one failure into an unhandled second one.
    /// </summary>
    [Fact]
    public async Task AThrowingFallbackDoesNotEscape()
    {
        AvaloniaUiTaskGuard.FallbackFailureReporter = _ => throw new InvalidOperationException("reporter");

        var act = async () => await AvaloniaUiTaskGuard.ObserveAsync(
            () => throw new InvalidOperationException("boom"));

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// No reporter installed -- headless construction, tests, early startup -- must stay harmless.
    /// </summary>
    [Fact]
    public async Task NoReporterInstalledIsStillSafe()
    {
        AvaloniaUiTaskGuard.FallbackFailureReporter = null;

        var act = async () => await AvaloniaUiTaskGuard.ObserveAsync(
            () => throw new InvalidOperationException("boom"));

        await act.Should().NotThrowAsync();
    }
}
