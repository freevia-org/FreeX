using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
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

    // A real left-button-up on the checkbox run, exactly the event OnCheckBoxControlClicked is wired to
    // via `wpf.MouseLeftButtonUp += OnCheckBoxControlClicked` -- NOT a direct call to the planner or to
    // ToggleContentControl, which every prior test used and which is why this gap was invisible.
    private static void ClickCheckBox(WpfRun wpf) =>
        wpf.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent
        });

    private static WpfRun SingleCheckboxRun(DocumentView view) =>
        view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(p => p.Inlines)
            .OfType<WpfRun>()
            .Single();

    /// <summary>
    /// K2 (round 153 remediation): an ordinary mouse click on an UNPROTECTED document's checkbox (no
    /// Restrict Editing / "Filling in Forms" involved) must honour the document's own authored glyphs
    /// (<see cref="ContentControlCheckBoxMetadata"/>), not the app's fixed
    /// <see cref="ContentControl.CheckedGlyph"/>/<see cref="ContentControl.UncheckedGlyph"/>. Before the
    /// fix, OnCheckBoxControlClicked only routed through the metadata-aware planner when
    /// RestrictEditingPolicy.IsFormFieldEditingOnly was true, so this -- the common gesture -- fell
    /// through to a hand-written toggle that overwrote the custom symbol.
    /// </summary>
    [StaFact]
    public void CheckboxClick_OnUnprotectedDocument_WithCustomGlyphMetadata_WritesTheDocumentsOwnGlyph()
    {
        var metadata = new ContentControlCheckBoxMetadata(
            CheckedState: new ContentControlCheckBoxStateMetadata("2714", "Segoe UI Symbol"),
            UncheckedState: new ContentControlCheckBoxStateMetadata("2716", "Segoe UI Symbol"));

        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Run.CheckBoxControl(@checked: false, tag: "agree", checkBoxMetadata: metadata));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);
        view.ProtectionMode.Should().Be(ProtectionMode.None, "the document must be unprotected for this to be the ordinary gesture");

        ClickCheckBox(SingleCheckboxRun(view));

        SingleCheckboxRun(view).Text.Should().Be("✔",
            "the click must honour the document's own CheckBoxMetadata glyph, not the app's fixed CheckedGlyph");

        view.CommitToModel();
        var committed = view.Model.Blocks.OfType<Paragraph>().Single().Runs[0];
        committed.Control!.Checked.Should().BeTrue("the click must flip the checked state");
        committed.Text.Should().Be("✔", "the committed run text must carry the custom glyph, not the app default");
    }

    /// <summary>
    /// Sibling/no-regression case: a checkbox with no custom w14 state metadata (the common case) must
    /// keep toggling to the app's default ☒/☐ glyphs through the same real-click path.
    /// </summary>
    [StaFact]
    public void CheckboxClick_OnUnprotectedDocument_WithoutCustomGlyphMetadata_StillUsesAppDefaultGlyphs()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Run.CheckBoxControl(@checked: false, tag: "subscribe"));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        ClickCheckBox(SingleCheckboxRun(view));

        SingleCheckboxRun(view).Text.Should().Be(ContentControl.CheckedGlyph);

        view.CommitToModel();
        var committed = view.Model.Blocks.OfType<Paragraph>().Single().Runs[0];
        committed.Control!.Checked.Should().BeTrue();
        committed.Text.Should().Be(ContentControl.CheckedGlyph);
    }

    /// <summary>
    /// The companion read-side half of K2: loading an already-checked custom-glyph checkbox and
    /// committing WITHOUT any click must preserve the checked state. This pins both the commit-time
    /// glyph-to-state inference (ContentControlInteractionPlanner.IsCheckBoxTextChecked) and the initial
    /// render (ResolveCheckBoxGlyph) agreeing on the same glyph -- if either used the app's fixed
    /// CheckedGlyph while the other used the metadata glyph, this round trip would flip the state.
    /// </summary>
    [StaFact]
    public void CheckboxCommit_WithoutAnyClick_PreservesCheckedStateForCustomGlyphMetadata()
    {
        var metadata = new ContentControlCheckBoxMetadata(
            CheckedState: new ContentControlCheckBoxStateMetadata("2714", "Segoe UI Symbol"),
            UncheckedState: new ContentControlCheckBoxStateMetadata("2716", "Segoe UI Symbol"));

        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Run.CheckBoxControl(@checked: true, tag: "agree", checkBoxMetadata: metadata));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        SingleCheckboxRun(view).Text.Should().Be("✔", "the initial render must also honour the custom glyph");

        view.CommitToModel();
        var committed = view.Model.Blocks.OfType<Paragraph>().Single().Runs[0];
        committed.Control!.Checked.Should().BeTrue("a commit with no edits must not flip the checked state");
        committed.Text.Should().Be("✔");
    }
}
