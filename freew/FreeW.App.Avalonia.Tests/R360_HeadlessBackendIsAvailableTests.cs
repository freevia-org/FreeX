using FluentAssertions;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r360: fails ONCE, loudly, if the headless backend probe did not come up -- because everything else
/// in this assembly reacts to that by passing.
///
/// <para><see cref="HeadlessUiThread"/> exists to stop a swallowed exception turning an assertion
/// failure into a silent pass, and it fixed that for the test BODIES. The probe it introduced kept
/// the shape one level up: it catches everything, so a real regression in
/// <c>DocumentView.LoadDocument</c> or <c>Measure</c> is indistinguishable from "this machine has no
/// drawing backend". Either way the answer is "unavailable", and every
/// <c>if (!ran) return;</c> in this assembly -- 1044 of them at the time of writing -- returns before
/// asserting anything. The suite reports a full green having executed none of its bodies.</para>
///
/// <para>The environment skip is still legitimate and is deliberately NOT removed; a machine with no
/// backend genuinely cannot run these. What was missing is that the skip was invisible. This test
/// makes the difference observable: one failure naming the real exception, instead of a thousand
/// passes that mean nothing. On a machine that truly has no backend this is the single expected
/// failure, and its message says so.</para>
/// </summary>
public sealed class R360_HeadlessBackendIsAvailableTests
{
    [Fact]
    public void TheHeadlessBackendProbeSucceeded()
    {
        var failure = HeadlessUiThread.BackendFailure;

        failure.Should().BeNull(
            "every OnUiThread-based test in this assembly SKIPS SILENTLY (if (!ran) return;) when this " +
            "probe fails, so a failure here means the rest of the suite's green is meaningless. If this " +
            "machine genuinely has no headless drawing backend, this is the one expected failure. " +
            "Otherwise the probe caught a real regression in DocumentView construction or measurement: " +
            "\n" + (failure?.ToString() ?? "<none>"));
    }
}
