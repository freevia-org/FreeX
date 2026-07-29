using System.Globalization;
using FluentAssertions;
using FreeX.App.UI;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R91-render-comment-ui-5-2: the threaded-comment/note author heading (reply pane, Notes list,
/// printed "at end" appendix) always showed a bare absolute UTC stamp
/// ("yyyy-MM-dd HH:mm 'UTC'") -- never converted to the viewer's local time zone and never given any
/// relative phrasing -- confusing a viewer in any non-UTC zone. <see cref="GridView.FormatMessageHeading"/>
/// (with the explicit-<c>now</c> overload used here so the test doesn't race the real clock) is the
/// exact formatter the reply pane/list/appendix all funnel through.
/// </summary>
public sealed class R91_CommentTimestampFormattingTests
{
    // "2pm local today" -- safely away from local midnight so subtracting a couple of hours or a day
    // can never accidentally cross a calendar-day boundary regardless of which time zone the test
    // itself happens to run in.
    private static DateTimeOffset SafeLocalNow()
    {
        var localToday = DateTime.Today.AddHours(14);
        return new DateTimeOffset(localToday, TimeZoneInfo.Local.GetUtcOffset(localToday));
    }

    [Fact]
    public void FormatMessageHeading_JustNow_ShowsRelativeLabel_NotAbsoluteUtcStamp()
    {
        var now = SafeLocalNow();

        GridView.FormatMessageHeading("Alex", now, now)
            .Should().Be("Alex - Just now");
    }

    [Fact]
    public void FormatMessageHeading_FiveMinutesAgo_ShowsMinuteCount()
    {
        var now = SafeLocalNow();

        GridView.FormatMessageHeading("Alex", now.AddMinutes(-5), now)
            .Should().Be("Alex - 5m");
    }

    [Fact]
    public void FormatMessageHeading_EarlierTodayLocal_ShowsTodayAndLocalTimeOfDay()
    {
        var now = SafeLocalNow();
        var createdAtUtc = now.AddHours(-2);
        var expectedTimeOfDay = createdAtUtc.ToLocalTime().ToString("h:mm tt", CultureInfo.InvariantCulture);

        GridView.FormatMessageHeading("Alex", createdAtUtc, now)
            .Should().Be($"Alex - Today, {expectedTimeOfDay}",
                "Excel shows same-local-day comment timestamps as 'Today, <local time>', not a bare UTC stamp");
    }

    [Fact]
    public void FormatMessageHeading_YesterdayLocal_ShowsYesterdayAndLocalTimeOfDay()
    {
        var now = SafeLocalNow();
        var createdAtUtc = now.AddHours(-26);
        var expectedTimeOfDay = createdAtUtc.ToLocalTime().ToString("h:mm tt", CultureInfo.InvariantCulture);

        GridView.FormatMessageHeading("Alex", createdAtUtc, now)
            .Should().Be($"Alex - Yesterday, {expectedTimeOfDay}");
    }

    [Fact]
    public void FormatMessageHeading_SeveralDaysAgo_ShowsLocalAbsoluteDateTime_NoRegression()
    {
        var now = SafeLocalNow();
        var createdAtUtc = now.AddDays(-5);
        var expectedLocal = createdAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        // Older activity still gets an absolute stamp (matching the pre-fix behavior's intent) --
        // but now in LOCAL time, never a bare UTC stamp.
        GridView.FormatMessageHeading("Alex", createdAtUtc, now)
            .Should().Be($"Alex - {expectedLocal}");
    }

    [Fact]
    public void FormatMessageHeading_NullTimestamp_ReturnsAuthorOnly_NoRegression()
    {
        var now = SafeLocalNow();

        GridView.FormatMessageHeading("Alex", null, now)
            .Should().Be("Alex");
    }

    [Fact]
    public void FormatMessageHeading_TwoArgOverload_DefaultsNowToCurrentClock_NoRegression()
    {
        // The pre-existing 2-arg call sites (reply pane heading, FormatReplyChoice, etc.) must keep
        // working unchanged -- they now implicitly thread DateTimeOffset.Now through.
        var justNow = DateTimeOffset.UtcNow;

        GridView.FormatMessageHeading("Alex", justNow)
            .Should().Be("Alex - Just now");
    }
}
