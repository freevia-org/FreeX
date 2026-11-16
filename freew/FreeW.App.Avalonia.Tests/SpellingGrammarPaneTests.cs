using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Proofing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class SpellingGrammarPaneTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public Task F7_pane_presents_and_applies_a_spelling_suggestion()
        => Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph("teh"));
            var editor = new DocumentView(new CustomDictionaryStore(null));
            editor.LoadDocument(document);
            editor.MoveCaretToBlockForTest(0, 1);
            var completionMessages = new List<string>();
            var pane = new SpellingGrammarPane(editor, (message, _) => completionMessages.Add(message));
            var window = new Window { Content = new StackPanel { Children = { editor, pane } } };

            try
            {
                window.Show();
                window.Measure(new Size(800, 600));
                window.Arrange(new Rect(0, 0, 800, 600));

                pane.Start();

                pane.IsPaneVisible.Should().BeTrue();
                pane.IsReviewing.Should().BeTrue();
                pane.IssueText.Should().Be("teh");
                pane.Suggestions.Should().Contain("the");
                pane.ChangeEnabled.Should().BeTrue();

                pane.ChangeButtonForTests.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                ((Paragraph)document.Blocks[0]).PlainText.Should().Be("the");
                pane.IsPaneVisible.Should().BeFalse();
                pane.IsReviewing.Should().BeFalse();
                completionMessages.Should().ContainSingle();
                editor.CanUndo.Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

    [Fact]
    public Task Change_all_rolls_back_earlier_replacements_when_a_locked_paragraph_declines_the_edit()
        => Session.Dispatch(() =>
        {
            var locked = new Paragraph("teh")
            {
                BlockContentControl = new BlockContentControl(
                    BlockContentControlKind.RichText,
                    LockMode: ContentControlLockMode.ContentLocked),
            };
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(locked);
            document.Blocks.Add(new Paragraph("teh"));
            var editor = new DocumentView(new CustomDictionaryStore(null));
            editor.LoadDocument(document);
            editor.MoveCaretToBlockForTest(0, 1);
            var session = new ProofingReviewSession(editor.CreateProofingReviewHost());

            session.Start();
            session.Current.Should().NotBeNull();
            session.Current!.BlockIndex.Should().Be(0);

            session.ChangeAll(0).Should().BeFalse();

            editor.Document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
                .Should().Equal("teh", "teh");
            session.ChangeCount.Should().Be(0);
            editor.CanUndo.Should().BeFalse();
        }, CancellationToken.None);

    [Fact]
    public Task Ignore_once_for_spelling_skips_only_the_current_occurrence()
        => Session.Dispatch(() =>
        {
            var editor = new DocumentView(new CustomDictionaryStore(null));
            editor.LoadDocument(DocumentWithText("teh teh"));
            editor.MoveCaretToBlockForTest(0, 1);
            var session = new ProofingReviewSession(editor.CreateProofingReviewHost());

            session.Start();
            session.Current!.Offset.Should().Be(0);

            session.IgnoreOnce().Should().BeTrue();
            session.Current.Should().NotBeNull();
            session.Current!.Offset.Should().Be(4);

            editor.MoveCaretToBlockForTest(0, 1);
            session.Start();
            session.Current.Should().NotBeNull("a new walk should not inherit an editor-wide Ignore All");
            session.Current!.Offset.Should().Be(0);
        }, CancellationToken.None);

    [Fact]
    public Task Loading_a_document_recreates_the_pane_session_and_clears_its_ignored_words()
        => Session.Dispatch(() =>
        {
            var editor = new DocumentView(new CustomDictionaryStore(null));
            editor.LoadDocument(DocumentWithText("teh"));
            editor.MoveCaretToBlockForTest(0, 1);
            var pane = new SpellingGrammarPane(editor, (_, _) => { });

            pane.Start();
            pane.IsReviewing.Should().BeTrue();
            pane.IgnoreAllButtonForTests.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            pane.IsReviewing.Should().BeFalse();

            editor.LoadDocument(DocumentWithText("teh"));
            editor.MoveCaretToBlockForTest(0, 1);
            pane.Start();

            pane.IsPaneVisible.Should().BeTrue();
            pane.IsReviewing.Should().BeTrue();
            pane.IssueText.Should().Be("teh");
        }, CancellationToken.None);

    private static TextDocument DocumentWithText(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
    }
}
