using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Free.Shared.AppServices;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// The Avalonia Find &amp; Replace dialog's Replace All is story-wide like the WPF dialog's: it replaces in
/// the body AND in the headers/footers, notes, comments and text boxes through the shared planners
/// (<see cref="FindReplaceBodyPlanner"/> / <see cref="FindReplaceStoryPlanner"/>), as one undo step.
/// </summary>
public sealed class FindReplaceStoryReplaceAllTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static DocumentView ViewWithStories()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Draft body"));
        document.Header = new HeaderFooter("Draft header");
        document.Footer = new HeaderFooter("Draft footer");
        document.FinalSectionHeadersFooters.FirstHeader = new HeaderFooter("Draft first");
        document.Footnotes[1] = new Footnote(1, "Draft note");
        document.Endnotes[1] = new Endnote(1, "Draft endnote");
        document.Comments[3] = new Comment(3, "Draft comment", "Ann");
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    [Fact]
    public async Task Dialog_ReplaceAll_replaces_in_the_body_and_every_other_story_as_one_undo_step()
    {
        var status = string.Empty;
        string? body = null, header = null, footer = null, first = null, note = null, endnote = null, comment = null;
        string? undone = null, undoneNote = null;
        var ran = await OnUiThread(() =>
        {
            var view = ViewWithStories();
            var dialog = new FindReplaceDialog(view, FindReplaceOpenMode.Replace);
            try
            {
                dialog.SetFindTextForTest("Draft");
                dialog.SetReplaceTextForTest("Final");
                dialog.ReplaceAllForTest();
                status = dialog.StatusForTest;

                var document = view.Document;
                body = ((Paragraph)document.Blocks[0]).PlainText;
                header = document.Header!.Paragraphs[0].PlainText;
                footer = document.Footer!.Paragraphs[0].PlainText;
                first = document.FinalSectionHeadersFooters.FirstHeader!.Paragraphs[0].PlainText;
                note = document.Footnotes[1].PlainText;
                endnote = document.Endnotes[1].PlainText;
                comment = document.Comments[3].PlainText;

                view.Undo();
                undone = ((Paragraph)view.Document.Blocks[0]).PlainText;
                undoneNote = view.Document.Footnotes[1].PlainText;
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        body.Should().Be("Final body");
        header.Should().Be("Final header");
        footer.Should().Be("Final footer");
        first.Should().Be("Final first");
        note.Should().Be("Final note");
        endnote.Should().Be("Final endnote");
        comment.Should().Be("Final comment");
        status.Should().Contain("7");
        undone.Should().Be("Draft body", "one Ctrl+Z reverts the body edit");
        undoneNote.Should().Be("Draft note", "and the story edits in the same undo step");
    }

    [Fact]
    public async Task ReplaceAll_inside_a_body_selection_leaves_the_other_stories_alone()
    {
        int count = -1;
        string? second = null, header = null, note = null;
        var ran = await OnUiThread(() =>
        {
            var view = ViewWithStories();
            view.Document.Blocks.Add(new Paragraph("Draft second"));
            view.LoadDocument(view.Document);
            view.Measure(new Size(800, 2000));
            view.SetSelectionRangePublic(0, 0, 0, "Draft body".Length);

            count = view.ReplaceAll("Draft", "Final");
            second = ((Paragraph)view.Document.Blocks[1]).PlainText;
            header = view.Document.Header!.Paragraphs[0].PlainText;
            note = view.Document.Footnotes[1].PlainText;
        });
        ran.Should().BeTrue();

        count.Should().Be(1);
        second.Should().Be("Draft second");
        header.Should().Be("Draft header");
        note.Should().Be("Draft note");
    }

    [Fact]
    public async Task ReplaceAll_expands_special_codes_in_the_replacement_like_the_wpf_dialog()
    {
        string? text = null;
        var ran = await OnUiThread(() =>
        {
            var view = ViewWithStories();
            view.ReplaceAll("Draft body", "Found ^& there", new FindReplaceSearchOptions(false, false, false));
            text = ((Paragraph)view.Document.Blocks[0]).PlainText;
        });
        ran.Should().BeTrue();

        text.Should().Be("Found Draft body there");
    }
}
