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
}
