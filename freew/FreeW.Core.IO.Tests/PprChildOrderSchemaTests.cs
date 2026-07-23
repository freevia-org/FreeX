using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Regression tests for DD1 (CT_PPr schema-order bug): <see cref="DocxWriter"/>'s
/// <c>BuildStyleParagraphProperties</c> previously emitted <c>w:jc</c> before <c>w:spacing</c> and
/// <c>w:ind</c>, violating the CT_PPr / EG_PPrBase schema sequence (spacing → ind → jc). This caused
/// Word's strict validator to flag the file as "unreadable content / repair" whenever a tracked paragraph-
/// format revision (<c>w:pPrChange</c>) or a style definition carried a non-Left alignment together with
/// indent or spacing values.
///
/// The correct order, as specified by ECMA-376 / ISO 29500 CT_PPr, is:
///   ... w:spacing, w:ind, [contextualSpacing], w:jc, ...
///
/// These tests verify the element order in the emitted XML and that OpenXmlValidator reports no schema
/// errors for both the pPrChange and the style-definition paths through the fixed helper.
/// </summary>
public class PprChildOrderSchemaTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static byte[] WriteDocx(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument GetDocumentXml(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static XDocument GetStylesXml(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/styles.xml")!.Open();
        return XDocument.Load(entry);
    }

    /// <summary>
    /// Validates the DOCX package with <see cref="OpenXmlValidator"/> at the Microsoft365 conformance
    /// level and returns any schema-category errors as a list of description strings.
    /// </summary>
    private static List<string> SchemaErrors(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var wdDoc = WordprocessingDocument.Open(ms, isEditable: false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(wdDoc)
            .Where(e => e.ErrorType == ValidationErrorType.Schema)
            .Select(e => $"{e.Description} @ {e.Path?.XPath}")
            .ToList();
    }

    // -------------------------------------------------------------------------
    // Helper documents
    // -------------------------------------------------------------------------

    /// <summary>
    /// A paragraph whose properties were changed under Track Changes: the CURRENT formatting is left-
    /// aligned (default); the PREVIOUS formatting (before the tracked change) was center-aligned with a
    /// 0.5 in (36 pt) left indent, and 12 pt before / 6 pt after spacing.
    /// </summary>
    private static TextDocument BuildDocumentWithPPrChangeHavingAlignmentAndIndentAndSpacing()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph("tracked change paragraph");
        // Current (new) formatting: plain left-aligned default.
        paragraph.Formatting = ParagraphFormatting.Default;
        // Previous (old) formatting: center + 0.5 in left indent + spacing before/after.
        var previousFormatting = new ParagraphFormatting
        {
            Alignment = TextAlignment.Center,
            IndentLeftPt = 36,          // 0.5 in
            SpaceBeforePt = 12,
            SpaceAfterPt = 6,
            ContextualSpacing = true,
        };
        paragraph.ParagraphFormatRevision = new ParagraphFormatRevision(
            previousFormatting, "Dave", "2026-06-26T12:00:00Z");
        doc.Blocks.Add(paragraph);
        return doc;
    }

    // -------------------------------------------------------------------------
    // pPrChange child-order tests (the nested previous-pPr inside w:pPrChange)
    // -------------------------------------------------------------------------

    /// <summary>
    /// DD1 regression: the nested <c>w:pPr</c> inside <c>w:pPrChange</c> must have its children in
    /// CT_PPr schema order: <c>w:spacing</c> and <c>w:ind</c> BEFORE <c>w:jc</c>.
    /// </summary>
    [Fact]
    public void PPrChange_NestedPPr_EmitsSpacingAndIndBeforeJc()
    {
        var bytes = WriteDocx(BuildDocumentWithPPrChangeHavingAlignmentAndIndentAndSpacing());
        var docXml = GetDocumentXml(bytes);

        // The nested pPr is the one inside w:pPrChange (not the outer paragraph pPr).
        var nestedPPr = docXml
            .Descendants(W + "pPrChange")
            .Select(c => c.Element(W + "pPr"))
            .FirstOrDefault(p => p is not null);

        nestedPPr.Should().NotBeNull("w:pPrChange must carry a nested w:pPr");

        var childNames = nestedPPr!.Elements().Select(e => e.Name.LocalName).ToList();

        // All four elements must be present.
        childNames.Should().Contain("spacing", "previous-pPr had before/after spacing");
        childNames.Should().Contain("ind",     "previous-pPr had a left indent");
        childNames.Should().Contain("contextualSpacing", "previous-pPr enabled contextual spacing");
        childNames.Should().Contain("jc",      "previous-pPr had center alignment");

        // Schema order: spacing < ind < contextualSpacing < jc (all indices must be strictly ascending).
        var idxSpacing = childNames.IndexOf("spacing");
        var idxInd     = childNames.IndexOf("ind");
        var idxContextualSpacing = childNames.IndexOf("contextualSpacing");
        var idxJc      = childNames.IndexOf("jc");

        idxSpacing.Should().BeLessThan(idxInd,
            "CT_PPr requires w:spacing before w:ind");
        idxInd.Should().BeLessThan(idxJc,
            "CT_PPr requires w:ind before w:jc");
        idxInd.Should().BeLessThan(idxContextualSpacing,
            "CT_PPr requires w:ind before w:contextualSpacing");
        idxContextualSpacing.Should().BeLessThan(idxJc,
            "CT_PPr requires w:contextualSpacing before w:jc");
    }

    /// <summary>
    /// The DOCX produced with a <c>w:pPrChange</c> whose previous-pPr has center alignment, 0.5 in
    /// indent, and before/after spacing must pass <see cref="OpenXmlValidator"/> with no schema errors.
    /// Before the DD1 fix, the out-of-order <c>w:jc</c> in the nested <c>w:pPr</c> caused a schema
    /// validation failure (the root cause of Word's "unreadable content / repair" prompt).
    /// </summary>
    [Fact]
    public void PPrChange_WithAlignmentAndIndentAndSpacing_PassesOpenXmlValidator()
    {
        var bytes = WriteDocx(BuildDocumentWithPPrChangeHavingAlignmentAndIndentAndSpacing());
        var errors = SchemaErrors(bytes);
        errors.Should().BeEmpty(
            "the nested pPr inside w:pPrChange must conform to the CT_PPr schema sequence; " +
            $"found: {string.Join("; ", errors)}");
    }

    // -------------------------------------------------------------------------
    // Style-definition child-order tests
    // -------------------------------------------------------------------------

    /// <summary>
    /// A style definition whose paragraph formatting has alignment + indent + spacing must emit its
    /// <c>w:pPr</c> children in CT_PPr schema order: <c>w:spacing</c> and <c>w:ind</c> BEFORE <c>w:jc</c>.
    /// </summary>
    [Fact]
    public void StyleDefinition_PPr_EmitsSpacingAndIndBeforeJc()
    {
        var doc = TextDocument.CreateEmpty();
        StyleManager.CreateStyle(
            doc, "DD1 Style", basedOnId: "Normal",
            RunFormatting.Default,
            new ParagraphFormatting
            {
                Alignment    = TextAlignment.Center,
                IndentLeftPt = 36,      // 0.5 in
                SpaceBeforePt = 12,
                SpaceAfterPt  = 6,
                ContextualSpacing = true,
            });

        var bytes   = WriteDocx(doc);
        var styles  = GetStylesXml(bytes);

        var stylePPr = styles.Root!
            .Elements(W + "style")
            .Where(e => (string?)e.Attribute(W + "styleId") == "DD1Style")
            .Select(e => e.Element(W + "pPr"))
            .FirstOrDefault(p => p is not null);

        stylePPr.Should().NotBeNull("the style definition must emit a w:pPr");

        var childNames = stylePPr!.Elements().Select(e => e.Name.LocalName).ToList();

        childNames.Should().Contain("spacing");
        childNames.Should().Contain("ind");
        childNames.Should().Contain("contextualSpacing");
        childNames.Should().Contain("jc");

        var idxSpacing = childNames.IndexOf("spacing");
        var idxInd     = childNames.IndexOf("ind");
        var idxContextualSpacing = childNames.IndexOf("contextualSpacing");
        var idxJc      = childNames.IndexOf("jc");

        idxSpacing.Should().BeLessThan(idxInd,
            "CT_PPr requires w:spacing before w:ind in a style definition");
        idxInd.Should().BeLessThan(idxJc,
            "CT_PPr requires w:ind before w:jc in a style definition");
        idxInd.Should().BeLessThan(idxContextualSpacing,
            "CT_PPr requires w:ind before w:contextualSpacing in a style definition");
        idxContextualSpacing.Should().BeLessThan(idxJc,
            "CT_PPr requires w:contextualSpacing before w:jc in a style definition");
    }

    /// <summary>
    /// A DOCX whose styles.xml contains a style definition with alignment + indent + spacing must pass
    /// <see cref="OpenXmlValidator"/> with no schema errors. The pre-DD1 out-of-order <c>w:jc</c> in
    /// the style <c>w:pPr</c> was a latent bug that this fix also corrects.
    /// </summary>
    [Fact]
    public void StyleDefinition_WithAlignmentAndIndentAndSpacing_PassesOpenXmlValidator()
    {
        var doc = TextDocument.CreateEmpty();
        StyleManager.CreateStyle(
            doc, "DD1 Style", basedOnId: "Normal",
            RunFormatting.Default,
            new ParagraphFormatting
            {
                Alignment     = TextAlignment.Center,
                IndentLeftPt  = 36,
                SpaceBeforePt = 12,
                SpaceAfterPt  = 6,
                ContextualSpacing = true,
            });

        var errors = SchemaErrors(WriteDocx(doc));
        errors.Should().BeEmpty(
            "a style definition pPr must conform to the CT_PPr schema sequence; " +
            $"found: {string.Join("; ", errors)}");
    }
}
