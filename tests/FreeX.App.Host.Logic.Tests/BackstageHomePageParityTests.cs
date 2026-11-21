using System.Globalization;
using System.IO;
using FluentAssertions;
using Free.Shared.Shell;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

/// <summary>
/// File &gt; Home parity: relative "date modified" text from the shared formatter, and source pins that keep
/// the WPF and Avalonia Home pages on the shared planners (empty states, Feedback rail entry).
/// </summary>
public sealed class BackstageHomePageParityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 15, 30, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 21)));

    [Fact]
    public void Format_UnderAnHourIsJustNow()
    {
        BackstageRecentDateFormatter.Format(Now.AddMinutes(-20), Now)
            .Should().Be(UiText.Get("Backstage_Recent_LastOpenedJustNow"));
    }

    [Fact]
    public void Format_EarlierTodayIsTodayAtTime()
    {
        var timestamp = Now.AddHours(-3);
        var time = timestamp.ToLocalTime().ToString(UiText.Get("Backstage_Recent_LastOpenedTimeFormat"), CultureInfo.CurrentCulture);

        BackstageRecentDateFormatter.Format(timestamp, Now)
            .Should().Be(UiText.Format("Backstage_Recent_LastOpenedTodayAt", time));
    }

    [Fact]
    public void Format_PreviousDayIsYesterdayAtTime()
    {
        var timestamp = Now.AddHours(-30);
        var time = timestamp.ToLocalTime().ToString(UiText.Get("Backstage_Recent_LastOpenedTimeFormat"), CultureInfo.CurrentCulture);

        BackstageRecentDateFormatter.Format(timestamp, Now)
            .Should().Be(UiText.Format("Backstage_Recent_LastOpenedYesterdayAt", time));
    }

    [Fact]
    public void Format_WithinAWeekIsWeekdayAtTime()
    {
        var timestamp = Now.AddDays(-4);
        var local = timestamp.ToLocalTime();
        var time = local.ToString(UiText.Get("Backstage_Recent_LastOpenedTimeFormat"), CultureInfo.CurrentCulture);
        var day = local.ToString("dddd", CultureInfo.CurrentCulture);

        BackstageRecentDateFormatter.Format(timestamp, Now)
            .Should().Be(UiText.Format("Backstage_Recent_LastOpenedWeekdayAt", day, time));
    }

    [Fact]
    public void Format_OlderUsesDateWithYearOnlyOutsideTheCurrentYear()
    {
        var sameYear = Now.AddDays(-40);
        var otherYear = Now.AddYears(-1);

        BackstageRecentDateFormatter.Format(sameYear, Now)
            .Should().Be(sameYear.ToLocalTime().ToString(UiText.Get("Backstage_Recent_LastOpenedDateFormat"), CultureInfo.CurrentCulture));
        BackstageRecentDateFormatter.Format(otherYear, Now)
            .Should().Be(otherYear.ToLocalTime().ToString(UiText.Get("Backstage_Recent_LastOpenedDateWithYearFormat"), CultureInfo.CurrentCulture));
    }

    [Fact]
    public void WpfHomePane_UsesSharedEmptyStatePolicyAndFeedbackRailHandler()
    {
        var backstage = Read("src", "FreeX.App.Host", "MainWindow.Backstage.cs");
        backstage.Should().Contain("FreeXBackstageHomePanePlanner.ResolveEmptyStateKey(");
        backstage.Should().Contain("RefreshSsHomeEmptyState();");
        backstage.Should().Contain("BackstageGreetingFormatter.FormatGreeting(DateTime.Now)");

        Read("src", "FreeX.App.Host", "MainWindow.xaml").Should().Contain("x:Name=\"SsHomeEmptyText\"");

        var frame = Read("src", "FreeX.App.Host", "MainWindow.BackstageFrame.cs");
        frame.Should().Contain("FeedbackAsync = () => RunBackstageCommand(() => FeedbackBtn_Click(");
    }

    [Fact]
    public void AvaloniaHomePane_UsesSharedEmptyStatePolicyAndFeedbackRailHandler()
    {
        var live = Read("src", "FreeX.App.Avalonia", "MainWindow.LiveBackstage.cs");
        live.Should().Contain("FreeXBackstageHomePanePlanner.ResolveEmptyStateKey(");
        live.Should().NotContain("WPF leaves an empty Pinned list blank");

        var workflow = Read("src", "FreeX.App.Avalonia", "MainWindow.BackstageWorkflow.cs");
        workflow.Should().Contain("FeedbackAsync = () => OpenExternalHelpLinkAsync(");
        workflow.Should().Contain("AppIssueReporter.CreateIssueUrl(CreateIssueReportContext())");
    }

    [Fact]
    public void RecentFileViewModel_DelegatesDateTextToSharedFormatter()
    {
        var source = Read("shared", "Free.Shared.Shell", "BackstageRecentFileListPlanner.cs");
        source.Should().Contain("BackstageRecentDateFormatter.Format(entry.LastOpened, DateTimeOffset.Now)");
    }

    private static string Read(params string[] parts) => File.ReadAllText(WorkspaceFileLocator.Find(parts));
}
