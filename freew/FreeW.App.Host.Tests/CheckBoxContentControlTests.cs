using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using Xunit;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Rendering-fidelity coverage for checkbox content controls in <see cref="DocumentView"/>: a checked
/// control must draw the ☒ glyph and an unchecked one the ☐ glyph in the FlowDocument, and both controls
/// (with their checked state) must survive a <see cref="DocumentView.CommitToModel"/> round-trip. Runs on
/// an STA thread (<c>[StaFact]</c>, via Xunit.StaFact) because the RichTextBox/FlowDocument need STA.
/// </summary>
public sealed class CheckBoxContentControlTests
{
    // A two-paragraph document: a checked checkbox control, then an unchecked one.
    private static TextDocument DocWithCheckboxes()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var checkedPara = new Paragraph();
        checkedPara.Runs.Add(Run.CheckBoxControl(@checked: true, tag: "agree", alias: "Agree"));
        doc.Blocks.Add(checkedPara);

        var uncheckedPara = new Paragraph();
        uncheckedPara.Runs.Add(Run.CheckBoxControl(@checked: false, tag: "subscribe", alias: "Subscribe"));
        doc.Blocks.Add(uncheckedPara);

        return doc;
    }

    // The concatenated text of every WPF run in the view's live FlowDocument.
    private static string FlowDocumentText(DocumentView view) =>
        string.Concat(
            view.Document.Blocks
                .OfType<System.Windows.Documents.Paragraph>()
                .SelectMany(p => p.Inlines)
                .OfType<WpfRun>()
                .Select(r => r.Text));

    [StaFact]
    public void CheckboxGlyphs_RenderAndRoundTrip()
    {
        var view = new DocumentView();
        view.LoadModel(DocWithCheckboxes());

        // The rendered FlowDocument must show the checked (☒) and unchecked (☐) glyphs.
        var rendered = FlowDocumentText(view);
        rendered.Should().Contain(ContentControl.CheckedGlyph,
            "a checked checkbox content control must draw the ☒ glyph");
        rendered.Should().Contain(ContentControl.UncheckedGlyph,
            "an unchecked checkbox content control must draw the ☐ glyph");

        // Commit back and assert both content controls (and their checked state) survive.
        view.CommitToModel();

        var paragraphs = view.Model.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().HaveCount(2);

        var checkedRun = paragraphs[0].Runs[0];
        checkedRun.Control.Should().NotBeNull("the checked control must survive commit");
        checkedRun.Control!.Kind.Should().Be(ContentControlKind.CheckBox);
        checkedRun.Control!.Checked.Should().BeTrue("the checked state must be preserved");
        checkedRun.Control!.Tag.Should().Be("agree");
        checkedRun.Text.Should().Be(ContentControl.CheckedGlyph);

        var uncheckedRun = paragraphs[1].Runs[0];
        uncheckedRun.Control.Should().NotBeNull("the unchecked control must survive commit");
        uncheckedRun.Control!.Kind.Should().Be(ContentControlKind.CheckBox);
        uncheckedRun.Control!.Checked.Should().BeFalse("the unchecked state must be preserved");
        uncheckedRun.Control!.Tag.Should().Be("subscribe");
        uncheckedRun.Text.Should().Be(ContentControl.UncheckedGlyph);
    }
}
