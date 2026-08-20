using System.Windows.Documents;
using FreeW.App.Host.Editing;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// K3: a placeholder-showing content control (w:showingPlcHdr) must stop reporting itself as showing
/// placeholder text once the user types real content into it, even if the resulting text is empty --
/// matching Word, which drops the flag the instant a placeholder field is edited. WPF's native
/// RichTextBox editing mutates the WpfRun's text directly with nothing else observing the edit, so
/// <see cref="DocumentView.CommitToModel"/>'s read-back is the only place that can tell an edited field
/// from an untouched one: it compares the committed text against the text captured when the field was
/// rendered (the run's original text, threaded through <c>ApplyContentControlMarker</c>'s
/// <c>originalText</c> parameter onto <c>ContentControlMarker.OriginalText</c>). Runs on an STA thread
/// (<c>[StaFact]</c>, via Xunit.StaFact) because the RichTextBox/FlowDocument need STA. Mirrors
/// <see cref="CheckBoxContentControlTests"/> and the Avalonia-side placeholder coverage in
/// DocumentViewContentControlKeyboardTests.
/// </summary>
public sealed class ContentControlPlaceholderReadbackTests
{
    private const string PlaceholderText = "Click to enter text";

    private static (DocumentView View, WpfRun FieldRun) LoadPlaceholderField()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(PlaceholderText)
        {
            Control = new ContentControl(
                ContentControlKind.PlainText,
                WordMetadata: new ContentControlWordMetadata(ShowingPlaceholder: true))
        });
        doc.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(doc);

        var fieldRun = view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(p => p.Inlines)
            .OfType<WpfRun>()
            .Single();
        return (view, fieldRun);
    }

    [StaFact]
    public void Typing_into_a_placeholder_showing_field_clears_the_placeholder_flag_on_commit()
    {
        var (view, fieldRun) = LoadPlaceholderField();

        // Simulate the user having typed over the placeholder the way native RichTextBox editing does:
        // it mutates the run's text directly and nothing else observes the edit.
        fieldRun.Text = "Bobby";
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>().Single().Runs.Single(r => r.Control is not null);
        run.Text.Should().Be("Bobby");
        run.Control!.WordMetadata!.ShowingPlaceholder.Should().BeFalse(
            "typing real text into a placeholder-showing field must clear w:showingPlcHdr on commit");
    }

    /// <summary>
    /// Sibling no-regression coverage: committing an UNTOUCHED placeholder-showing field -- e.g. loading a
    /// document and saving it right back without editing it -- must not silently clear the flag just from
    /// passing through CommitToModel's read-back.
    /// </summary>
    [StaFact]
    public void Committing_an_untouched_placeholder_showing_field_leaves_the_flag_alone()
    {
        var (view, _) = LoadPlaceholderField();

        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>().Single().Runs.Single(r => r.Control is not null);
        run.Text.Should().Be(PlaceholderText);
        run.Control!.WordMetadata!.ShowingPlaceholder.Should().BeTrue(
            "committing an untouched field must not clear its placeholder flag");
    }
}
