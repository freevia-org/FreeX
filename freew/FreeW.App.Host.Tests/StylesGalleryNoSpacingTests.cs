using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// R160 / freew-styles-pane F1: the WPF Home &gt; Styles gallery's "No Spacing" swatch used to be
/// paired with style id "Normal" instead of the real built-in id "NoSpacing" (<see cref="StylesGallery"/>
/// line 29), so both the hover preview and the click-commit silently applied Normal -- leaving any
/// existing extra paragraph spacing untouched, the opposite of what the label promises. These tests
/// exercise the two paths a defect like this can hide in: the strip must actually list the swatch
/// under the correct id (reader), and clicking it must actually commit that id to the paragraph
/// (writer) -- asserting a substring of one path is not enough (round-153 gotcha). The paragraph
/// reference is always re-fetched from the model AFTER the action under test: MoveCaretToBlockForTest
/// re-splices the model's leaf blocks, so a reference captured before it (or before CommitToModel)
/// goes stale and reads back null regardless of what the production code actually did.
/// </summary>
public sealed class StylesGalleryNoSpacingTests
{
    private static DocumentView FreshEditor()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Some body text"));
        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.MoveCaretToBlockForTest(0, 0);
        return editor;
    }

    private static Paragraph FirstParagraph(DocumentView editor) =>
        (Paragraph)editor.Model.Blocks[0];

    // ── Reader: the built-in table itself must carry the real style id ─────────────────────────────

    [StaFact]
    public void BuiltIns_PairsNoSpacingLabelWithTheRealStyleId()
    {
        var builtIns = (System.Collections.Generic.IEnumerable<(string Name, string Id)>)
            typeof(StylesGallery)
                .GetField("BuiltIns", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null)!;

        var noSpacing = builtIns.Single(e => e.Name == "No Spacing");
        noSpacing.Id.Should().Be("NoSpacing",
            "the swatch labelled 'No Spacing' must apply the real built-in id, not 'Normal'");
    }

    // ── Writer: clicking (CommitStylePreview) must actually apply NoSpacing, not Normal ─────────────

    [StaFact]
    public void CommitStylePreview_NoSpacing_OnFreshDocument_SetsNoSpacingStyleId()
    {
        var editor = FreshEditor();

        // A brand-new document does not pre-seed NoSpacing (unlike Normal/Heading1/Title/etc, it
        // carries no BuiltInStyleRole) -- the gallery must seed it itself so the click below is not a
        // silent no-op against the DocumentView/ParagraphStylePreviewSession Styles.ContainsKey guards.
        editor.Model.Styles.ContainsKey("NoSpacing").Should().BeFalse(
            "sanity: a fresh document does not auto-seed NoSpacing (Role = None)");

        // Building the gallery is what the live Home tab does (MainWindow.cs calls StylesGallery.Build);
        // it must make the swatch usable, seeding NoSpacing into the document's catalog as a side effect.
        StylesGallery.Build(editor);

        editor.CommitStylePreview("NoSpacing");

        var styleId = FirstParagraph(editor).StyleId;
        styleId.Should().Be("NoSpacing",
            "clicking the swatch labelled 'No Spacing' must set the paragraph's style to NoSpacing");
        styleId.Should().NotBe("Normal",
            "the pre-fix defect silently applied Normal instead of NoSpacing");
    }

    [StaFact]
    public void CommitStylePreview_NoSpacing_ResolvesToZeroParagraphSpacing()
    {
        var editor = FreshEditor();
        StylesGallery.Build(editor);

        editor.CommitStylePreview("NoSpacing");

        var styleId = FirstParagraph(editor).StyleId;
        var style = editor.Model.Styles[styleId!];
        style.Paragraph.SpaceBeforePt.Should().Be(0);
        style.Paragraph.SpaceAfterPt.Should().Be(0);
    }

    // ── Sibling no-regression: the "Normal" swatch (never buggy) must still commit Normal ───────────

    [StaFact]
    public void CommitStylePreview_Normal_StillCommitsNormal()
    {
        var editor = FreshEditor();
        StylesGallery.Build(editor);

        editor.CommitStylePreview("Normal");

        FirstParagraph(editor).StyleId.Should().Be("Normal");
    }

    // ── Sibling no-regression: a different built-in (Heading1) is unaffected by this fix ────────────

    [StaFact]
    public void CommitStylePreview_Heading1_StillCommitsHeading1()
    {
        var editor = FreshEditor();
        StylesGallery.Build(editor);

        editor.CommitStylePreview("Heading1");

        FirstParagraph(editor).StyleId.Should().Be("Heading1");
    }

    // ── The swatch strip actually lists "No Spacing" for a fresh document ───────────────────────────

    [StaFact]
    public void Build_VisibleStrip_ListsNoSpacingSwatchOnFreshDocument()
    {
        var editor = FreshEditor();
        var gallery = (System.Windows.FrameworkElement)StylesGallery.Build(editor);

        var buttons = System.Windows.LogicalTreeHelper.GetChildren(gallery)
            .OfType<System.Windows.DependencyObject>()
            .SelectMany(Descendants)
            .OfType<System.Windows.Controls.Button>()
            .Where(b => b.ToolTip as string == "No Spacing");

        buttons.Should().NotBeEmpty("the 'No Spacing' swatch must be visible even on a brand-new document");
    }

    private static System.Collections.Generic.IEnumerable<System.Windows.DependencyObject> Descendants(
        System.Windows.DependencyObject root)
    {
        yield return root;
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
            foreach (var d in Descendants(child))
                yield return d;
    }
}
