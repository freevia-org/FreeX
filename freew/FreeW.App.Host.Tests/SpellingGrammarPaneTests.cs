using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// The docked Spelling &amp; Grammar pane is a thin view: these tests drive its real buttons and check
/// that it only projects the shared session/planner and forwards clicks to the editor.
/// </summary>
public sealed class SpellingGrammarPaneTests
{
    [StaFact]
    public void Start_shows_the_first_issue_and_buttons_walk_to_words_completion_message()
    {
        var view = LoadedView("I saw teh cat", "It was the the dog");
        var messages = new List<(string Message, string Title)>();
        var pane = new SpellingGrammarPane(view, (message, title) => messages.Add((message, title)));
        var root = pane.Build();
        pane.IsVisible.Should().BeFalse();

        pane.Start();

        pane.IsVisible.Should().BeTrue();
        view.Selection.Text.Should().Be("teh");
        Texts(root).Should().Contain(["Spelling & Grammar", "Not in Dictionary", "teh"]);
        Suggestions(root).Items.Cast<string>().Should().Equal("the");
        VisibleButtons(root).Should().Equal(
            "Change", "Change All", "Ignore Once", "Ignore All", "Add to Dictionary", "Close");

        Click(root, "Change");

        ParagraphTexts(view).Should().Equal("I saw the cat", "It was the the dog");
        Texts(root).Should().Contain("Repeated Word");
        Suggestions(root).Items.Cast<string>().Should().Equal("Delete Repeated Word");
        VisibleButtons(root).Should().Equal("Change", "Ignore Once", "Don't Check for This Issue", "Close");
        messages.Should().BeEmpty();

        Click(root, "Change");

        ParagraphTexts(view).Should().Equal("I saw the cat", "It was the dog");
        pane.IsVisible.Should().BeFalse("the pane is put away when the check completes");
        messages.Should().Equal(("Spelling and grammar check is complete.", "Spelling & Grammar"));
    }

    [StaFact]
    public void Start_on_a_clean_document_reports_completion_without_opening_the_pane()
    {
        var view = LoadedView("Nothing to fix here");
        var messages = new List<string>();
        var pane = new SpellingGrammarPane(view, (message, _) => messages.Add(message));
        pane.Build();

        pane.Start();

        pane.IsVisible.Should().BeFalse();
        messages.Should().Equal("Spelling and grammar check is complete.");
    }

    [StaFact]
    public void Close_hides_the_pane_and_leaves_the_document_alone()
    {
        var view = LoadedView("I saw teh cat");
        var pane = new SpellingGrammarPane(view, (_, _) => { });
        var root = pane.Build();
        pane.Start();

        Click(root, "Close");

        pane.IsVisible.Should().BeFalse();
        ParagraphTexts(view).Should().Equal("I saw teh cat");
        view.Commands.CanUndo.Should().BeFalse();
    }

    private static void Click(UIElement root, string label) =>
        Descendants(root).OfType<Button>()
            .Single(button => button.Visibility == Visibility.Visible && Equals(button.Content, label))
            .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static string[] VisibleButtons(UIElement root) =>
        Descendants(root).OfType<Button>()
            .Where(button => button.Visibility == Visibility.Visible)
            .Select(button => (string)button.Content)
            .ToArray();

    private static string[] Texts(UIElement root) =>
        Descendants(root).OfType<TextBlock>()
            .Where(text => text.Visibility == Visibility.Visible)
            .Select(text => text.Text)
            .ToArray();

    private static ListBox Suggestions(UIElement root) => Descendants(root).OfType<ListBox>().Single();

    private static IEnumerable<object> Descendants(object root)
    {
        yield return root;
        if (root is not DependencyObject dependencyObject)
            yield break;

        foreach (var child in LogicalTreeHelper.GetChildren(dependencyObject))
        {
            // A ListBox's string items are data, not controls.
            if (child is not DependencyObject)
                continue;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static DocumentView LoadedView(params string[] paragraphs)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var paragraph in paragraphs)
            doc.Blocks.Add(new Paragraph(paragraph));
        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static string[] ParagraphTexts(DocumentView view)
    {
        view.CommitToModel();
        return view.Model.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText).ToArray();
    }
}
