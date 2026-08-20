using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// A date-picker content control used to offer only today, yesterday and tomorrow, so every other date
/// had to be typed into the field by hand. Clicking one now opens a real calendar, the way Word's own
/// date field behaves. These tests drive the flyout the click gesture builds — a headless run cannot
/// click inside a popup, so they inspect its content and raise the events its handlers listen for.
/// </summary>
public sealed class DocumentViewContentControlCalendarTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public Task The_calendar_opens_on_the_date_the_field_already_shows() =>
        OnHostedView((view, paragraph) =>
        {
            var calendar = CalendarOf(view.OpenContentControlCalendarForTest(0, 0));

            calendar.SelectedDate.Should().Be(
                new DateTime(2026, 7, 4),
                "opening on today would hide which date the field holds");
            calendar.IsEnabled.Should().BeTrue();
        });

    [Fact]
    public Task Picking_a_date_commits_it_to_the_field_as_one_undoable_edit() =>
        OnHostedView((view, paragraph) =>
        {
            var calendar = CalendarOf(view.OpenContentControlCalendarForTest(0, 0));

            calendar.SelectedDate = new DateTime(1999, 12, 31);

            paragraph.Runs[0].Text.Should().Be("1999-12-31", "a calendar reaches dates no relative choice does");
            paragraph.Runs[0].Control!.Kind.Should().Be(ContentControlKind.DatePicker);

            // The guard against committing the same click twice (the selection change AND the
            // pointer-released fallback both fire on a day click) must not swallow the undo.
            view.Undo();
            view.Document.Paragraphs.Single().Runs[0].Text.Should().Be("2026-07-04");
        });

    [Fact]
    public Task The_today_button_commits_todays_date() =>
        OnHostedView((view, paragraph) =>
        {
            var flyout = view.OpenContentControlCalendarForTest(0, 0);
            var today = ContentOf(flyout).Children.OfType<Button>().Single();

            today.Content.Should().Be(ContentControlInteractionPlanner.TodayLabel);
            today.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            paragraph.Runs[0].Text.Should().Be(DateTime.Today.ToString("yyyy-MM-dd"));
        });

    /// <summary>
    /// A locked field still shows its calendar, disabled — which is the affordance the relative-date menu
    /// this replaced already gave. A click that put nothing on screen would read as broken.
    /// </summary>
    [Fact]
    public Task A_content_locked_field_shows_a_disabled_calendar_rather_than_nothing() =>
        OnHostedView((view, paragraph) =>
        {
            var flyout = view.OpenContentControlCalendarForTest(0, 1);
            var calendar = CalendarOf(flyout);

            calendar.IsEnabled.Should().BeFalse();
            ContentOf(flyout).Children.OfType<Button>().Single().IsEnabled.Should().BeFalse();

            // And the commit path refuses it even if the disabled control were somehow driven.
            calendar.SelectedDate = new DateTime(1999, 12, 31);
            paragraph.Runs[1].Text.Should().Be("2026-07-04");
        });

    private static global::Avalonia.Controls.Calendar CalendarOf(Flyout? flyout) =>
        ContentOf(flyout).Children.OfType<global::Avalonia.Controls.Calendar>().Single();

    private static StackPanel ContentOf(Flyout? flyout) =>
        flyout.Should().NotBeNull().And.Subject.As<Flyout>().Content.Should().BeOfType<StackPanel>().Subject;

    private static Task OnHostedView(Action<DocumentView, Paragraph> body) =>
        Session.Dispatch(() =>
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.DatePickerControl("2026-07-04", dateFormat: "yyyy-MM-dd"));
            paragraph.Runs.Add(Run.DatePickerControl("2026-07-04", dateFormat: "yyyy-MM-dd"));
            paragraph.Runs[1].Control = paragraph.Runs[1].Control! with
            {
                LockMode = ContentControlLockMode.ContentLocked,
            };

            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(paragraph);
            var view = new DocumentView();
            view.LoadDocument(document);
            var window = new Window { Width = 900, Height = 700, Content = view };
            window.Show();
            try
            {
                body(view, paragraph);
            }
            finally
            {
                window.Close();
            }
        }, System.Threading.CancellationToken.None);
}
