using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using ModelContentControl = FreeW.Core.Model.ContentControl;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-CCEDIT: a <c>w:sdt</c> can wrap a run inside a text box exactly as it can a body run, and its lock
/// was already honoured for typing there — but no click gesture reached it, because every gesture
/// resolved its target through the body/table-cell hit test. So a check box in a text box would not
/// toggle, a drop-down offered no choices and a date field no calendar.
/// </summary>
public sealed class DocumentViewShapeTextContentControlTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public Task A_check_box_in_a_text_box_toggles_when_it_is_activated() =>
        OnShapeTextField(
            Run.CheckBoxControl(@checked: false, tag: "Approved"),
            (view, field, body) =>
            {
                view.ActivateShapeTextContentControlForTest().Should().BeTrue();

                field.Control!.Checked.Should().BeTrue();
                field.Text.Should().Be(ModelContentControl.CheckedGlyph);
                field.Control.Tag.Should().Be("Approved");
                body.Runs[1].Text.Should().Be(
                    field.Text,
                    "the owning drawing run mirrors the shape's plain text");

                // Undo must put BOTH the glyph and the control's own state back, or the field and its
                // rendering disagree -- which is why this needs its own command rather than a text-only one.
                view.Undo();
                field.Text.Should().Be(ModelContentControl.UncheckedGlyph);
                field.Control!.Checked.Should().BeFalse();
            });

    [Fact]
    public Task A_locked_check_box_in_a_text_box_refuses_to_toggle() =>
        OnShapeTextField(
            LockedCheckBox(),
            (view, field, _) =>
            {
                view.ActivateShapeTextContentControlForTest().Should().BeFalse();
                field.Control!.Checked.Should().BeFalse();
            });

    [Fact]
    public Task A_date_field_in_a_text_box_opens_a_calendar_and_commits_the_picked_date() =>
        OnShapeTextField(
            Run.DatePickerControl("2026-07-04", tag: "Signed", dateFormat: "yyyy-MM-dd"),
            (view, field, _) =>
            {
                view.ActivateShapeTextContentControlForTest().Should().BeTrue();

                var calendar = view.ActiveContentControlCalendarForTest
                    .Should().NotBeNull().And.Subject.As<Flyout>()
                    .Content.Should().BeOfType<StackPanel>().Subject
                    .Children.OfType<global::Avalonia.Controls.Calendar>().Single();
                calendar.SelectedDate.Should().Be(new DateTime(2026, 7, 4));

                calendar.SelectedDate = new DateTime(1999, 12, 31);
                field.Text.Should().Be("1999-12-31");
            });

    private static Run LockedCheckBox()
    {
        var run = Run.CheckBoxControl(@checked: false, tag: "Approved");
        run.Control = run.Control! with { LockMode = ContentControlLockMode.ContentLocked };
        return run;
    }

    /// <summary>
    /// Builds a document whose floating text box holds <paramref name="field"/>, enters text editing and
    /// parks the caret ON the field, then runs <paramref name="body"/>. Dispatches WITHOUT swallowing
    /// exceptions, so a failed assertion inside fails the test rather than silently passing.
    /// </summary>
    private static Task OnShapeTextField(Run field, Action<DocumentView, Run, Paragraph> body) =>
        Session.Dispatch(
            () =>
            {
                var document = TextDocument.CreateEmpty();
                document.Blocks.Clear();
                var bodyParagraph = new Paragraph();
                bodyParagraph.Runs.Add(new Run("Body text with a floating shape anchored here."));

                var shape = new Shape(ShapeKind.TextBox, 144, 108, "#FFFFFF")
                {
                    Placement = new FloatingPlacement
                    {
                        Wrapping = ImageWrapping.InFront,
                        HorizontalAnchor = HorizontalAnchor.Column,
                        VerticalAnchor = VerticalAnchor.Paragraph,
                    },
                };
                var textParagraph = new Paragraph();
                textParagraph.Runs.Add(field);
                shape.TextParagraphs.Add(textParagraph);
                bodyParagraph.Runs.Add(new Run(shape.PlainText) { Shape = shape });
                document.Blocks.Add(bodyParagraph);

                var view = new DocumentView();
                view.LoadDocument(document);
                view.Measure(new Size(816, 2000));
                view.SelectFloating(0, 1);
                view.EnterSelectedShapeTextEditing().Should().BeTrue();
                view.SelectShapeTextRangeForTest(0, 0, 0);

                body(view, field, bodyParagraph);
            },
            CancellationToken.None);
}
