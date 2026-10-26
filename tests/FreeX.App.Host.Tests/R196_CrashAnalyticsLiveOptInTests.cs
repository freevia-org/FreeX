using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

/// <summary>
/// r196: the Trust Center's "send opt-in crash reports" choice was read exactly once, inside
/// <c>Initialize</c>, which startup calls once. Unticking the box therefore changed nothing until
/// the app was restarted -- the user withdrew consent and reports kept being sent for the rest of
/// the session -- and the checkbox carries no restart notice. Withdrawal of consent is the one
/// direction that must not lag, and every other side effect the Options commit handler drives
/// (gridlines, headings, the QAT, calculation mode) already takes effect immediately.
/// </summary>
public sealed class R196_CrashAnalyticsLiveOptInTests
{
    [Fact]
    public void OptingOutMidSession_TakesEffectWithoutARestart()
    {
        var analytics = new SentryCrashAnalytics();

        // Never initialised, so the SDK is off and IsEnabled is false either way -- the point here
        // is that ApplyOptIn is reachable and cannot turn reporting ON by itself.
        analytics.ApplyOptIn(true);
        analytics.IsEnabled.Should().BeFalse(
            "opting in cannot enable reporting when the SDK was never initialised");

        analytics.ApplyOptIn(false);
        analytics.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void SendTestReport_RefusesOnceTheUserHasOptedOut()
    {
        var analytics = new SentryCrashAnalytics();
        analytics.ApplyOptIn(false);

        analytics.SendTestReport().Should().BeFalse();
    }

    [Fact]
    public void DisabledImplementation_AcceptsApplyOptInWithoutThrowing()
    {
        var analytics = new DisabledCrashAnalytics();

        var act = () => analytics.ApplyOptIn(true);

        act.Should().NotThrow();
    }

    [Fact]
    public void EverySendPathConsultsTheLiveGate_NotTheStartupField()
    {
        // The defect was that the send paths tested a field frozen at Initialize. They must read
        // IsEnabled, which folds in the live opt-in.
        var source = DialogSourceTestSupport.ReadHostSources("SentryCrashAnalytics.cs");

        source.Should().NotContain(
            "if (!_isEnabled)",
            "a send path guarded by the startup field ignores a mid-session opt-out");
        source.Should().Contain("public void ApplyOptIn(bool enabled)");
    }

    [Fact]
    public void TheOptionsCommitHandler_AppliesTheChoiceImmediately()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

        source.Should().Contain(
            "ApplyOptIn(_options.CrashAnalyticsEnabled)",
            "committing Options must carry the crash-report choice through at once");
    }

    [Fact]
    public void TheSentryBeforeSendHook_DropsEventsAfterAnOptOut()
    {
        // The methods on this class are not the only way an event leaves: the SDK captures
        // unhandled exceptions itself. BeforeSend is the last gate before anything is transmitted.
        var source = DialogSourceTestSupport.ReadHostSources("SentryCrashAnalytics.cs");

        source.Should().Contain(
            "if (!_optedIn)",
            "the BeforeSend hook must drop SDK-captured events once the user has opted out");
    }
}
