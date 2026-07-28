using System.Threading;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class AutoCorrectAsYouTypeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task AutoCorrect_replaces_typo_through_live_text_input()
    {
        string? text = null;
        await Session.Dispatch(() =>
        {
            var view = NewEditor();
            view.SimulateTextInputForTest("I teh ");
            text = view.Document.PlainText;
        }, CancellationToken.None);

        text.Should().Be("I the ");
    }

    [Fact]
    public async Task AutoFormat_converts_ordinal_suffix_to_superscript()
    {
        Run? superscript = null;
        await Session.Dispatch(() =>
        {
            var view = NewEditor();
            view.AutoCorrectOptions = AutoCorrectOptions.AllOff;
            view.SimulateTextInputForTest("1st ");
            superscript = view.Document.Blocks
                .OfType<Paragraph>()
                .SelectMany(paragraph => paragraph.Runs)
                .FirstOrDefault(run => run.Formatting.VerticalAlign == VerticalAlign.Superscript);
        }, CancellationToken.None);

        superscript.Should().NotBeNull();
        superscript!.Text.Should().Be("st");
    }

    [Fact]
    public async Task AutoFormat_text_outcomes_match_the_shared_Wpf_contract()
    {
        string? text = null;
        (string? Url, string? LinkedText)? link = null;
        await Session.Dispatch(() =>
        {
            var view = NewEditor();
            view.AutoCorrectOptions = AutoCorrectOptions.AllOff;
            view.SimulateTextInputForTest("\"hi\" 1/2 ");
            text = view.Document.PlainText;

            view = NewEditor();
            view.AutoCorrectOptions = AutoCorrectOptions.AllOff;
            view.SimulateTextInputForTest("see http://example.com ");
            var linkedRun = view.Document.Blocks
                .OfType<Paragraph>()
                .SelectMany(paragraph => paragraph.Runs)
                .FirstOrDefault(run => run.HyperlinkUrl is not null);
            if (linkedRun is not null)
                link = (linkedRun.HyperlinkUrl, linkedRun.Text);
        }, CancellationToken.None);

        text.Should().Be("“hi” ½ ");
        link.Should().NotBeNull();
        link!.Value.Url.Should().StartWith("http://example.com");
        link.Value.LinkedText.Should().Be("http://example.com");
    }

    [Fact]
    public async Task AutoFormat_marker_is_one_undo_unit_and_following_typing_is_separate()
    {
        (ListKind Kind, string Text)? result = null;
        await Session.Dispatch(() =>
        {
            var view = NewEditor();
            view.AutoCorrectOptions = AutoCorrectOptions.AllOff;
            view.SimulateTextInputForTest("* ");
            var paragraph = view.Document.Blocks.OfType<Paragraph>().Single();
            result = (paragraph.Formatting.ListKind, paragraph.PlainText);

            view.Undo();
            paragraph.Formatting.ListKind.Should().Be(ListKind.None);
            paragraph.PlainText.Should().Be("*");
            view.Redo();
            paragraph.Formatting.ListKind.Should().Be(ListKind.Bullet);
            paragraph.PlainText.Should().BeEmpty();

            view.SimulateTextInputForTest("item");
            view.Undo();
            paragraph.Formatting.ListKind.Should().Be(ListKind.Bullet);
            paragraph.PlainText.Should().Be("ite");
        }, CancellationToken.None);

        result.Should().Be((ListKind.Bullet, string.Empty));
    }

    private static DocumentView NewEditor()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());
        var view = new DocumentView();
        view.LoadDocument(document);
        return view;
    }
}
