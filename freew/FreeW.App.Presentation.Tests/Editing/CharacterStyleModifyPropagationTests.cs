using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Editing;

/// <summary>
/// Evidence tests for freew-styles-inheritance F3 ("modifying a character style never updates text that
/// already has it applied"). The root cause was that <see cref="DocumentEditingSession.ApplyNamedStyle"/>
/// baked a character style's fields into a run's <see cref="Run.Formatting"/> without ever recording
/// <see cref="Run.StyleId"/>, so <see cref="StyleManager.ModifyStyle"/> had no run to re-cascade to
/// (unlike a run read from a docx's w:rPr/w:rStyle, which DocxReader already links). ApplyNamedStyle
/// now also sets <see cref="Run.StyleId"/> to the applied character style, alongside its existing bake,
/// so <see cref="DocumentRunFormattingResolver"/> -- wired into real rendering at
/// freew/FreeW.App.Host/Editing/DocumentView.cs:18155 and freew/FreeW.App.Avalonia/Editing/DocumentView.cs:25893
/// -- has a link to re-walk live the moment the style catalog changes.
///
/// The existing bake is deliberately left in place (it is pinned by many other tests, e.g.
/// FreeW.App.Avalonia.Tests/StylesGalleryTests.cs and this project's NamedStyleEditingSessionTests.cs,
/// which assert the run's Formatting reflects the style immediately after Apply without going through the
/// resolver). Because DocumentRunFormattingResolver's cascade OR-combines boolean fields with the run's
/// direct Formatting, a field the style already baked true at apply time stays stuck true even after the
/// style is later modified to turn it off -- only fields the style newly introduces (that were never
/// baked) pick up a later Modify live. That residual gap is a separate, pre-existing limitation in the
/// shared OR/??-based Overlay cascade (used by every other formatting caller too), not something this
/// finding's fix reaches.
/// </summary>
public sealed class CharacterStyleModifyPropagationTests
{
    [Fact]
    public void ApplyNamedStyle_NowLinksTheRunToTheStyle_SoAModifiedStyleCanRecascade()
    {
        var document = CharacterStyledDocument();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var paragraph = (Paragraph)document.Blocks[0];

        // The exact user gesture: select text, apply the "Strong" character style (Bold).
        session.ApplyNamedStyle(
            "Strong",
            Target(hasTextSelection: true, Range(0, 0, 5)));

        var styled = paragraph.Runs.Single(run => run.Text == "abcde");
        styled.Formatting.Bold.Should().BeTrue("the style's Bold is still baked directly into Formatting");

        // The fix: FreeW's own apply path now records which style produced this run's look.
        styled.StyleId.Should().Be(
            "Strong",
            "ApplyNamedStyle must link the run to the character style it applied, the same way a " +
            "docx-authored w:rPr/w:rStyle run is linked, so a later style Modify has something to " +
            "re-cascade to");

        // Modify the style through the same path Manage Styles > Modify uses, introducing a field
        // ("Underline") the style never had at apply time, so it was never baked into Formatting.
        StyleManager.ModifyStyle(
            document,
            "Strong",
            run: RunFormatting.Default with { Bold = true, Underline = true });

        // Fixed: because the run now carries StyleId, DocumentRunFormattingResolver -- the same resolver
        // every renderer uses -- re-walks the modified style live and the newly-added Underline appears
        // on already-styled text without any further edit.
        var effective = DocumentRunFormattingResolver.Resolve(document, paragraph, styled);
        effective.Bold.Should().BeTrue("Strong is still bold");
        effective.Underline.Should().BeTrue(
            "fixed: the run's StyleId lets the resolver pick up the newly-added Underline live");
    }

    [Fact]
    public void ApplyNamedStyle_TurningOffAnAlreadyBakedFieldStaysStale_KnownResolverCascadeLimitation()
    {
        // Documents the residual gap called out above: a boolean the style already baked true at apply
        // time (Bold here) stays stuck true after a later Modify turns it off, because
        // DocumentRunFormattingResolver's Overlay OR-combines direct Formatting with the live style
        // cascade, and OverlayCharacterStyle baked Bold=true into direct Formatting up front. This is a
        // pre-existing limitation of the shared cascade (also relied on by direct-formatting toggles
        // elsewhere), not a regression introduced by linking Run.StyleId.
        var document = CharacterStyledDocument();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var paragraph = (Paragraph)document.Blocks[0];

        session.ApplyNamedStyle(
            "Strong",
            Target(hasTextSelection: true, Range(0, 0, 5)));
        var styled = paragraph.Runs.Single(run => run.Text == "abcde");
        styled.StyleId.Should().Be("Strong");

        StyleManager.ModifyStyle(document, "Strong", run: RunFormatting.Default with { Italic = true });

        var effective = DocumentRunFormattingResolver.Resolve(document, paragraph, styled);
        effective.Bold.Should().BeTrue(
            "known limitation: Bold was already baked true at apply time, so it stays stuck even though " +
            "the style no longer sets it -- direct Formatting always wins over the live cascade for a " +
            "field it already carries");
        effective.Italic.Should().BeTrue(
            "fixed: Italic was never baked (the style didn't set it at apply time), so the run's StyleId " +
            "link lets it pick up the new Italic live");
    }

    [Fact]
    public void ModifyStyle_AloneAlreadyPropagatesToAnyRunThatCarriesTheStyleLink()
    {
        // Adjacent/sibling case: a run that DOES carry Run.StyleId (as a docx round-trip run with
        // w:rPr/w:rStyle would, via DocxReader.ReadRunStyleId) already re-resolves correctly the moment
        // StyleManager.ModifyStyle replaces the catalog entry -- no walk of doc.Blocks is needed. This
        // proves StyleManager.cs's current catalog-only update is correct and sufficient for a properly
        // linked run, so the fix does not belong there.
        var document = CharacterStyledDocument();
        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.Runs.Clear();
        paragraph.Runs.Add(new Run("abcde", RunFormatting.Default) { StyleId = "Strong" });
        var linkedRun = paragraph.Runs[0];

        StyleManager.ModifyStyle(document, "Strong", run: RunFormatting.Default with { Italic = true });

        var effective = DocumentRunFormattingResolver.Resolve(document, paragraph, linkedRun);
        effective.Bold.Should().BeFalse("the run's own Formatting carries no baked Bold, so it follows the new style");
        effective.Italic.Should().BeTrue("the resolver re-cascades the modified style immediately, like a paragraph style");
    }

    private static NamedStyleApplicationTarget Target(
        bool hasTextSelection,
        params DocumentTextRange[] ranges) =>
        new(
            ranges,
            ranges.Select(range => range.Start.BlockIndex).Distinct().ToArray(),
            hasTextSelection);

    private static DocumentTextRange Range(int blockIndex, int startOffset, int endOffset) =>
        new(
            new DocumentTextPosition(blockIndex, startOffset),
            new DocumentTextPosition(blockIndex, endOffset));

    private static TextDocument CharacterStyledDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("abcde"));
        document.Styles["Strong"] = new DocumentStyle
        {
            Id = "Strong",
            Name = "Strong",
            Type = StyleType.Character,
            Run = RunFormatting.Default with { Bold = true },
        };
        return document;
    }
}
