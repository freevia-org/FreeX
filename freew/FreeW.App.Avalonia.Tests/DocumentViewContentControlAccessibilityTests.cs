using System.Threading;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-A11Y: a content control is a form field, and a screen reader has to be told so. Until now the
/// accessibility projection had no node for one, so a field reached assistive technology as ordinary
/// text: no name, no control type, no indication that the document forbids editing it. These walk the
/// real automation peer tree the way a screen reader does.
/// </summary>
public sealed class DocumentViewContentControlAccessibilityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task A_field_is_an_edit_element_named_by_its_title_and_valued_by_its_text() =>
        await Session.Dispatch(
            () =>
            {
                var view = LoadFieldDocument(Run.PlainTextControl("Bob", alias: "Applicant"));

                var field = FindContentControlPeer(view);

                field.Should().NotBeNull("a form field must appear in the accessibility tree");
                field!.GetAutomationControlType().Should().Be(AutomationControlType.Edit);
                field.GetName().Should().Be("Applicant, plain-text field");
                field.GetHelpText().Should().Be("Plain-text field");
                field.GetProvider<IValueProvider>()!.Value.Should().Be("Bob");
                field.GetProvider<IValueProvider>()!.IsReadOnly.Should().BeFalse();
            },
            CancellationToken.None);

    [Fact]
    public async Task A_check_box_field_reports_its_toggle_state() =>
        await Session.Dispatch(
            () =>
            {
                var view = LoadFieldDocument(Run.CheckBoxControl(@checked: true, alias: "Reviewed"));

                var field = FindContentControlPeer(view);

                field!.GetAutomationControlType().Should().Be(AutomationControlType.CheckBox);
                field.GetProvider<IToggleProvider>()!.ToggleState.Should().Be(ToggleState.On);
            },
            CancellationToken.None);

    [Fact]
    public async Task Toggling_a_check_box_field_through_automation_actually_ticks_it() =>
        await Session.Dispatch(
            () =>
            {
                var view = LoadFieldDocument(Run.CheckBoxControl(@checked: false, alias: "Reviewed"));

                FindContentControlPeer(view)!.GetProvider<IToggleProvider>()!.Toggle();

                // The editor replaces the run through its command bus, so read the live document.
                LiveField(view).Control!.Checked.Should().BeTrue(
                    "ticking the box is the whole interaction of a check-box field");
                view.CanUndo.Should().BeTrue("it goes through the editor, so it is undoable");
                FindContentControlPeer(view)!.GetProvider<IToggleProvider>()!.ToggleState
                    .Should().Be(ToggleState.On);
            },
            CancellationToken.None);

    [Fact]
    public async Task A_text_field_offers_no_toggle_pattern_and_a_locked_check_box_refuses() =>
        await Session.Dispatch(
            () =>
            {
                var text = LoadFieldDocument(Run.PlainTextControl("Bob", alias: "Applicant"));
                FindContentControlPeer(text)!.GetProvider<IToggleProvider>()
                    .Should().BeNull("a text field has no on/off state to advertise");

                var locked = Run.CheckBoxControl(@checked: false, alias: "Reviewed");
                locked.Control = locked.Control! with { LockMode = ContentControlLockMode.ContentLocked };
                var lockedView = LoadFieldDocument(locked);

                var toggle = () => FindContentControlPeer(lockedView)!.GetProvider<IToggleProvider>()!.Toggle();

                toggle.Should().Throw<NotSupportedException>(
                    "automation must not bypass the lock the editor enforces");
                LiveField(lockedView).Control!.Checked.Should().BeFalse();
            },
            CancellationToken.None);

    [Fact]
    public async Task A_drop_down_field_reports_as_a_combo_box() =>
        await Session.Dispatch(
            () =>
            {
                var view = LoadFieldDocument(Run.DropDownListControl(
                    [new ContentControlListItem("Red", "R")],
                    alias: "Colour"));

                FindContentControlPeer(view)!.GetAutomationControlType()
                    .Should().Be(AutomationControlType.ComboBox);
            },
            CancellationToken.None);

    [Fact]
    public async Task A_field_the_document_will_not_let_the_user_edit_reports_read_only() =>
        await Session.Dispatch(
            () =>
            {
                var locked = Run.PlainTextControl("Bob", alias: "Applicant");
                locked.Control = locked.Control! with { LockMode = ContentControlLockMode.ContentLocked };
                var view = LoadFieldDocument(locked);

                var field = FindContentControlPeer(view);

                field!.GetProvider<IValueProvider>()!.IsReadOnly.Should().BeTrue(
                    "announcing a locked field as editable invites the user to type what the document refuses");
                field.GetHelpText().Should().Be("Plain-text field, locked");
            },
            CancellationToken.None);

    [Fact]
    public async Task Moving_the_caret_into_a_field_announces_which_field_it_is() =>
        await Session.Dispatch(
            () =>
            {
                var locked = Run.PlainTextControl("Bob", alias: "Applicant");
                locked.Control = locked.Control! with { LockMode = ContentControlLockMode.ContentLocked };
                var view = LoadFieldDocument(locked);

                view.MoveCaretToBlockForTest(0, 0);
                var outsideStatus = view.AutomationSelectionStatus();
                view.MoveCaretToBlockForTest(0, 7);
                var insideStatus = view.AutomationSelectionStatus();

                outsideStatus.Should().StartWith("Caret ", "ordinary body text keeps the existing status");
                insideStatus.Should().StartWith(
                    "Applicant, plain-text field, Plain-text field, locked; ",
                    "being in a form field — and that it is locked — leads the announcement");
                insideStatus.Should().Contain("paragraph 1 of 1", "the positional status still follows");
            },
            CancellationToken.None);

    private static Run LiveField(DocumentView view) =>
        view.Document.Paragraphs.SelectMany(paragraph => paragraph.Runs).Single(run => run.Control is not null);

    private static AutomationPeer? FindContentControlPeer(DocumentView view)
    {
        var root = view.CreateAutomationPeerForTests();
        return Descendants(root).FirstOrDefault(peer =>
            peer.GetClassName() == "Document" + DocumentAccessibilityNodeKind.ContentControl);
    }

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static DocumentView LoadFieldDocument(Run control)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Name: "));
        paragraph.Runs.Add(control);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 1200));
        return view;
    }
}
