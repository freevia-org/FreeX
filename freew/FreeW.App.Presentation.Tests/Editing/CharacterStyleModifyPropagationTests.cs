using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Editing;

/// <summary>
/// Evidence tests for freew-styles-pane F3 ("modifying a character style never updates text that
/// already has it applied"). These tests do NOT accompany a production change: they demonstrate that
/// the reported symptom is real, but that its root cause is NOT in <see cref="StyleManager.ModifyStyle"/>
/// (which this wave owns) -- it is that <see cref="DocumentEditingSession.ApplyNamedStyle"/> never sets
/// <see cref="Run.StyleId"/> when baking a character style into a run's <see cref="Run.Formatting"/>.
/// FreeW already has the dynamic paragraph-style/character-style/direct-formatting cascade the finding
/// says is missing (<see cref="DocumentRunFormattingResolver"/>), wired into real rendering
/// (freew/FreeW.App.Host/Editing/DocumentView.cs:18155 and freew/FreeW.App.Avalonia/Editing/DocumentView.cs:25893).
/// A run that legitimately carries <see cref="Run.StyleId"/> (e.g. read from a docx's w:rPr/w:rStyle via
/// DocxReader.ReadRunStyleId) already re-resolves correctly the moment <see cref="StyleManager.ModifyStyle"/>
/// replaces the catalog entry -- exactly mirroring how paragraph styles work. FreeW's own "Apply Named
/// Style" ribbon action just never establishes that link for text it styles itself.
/// </summary>
public sealed class CharacterStyleModifyPropagationTests
{
    [Fact]
    public void ApplyNamedStyle_NeverLinksTheRunToTheStyle_SoAModifiedStyleLeavesBakedTextStale()
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
        styled.Formatting.Bold.Should().BeTrue("the style's Bold got baked directly into Formatting");

        // Root-cause check: FreeW's own apply path never records which style produced this run's look.
        styled.StyleId.Should().BeNull(
            "ApplyNamedStyle bakes the style's Run fields into Formatting but never sets Run.StyleId, " +
            "so the run carries no memory of which style produced it");

        // Now modify the style through the same path Manage Styles > Modify uses: uncheck Bold, check Italic.
        StyleManager.ModifyStyle(document, "Strong", run: RunFormatting.Default with { Italic = true });

        // Reproduces the finding: resolving this run's effective formatting (the same resolver every
        // renderer uses) still shows Bold and never picks up Italic, because the old style's Bold=true
        // is now baked into Formatting itself and the run has no style link for the resolver to re-cascade.
        var effective = DocumentRunFormattingResolver.Resolve(document, paragraph, styled);
        effective.Bold.Should().BeTrue("bug: the already-styled run stays bold");
        effective.Italic.Should().BeFalse("bug: the already-styled run never picks up the new Italic");
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
