using System.Text.Json;
using System.Security.Cryptography;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class VisualEvidencePlannerTests
{
    [Fact]
    public void ScenarioCatalog_IncludesF2PageCompositionContracts()
    {
        var scenarios = FreeWVisualEvidencePlanner.Scenarios;

        scenarios.Select(s => s.ScenarioId).Should().Contain([
            "f2-hf-basic",
            "f2-hf-firstpage",
            "f2-hf-oddeven",
            "field-page-number-variants",
            "references-heavy-fields",
            "f2-footnotes",
            "f2-endnotes",
            "f2-columns",
            "f2-border-watermark",
            "f2-section-landscape",
            "f2-tracked-changes",
            "f2-comments",
            "table-layout-complex",
            "table-pagination-repeat-header",
            "drawing-objects-complex",
            "object-format-position-size-style",
            "chart-smartart-complex",
            "wordart-watermark-stress",
            "wordart-picture-watermark-layout",
            "page-composition-print-layout",
            "page-composition-columns",
            "page-composition-border-watermark",
            "page-composition-web-layout",
            "page-composition-draft",
            "page-composition-floating-image",
            "backstage-print-preview-fidelity",
            "backstage-pdf-export-fidelity"]);

        var sectionScenario = FreeWVisualEvidencePlanner.ResolveScenario("f2-section-landscape.docx");
        sectionScenario.ExpectedFeatureTags.Should().Contain(["f2", "section-geometry", "portrait-landscape"]);
        sectionScenario.Composition.ExpectsSectionGeometryChange.Should().BeTrue();

        var trackedScenario = FreeWVisualEvidencePlanner.ResolveScenario("f2-tracked-changes");
        trackedScenario.ExpectedFeatureTags.Should().Contain(["f2", "tracked-changes", "revision-marks"]);
        trackedScenario.ExpectedOutputNamePattern.Should().Be("f2-tracked-changes_p{page}.png");
        trackedScenario.Composition.ExpectsTrackedChanges.Should().BeTrue();

        var commentsScenario = FreeWVisualEvidencePlanner.ResolveScenario("f2-comments");
        commentsScenario.ExpectedFeatureTags.Should().Contain(["f2", "comments", "comment-anchors"]);
        commentsScenario.ExpectedOutputNamePattern.Should().Be("f2-comments_p{page}.png");
        commentsScenario.Composition.ExpectsComments.Should().BeTrue();

        var fieldScenario = FreeWVisualEvidencePlanner.ResolveScenario("field-page-number-variants");
        fieldScenario.ExpectedFeatureTags.Should().Contain([
            "fields",
            "page-number-fields",
            "numpages-fields",
            "document-property-fields",
            "complex-fields",
            "header-footer-fields"]);
        fieldScenario.ExpectedOutputNamePattern.Should().Be("field-page-number-variants_p{page}.png");
        fieldScenario.MinimumExpectedOutputs.Should().Be(3);
        fieldScenario.Composition.ExpectsHeadersFooters.Should().BeTrue();

        var referencesScenario = FreeWVisualEvidencePlanner.ResolveScenario("references-heavy-fields");
        referencesScenario.ExpectedFeatureTags.Should().Contain([
            "references",
            "source-manager",
            "citation-fields",
            "bibliography-fields",
            "toa-fields",
            "cached-toa-page-number-sentinel",
            "generated-toa-page-references",
            "legal-authorities"]);
        referencesScenario.ExpectedOutputNamePattern.Should().Be("references-heavy-fields_p{page}.png");
        referencesScenario.MinimumExpectedOutputs.Should().Be(2);

        var floatingScenario = FreeWVisualEvidencePlanner.ResolveScenario("page-composition-floating-image");
        floatingScenario.Composition.ExpectsFloatingObjects.Should().BeTrue();

        var previewScenario = FreeWVisualEvidencePlanner.ResolveScenario("backstage-print-preview-fidelity");
        previewScenario.ExpectedFeatureTags.Should().Contain(["backstage", "print-preview", "fixed-layout", "header-footer", "columns", "page-border", "watermark"]);
        previewScenario.ExpectedOutputNamePattern.Should().Be("backstage-print-preview_p{page}.png");
        previewScenario.MinimumExpectedOutputs.Should().Be(2);
        previewScenario.Composition.ExpectsHeadersFooters.Should().BeTrue();
        previewScenario.Composition.ExpectsColumns.Should().BeTrue();
        previewScenario.Composition.ExpectsPageBorder.Should().BeTrue();
        previewScenario.Composition.ExpectsWatermark.Should().BeTrue();

        var pdfScenario = FreeWVisualEvidencePlanner.ResolveScenario("backstage-pdf-export-fidelity");
        pdfScenario.ExpectedFeatureTags.Should().Contain(["backstage", "pdf-export", "pdf-rasterized", "header-footer", "columns", "page-border", "watermark"]);
        pdfScenario.ExpectedOutputNamePattern.Should().Be("backstage-pdf-export_p{page}.png");
        pdfScenario.MinimumExpectedOutputs.Should().Be(2);
        pdfScenario.Composition.ExpectsHeadersFooters.Should().BeTrue();
        pdfScenario.Composition.ExpectsColumns.Should().BeTrue();
        pdfScenario.Composition.ExpectsPageBorder.Should().BeTrue();
        pdfScenario.Composition.ExpectsWatermark.Should().BeTrue();

        var columnsScenario = FreeWVisualEvidencePlanner.ResolveScenario("f2-columns");
        columnsScenario.Composition.ExpectsColumns.Should().BeTrue();

        var footnoteScenario = FreeWVisualEvidencePlanner.ResolveScenario("f2-footnotes");
        footnoteScenario.Composition.ExpectsFootnotes.Should().BeTrue();

        var endnoteScenario = FreeWVisualEvidencePlanner.ResolveScenario("f2-endnotes");
        endnoteScenario.Composition.ExpectsEndnotes.Should().BeTrue();

        var borderScenario = FreeWVisualEvidencePlanner.ResolveScenario("f2-border-watermark");
        borderScenario.Composition.ExpectsPageBorder.Should().BeTrue();
        borderScenario.Composition.ExpectsWatermark.Should().BeTrue();

        var tableScenario = FreeWVisualEvidencePlanner.ResolveScenario("table-layout-complex");
        tableScenario.ExpectedFeatureTags.Should().Contain(["table-layout", "merged-cells", "repeat-header-row"]);
        tableScenario.ExpectedOutputNamePattern.Should().Be("table-layout-complex_p{page}.png");
        tableScenario.Composition.ExpectsTables.Should().BeTrue();

        var tablePaginationScenario = FreeWVisualEvidencePlanner.ResolveScenario("table-pagination-repeat-header");
        tablePaginationScenario.ExpectedFeatureTags.Should().Contain(["table-pagination", "repeat-header-row", "keep-rows"]);
        tablePaginationScenario.ExpectedOutputNamePattern.Should().Be("table-pagination-repeat-header_p{page}.png");
        tablePaginationScenario.MinimumExpectedOutputs.Should().Be(2);
        tablePaginationScenario.Composition.ExpectsTables.Should().BeTrue();

        var drawingScenario = FreeWVisualEvidencePlanner.ResolveScenario("drawing-objects-complex");
        drawingScenario.ExpectedFeatureTags.Should().Contain([
            "drawing-objects",
            "charts",
            "smartart",
            "wordart",
            "drawing-effects",
            "image-effects",
            "shape-effects",
            "wordart-effects",
            "grouped-child-effects",
            "grouped-child-shape-effects",
            "shadow",
            "glow",
            "reflection",
            "artistic-effect"]);
        drawingScenario.ExpectedOutputNamePattern.Should().Be("drawing-objects-complex_p{page}.png");
        drawingScenario.Composition.ExpectsFloatingObjects.Should().BeTrue();

        var objectFormatScenario = FreeWVisualEvidencePlanner.ResolveScenario("object-format-position-size-style");
        objectFormatScenario.ExpectedFeatureTags.Should().Contain([
            "object-format",
            "position-size",
            "alt-text",
            "shapes",
            "images",
            "wordart",
            "square-wrap",
            "top-bottom-wrap",
            "behind-text",
            "in-front",
            "z-order"]);
        objectFormatScenario.ExpectedOutputNamePattern.Should().Be("object-format-position-size-style_p{page}.png");
        objectFormatScenario.Composition.ExpectsFloatingObjects.Should().BeTrue();

        var chartSmartArtScenario = FreeWVisualEvidencePlanner.ResolveScenario("chart-smartart-complex");
        chartSmartArtScenario.ExpectedFeatureTags.Should().Contain(["chart-smartart", "chart-palette", "scatter-markers", "smartart-style"]);
        chartSmartArtScenario.ExpectedOutputNamePattern.Should().Be("chart-smartart-complex_p{page}.png");
        chartSmartArtScenario.MinimumExpectedOutputs.Should().Be(1);

        var wordArtWatermarkScenario = FreeWVisualEvidencePlanner.ResolveScenario("wordart-watermark-stress");
        wordArtWatermarkScenario.ExpectedFeatureTags.Should().Contain([
            "drawing-objects",
            "wordart",
            "watermark",
            "page-border",
            "drawing-effects",
            "shape-effects",
            "wordart-effects",
            "shadow",
            "glow"]);
        wordArtWatermarkScenario.ExpectedOutputNamePattern.Should().Be("wordart-watermark-stress_p{page}.png");
        wordArtWatermarkScenario.Composition.ExpectsFloatingObjects.Should().BeTrue();
        wordArtWatermarkScenario.Composition.ExpectsWatermark.Should().BeTrue();
        wordArtWatermarkScenario.Composition.ExpectsPageBorder.Should().BeTrue();

        var pictureWatermarkScenario = FreeWVisualEvidencePlanner.ResolveScenario("wordart-picture-watermark-layout");
        pictureWatermarkScenario.ExpectedFeatureTags.Should().Contain([
            "wordart-watermark-layout",
            "wordart",
            "picture-watermark",
            "watermark",
            "page-border",
            "columns",
            "in-front"]);
        pictureWatermarkScenario.ExpectedOutputNamePattern.Should().Be("wordart-picture-watermark-layout_p{page}.png");
        pictureWatermarkScenario.Composition.ExpectsWatermark.Should().BeTrue();
        pictureWatermarkScenario.Composition.ExpectsColumns.Should().BeTrue();
        pictureWatermarkScenario.Composition.ExpectsPageBorder.Should().BeTrue();
        pictureWatermarkScenario.Composition.ExpectsFloatingObjects.Should().BeTrue();
    }

    [Fact]
    public void SharedBackstagePrintExportFactory_BuildsPrintSpecificContract()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument(
            "Backstage Print Preview Fidelity",
            "Print preview renderer capture");

        document.FinalSectionHeadersFooters.Header.Should().NotBeNull();
        document.FinalSectionHeadersFooters.Footer.Should().NotBeNull();
        document.Page.ColumnCount.Should().Be(2);
        document.Page.ColumnsLineBetween.Should().BeTrue();
        document.Page.PageBorder.Should().NotBeNull();
        document.Page.WatermarkOptions.Should().NotBeNull();

        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "backstage-print-preview-fidelity",
            document.Page,
            pageNumber: 1,
            pageCount: 2,
            outputName: "backstage-print-preview_p1.png",
            headerSlotName: "default-header",
            footerSlotName: "default-footer",
            document: document);

        expectation.Composition.ExpectsHeadersFooters.Should().BeTrue();
        expectation.Composition.ExpectsColumns.Should().BeTrue();
        expectation.Features.Columns.Count.Should().Be(2);
        expectation.Features.PageBorder.Present.Should().BeTrue();
        expectation.Features.Watermark.Present.Should().BeTrue();
        expectation.HeaderSlotName.Should().Be("default-header");
        expectation.FooterSlotName.Should().Be("default-footer");
    }

    [Fact]
    public void SharedNotePlacementFactories_BuildF2NoteContracts()
    {
        var footnotes = FreeWVisualEvidenceDocumentFactory.BuildFootnotePlacementDocument();
        var endnotes = FreeWVisualEvidenceDocumentFactory.BuildEndnotePlacementDocument();

        footnotes.Footnotes.Keys.Should().BeEquivalentTo([1, 2]);
        footnotes.Endnotes.Should().BeEmpty();
        footnotes.Blocks
            .OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(r => r.FootnoteId is not null)
            .Select(r => r.FootnoteId!.Value)
            .Should().ContainInOrder(1, 2);

        endnotes.Endnotes.Keys.Should().BeEquivalentTo([1, 2]);
        endnotes.Footnotes.Should().BeEmpty();
        endnotes.Blocks
            .OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(r => r.EndnoteId is not null)
            .Select(r => r.EndnoteId!.Value)
            .Should().ContainInOrder(1, 2);

        var footnoteExpectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "f2-footnotes",
            footnotes.Page,
            pageNumber: 1,
            pageCount: 2,
            outputName: "f2-footnotes_p1.png",
            hasFootnotes: true,
            document: footnotes);
        footnoteExpectation.ExpectedOutputName.Should().Be("f2-footnotes_p1.png");
        footnoteExpectation.HasFootnotes.Should().BeTrue();
        footnoteExpectation.Composition.ExpectsFootnotes.Should().BeTrue();

        var endnoteExpectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "f2-endnotes",
            endnotes.Page,
            pageNumber: 3,
            pageCount: 3,
            outputName: "f2-endnotes_p3.png",
            hasEndnotes: true,
            isSyntheticPage: true,
            document: endnotes);
        endnoteExpectation.ExpectedOutputName.Should().Be("f2-endnotes_p3.png");
        endnoteExpectation.HasEndnotes.Should().BeTrue();
        endnoteExpectation.IsSyntheticPage.Should().BeTrue();
        endnoteExpectation.Composition.ExpectsEndnotes.Should().BeTrue();
    }

    [Fact]
    public void SharedNoteRegionPlanner_BuildsFootnoteAndSyntheticEndnoteRows()
    {
        var footnotes = FreeWVisualEvidenceDocumentFactory.BuildFootnotePlacementDocument();
        var endnotes = FreeWVisualEvidenceDocumentFactory.BuildEndnotePlacementDocument();
        var (contentWidth, _) = PageLayout.ContentAreaDip(footnotes.Page);

        var footnotePlan = DocumentNoteRegionPlanner.BuildFootnoteRegion(
            footnotes,
            [1],
            pageNumber: 1,
            contentWidth);

        footnotePlan.Kind.Should().Be(DocumentNoteRegionKind.Footnotes);
        footnotePlan.IsSyntheticPage.Should().BeFalse();
        footnotePlan.Heading.Should().BeNull();
        footnotePlan.SeparatorWidthDip.Should().Be(DocumentNoteRegionPlanner.FootnoteSeparatorWidthDip);
        footnotePlan.Rows.Should().ContainSingle();
        footnotePlan.Rows[0].Label.Should().Be("1");
        footnotePlan.Rows[0].Text.Should().Contain("bottom of page 1");
        footnotePlan.EstimatedHeightDip.Should().BeGreaterThan(0);

        var endnotePlan = DocumentNoteRegionPlanner.BuildEndnoteRegion(
            endnotes,
            DocumentNoteRegionPlanner.EndnoteIdsForSyntheticPage(endnotes),
            pageNumber: 3,
            contentWidth,
            isSyntheticPage: true);

        endnotePlan.Kind.Should().Be(DocumentNoteRegionKind.Endnotes);
        endnotePlan.IsSyntheticPage.Should().BeTrue();
        endnotePlan.Heading.Should().Be("Endnotes");
        endnotePlan.SeparatorWidthDip.Should().Be(contentWidth);
        endnotePlan.Rows.Select(r => r.Label).Should().ContainInOrder("1", "2");
        endnotePlan.Rows.Select(r => r.Text).Should().Contain(t => t.Contains("very end of the document"));
    }

    [Fact]
    public void SharedReviewFactories_BuildF2ReviewContracts()
    {
        var tracked = FreeWVisualEvidenceDocumentFactory.BuildTrackedChangesReviewDocument();
        var comments = FreeWVisualEvidenceDocumentFactory.BuildCommentsReviewDocument();

        var revisions = tracked.Blocks
            .OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(r => r.Revision != RevisionKind.None)
            .ToList();
        revisions.Where(r => r.Revision == RevisionKind.Inserted).Should().HaveCount(4);
        revisions.Where(r => r.Revision == RevisionKind.Deleted).Should().HaveCount(2);
        revisions.Select(r => r.RevisionAuthor).Should().Contain(["Alice", "Bob", "Carol"]);

        comments.Comments.Keys.Should().BeEquivalentTo([1, 2]);
        comments.Blocks
            .OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(r => r.IsCommentReference)
            .Select(r => r.CommentId!.Value)
            .Should().ContainInOrder(1, 2);
        comments.Blocks
            .OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(r => r.CommentId is not null && !r.IsCommentReference)
            .Select(r => r.CommentId!.Value)
            .Should().Contain([1, 2]);

        var trackedExpectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "f2-tracked-changes",
            tracked.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "f2-tracked-changes_p1.png",
            document: tracked);
        trackedExpectation.ExpectedOutputName.Should().Be("f2-tracked-changes_p1.png");
        trackedExpectation.Composition.ExpectsTrackedChanges.Should().BeTrue();

        var commentsExpectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "f2-comments",
            comments.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "f2-comments_p1.png",
            document: comments);
        commentsExpectation.ExpectedOutputName.Should().Be("f2-comments_p1.png");
        commentsExpectation.Composition.ExpectsComments.Should().BeTrue();
    }

    [Fact]
    public void SharedFieldPageNumberFactory_BuildsFieldVariantContracts()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument();

        document.Page.DifferentFirstPage.Should().BeTrue();
        document.Page.DifferentOddEvenPages.Should().BeTrue();
        document.FinalSectionHeadersFooters.FirstHeader.Should().NotBeNull();
        document.FinalSectionHeadersFooters.FirstFooter.Should().NotBeNull();
        document.FinalSectionHeadersFooters.EvenHeader.Should().NotBeNull();
        document.FinalSectionHeadersFooters.EvenFooter.Should().NotBeNull();
        document.FinalSectionHeadersFooters.Header.Should().NotBeNull();
        document.FinalSectionHeadersFooters.Footer.Should().NotBeNull();
        document.Properties.Title.Should().Be("Field Page Number Evidence");
        document.Properties.Author.Should().Be("FreeW Visual Evidence");

        var fields = FreeWVisualEvidencePlanner.BuildFieldExpectation(document);
        fields.SimpleFieldCount.Should().Be(14);
        fields.ComplexFieldCount.Should().Be(4);
        fields.BodyFieldCount.Should().Be(7);
        fields.HeaderFooterFieldCount.Should().Be(11);
        fields.PageFieldCount.Should().Be(5);
        fields.NumPagesFieldCount.Should().Be(4);
        fields.DocumentPropertyFieldCount.Should().Be(9);
        fields.HasPageFields.Should().BeTrue();
        fields.HasNumPagesFields.Should().BeTrue();
        fields.HasDocumentPropertyFields.Should().BeTrue();
        fields.HasComplexFields.Should().BeTrue();
        fields.HasComplexResultFields.Should().BeTrue();
        fields.HeaderFooterSlotNames.Should().BeEquivalentTo([
            "first-header",
            "first-footer",
            "even-header",
            "even-footer",
            "header",
            "footer"]);
        fields.FieldKinds.Should().Contain([
            nameof(RunFieldKind.PageNumber),
            nameof(RunFieldKind.NumPages),
            nameof(RunFieldKind.Title),
            nameof(RunFieldKind.Author),
            nameof(RunFieldKind.Subject),
            nameof(RunFieldKind.Keywords),
            nameof(RunFieldKind.DocComments),
            "Complex:PAGE",
            "Complex:NUMPAGES",
            "Complex:TITLE",
            "Complex:AUTHOR"]);
    }

    [Fact]
    public void SharedReferencesHeavyFactory_BuildsCitationBibliographyAndToaContracts()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildReferencesHeavyFieldDocument();

        document.BibliographyStyle.Should().Be(CitationStyle.Ieee);
        document.Sources.Should().HaveCount(3);
        document.Sources.Select(s => s.Type).Should().Contain([
            SourceType.Book,
            SourceType.JournalArticle,
            SourceType.WebSite]);
        document.Blocks.OfType<Paragraph>()
            .Should().Contain(p => Citations.IsBibliographyParagraph(p));
        document.Blocks.OfType<Paragraph>()
            .Should().Contain(p => TableOfAuthorities.IsTableOfAuthoritiesParagraph(p));
        document.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(r => r.Citation is not null)
            .Select(r => r.Citation!.Category)
            .Should().Contain([CitationCategory.Cases, CitationCategory.Statutes]);
        document.Blocks.OfType<Paragraph>()
            .Where(p => p.StyleId == TableOfAuthorities.EntryStyleId)
            .Select(p => p.PlainText)
            .Should().Contain([
                "Example v. FreeW, 123 F.4th 456 (2026)\t1, 2",
                "Free Software Evidence Act, 42 U.S.C. 2026\t1"
            ]);

        var fields = FreeWVisualEvidencePlanner.BuildFieldExpectation(document);
        fields.SimpleFieldCount.Should().Be(0);
        fields.ComplexFieldCount.Should().Be(7);
        fields.BodyFieldCount.Should().Be(7);
        fields.HeaderFooterFieldCount.Should().Be(0);
        fields.HasComplexFields.Should().BeTrue();
        fields.HasComplexResultFields.Should().BeTrue();
        fields.ComplexFieldKeywords.Should().BeEquivalentTo(["BIBLIOGRAPHY", "CITATION", "TOA"]);
        fields.FieldKinds.Should().Contain(["Complex:BIBLIOGRAPHY", "Complex:CITATION", "Complex:TOA"]);
        var toa = FreeWVisualEvidencePlanner.BuildTableOfAuthoritiesExpectation(document);
        toa.EntryCount.Should().Be(2);
        toa.EntryWithPageReferenceCount.Should().Be(2);
        toa.HasGeneratedTable.Should().BeTrue();
        toa.HasPageReferences.Should().BeTrue();
        toa.HasExplicitPageNumbers.Should().BeTrue();
        var caseToa = toa.PageReferences.Should().ContainSingle(reference =>
            reference.Category == "Cases"
            && reference.EntryText == "Example v. FreeW, 123 F.4th 456 (2026)"
            && reference.PageReferenceText == "1, 2").Subject;
        caseToa.PageNumbers.Should().Equal(1, 2);
        var statuteToa = toa.PageReferences.Should().ContainSingle(reference =>
            reference.Category == "Statutes"
            && reference.EntryText == "Free Software Evidence Act, 42 U.S.C. 2026"
            && reference.PageReferenceText == "1").Subject;
        statuteToa.PageNumbers.Should().Equal(1);

        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "references-heavy-fields",
            document.Page,
            pageNumber: 2,
            pageCount: 2,
            outputName: "references-heavy-fields_p2.png",
            document: document);
        expectation.ExpectedOutputName.Should().Be("references-heavy-fields_p2.png");
        expectation.Fields.ComplexFieldKeywords.Should().Contain(["CITATION", "BIBLIOGRAPHY", "TOA"]);
        expectation.TableOfAuthorities.PageReferences.Select(reference => reference.PageReferenceText)
            .Should().BeEquivalentTo(["1, 2", "1"]);
    }

    [Fact]
    public void SharedSectionGeometryFactory_BuildsMixedPortraitLandscapeContract()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildSectionGeometryDocument();

        document.Sections.Should().HaveCount(2);
        document.Sections[0].Page.Landscape.Should().BeFalse();
        document.Sections[0].Page.WidthPt.Should().Be(612);
        document.Sections[0].Page.HeightPt.Should().Be(792);
        document.Sections[1].Page.Should().BeSameAs(document.Page);
        document.Sections[1].Page.Landscape.Should().BeTrue();
        document.Sections[1].Page.WidthPt.Should().Be(792);
        document.Sections[1].Page.HeightPt.Should().Be(612);

        var pages = FreeWVisualEvidencePlanner.BuildSectionGeometryPagePlans(document, pageCount: 2);

        pages.Should().HaveCount(2);
        pages[0].PageNumber.Should().Be(1);
        pages[0].SectionOrdinal.Should().Be(1);
        pages[0].SectionOwnerId.Should().Be("section-1");
        pages[0].Orientation.Should().Be("portrait");
        pages[0].Page.Should().BeSameAs(document.Sections[0].Page);
        pages[1].PageNumber.Should().Be(2);
        pages[1].SectionOrdinal.Should().Be(2);
        pages[1].SectionOwnerId.Should().Be("section-2");
        pages[1].Orientation.Should().Be("landscape");
        pages[1].Page.Should().BeSameAs(document.Page);

        var portraitExpectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "f2-section-landscape",
            pages[0].Page,
            pages[0].PageNumber,
            pages[0].PageCount,
            "f2-section-landscape_p1.png",
            sectionOrdinal: pages[0].SectionOrdinal,
            sectionRelativePageNumber: pages[0].SectionRelativePageNumber,
            sectionOwnerId: pages[0].SectionOwnerId,
            document: document);
        var landscapeExpectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "f2-section-landscape",
            pages[1].Page,
            pages[1].PageNumber,
            pages[1].PageCount,
            "f2-section-landscape_p2.png",
            sectionOrdinal: pages[1].SectionOrdinal,
            sectionRelativePageNumber: pages[1].SectionRelativePageNumber,
            sectionOwnerId: pages[1].SectionOwnerId,
            document: document);

        portraitExpectation.Composition.ExpectsSectionGeometryChange.Should().BeTrue();
        portraitExpectation.Geometry.PageWidthDip.Should().BeApproximately(816, 0.01);
        portraitExpectation.Geometry.PageHeightDip.Should().BeApproximately(1056, 0.01);
        portraitExpectation.Features.Section.SectionOrdinal.Should().Be(1);
        landscapeExpectation.Geometry.PageWidthDip.Should().BeApproximately(1056, 0.01);
        landscapeExpectation.Geometry.PageHeightDip.Should().BeApproximately(816, 0.01);
        landscapeExpectation.Features.Section.SectionOrdinal.Should().Be(2);
        landscapeExpectation.Features.Section.OwnerId.Should().Be("section-2");
    }

    [Fact]
    public void SharedSectionGeometrySurfacePlans_BuildPageSizedSectionSlices()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildSectionGeometryDocument();

        var surfacePlans = FreeWVisualEvidencePlanner.BuildSectionGeometrySurfacePlans(document, pageCount: 2);

        surfacePlans.Should().HaveCount(2);
        surfacePlans[0].RenderStatus.Should().Be(FreeWVisualEvidencePlanner.SectionGeometryPageSurfaceRenderStatus);
        surfacePlans[0].Orientation.Should().Be("portrait");
        surfacePlans[0].SourceBlockIndexes.Should().Equal(0, 1, 2, 3, 4, 5, 6);
        surfacePlans[0].Document.Page.WidthPt.Should().Be(612);
        surfacePlans[0].Document.Page.HeightPt.Should().Be(792);
        surfacePlans[0].Document.Page.Landscape.Should().BeFalse();
        surfacePlans[0].CaptureWidthDip.Should().BeApproximately(864, 0.01);
        surfacePlans[0].CaptureHeightDip.Should().BeApproximately(1104, 0.01);

        surfacePlans[1].Orientation.Should().Be("landscape");
        surfacePlans[1].SourceBlockIndexes.Should().Equal(7, 8, 9, 10, 11, 12);
        surfacePlans[1].Document.Page.WidthPt.Should().Be(792);
        surfacePlans[1].Document.Page.HeightPt.Should().Be(612);
        surfacePlans[1].Document.Page.Landscape.Should().BeTrue();
        surfacePlans[1].CaptureWidthDip.Should().BeApproximately(1104, 0.01);
        surfacePlans[1].CaptureHeightDip.Should().BeApproximately(864, 0.01);
        surfacePlans[1].Document.Blocks.OfType<Paragraph>().First().PlainText.Should().Contain("Section 2");

        foreach (var plan in surfacePlans)
        {
            plan.Document.Blocks
                .OfType<Paragraph>()
                .Should()
                .OnlyContain(paragraph => paragraph.SectionBreak == null);
        }
    }

    [Fact]
    public void DefaultExpectedScenarios_RequiresPairedBackstageRendererEvidence()
    {
        var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;

        foreach (var scenarioId in FreeWVisualEvidenceManifestNormalizer.BackstageRendererScenarioIds)
        {
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == 2);
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == 2);
        }
    }

    [Fact]
    public void DefaultExpectedScenarios_RequiresPairedNoteRendererEvidence()
    {
        var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;

        foreach (var scenarioId in FreeWVisualEvidenceManifestNormalizer.NoteRendererScenarioIds)
        {
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
        }
    }

    [Fact]
    public void DefaultExpectedScenarios_RequiresPairedSectionGeometryEvidence()
    {
        var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;

        foreach (var scenarioId in FreeWVisualEvidenceManifestNormalizer.SectionGeometryRendererScenarioIds)
        {
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
        }
    }

    [Fact]
    public void DefaultExpectedScenarios_RequiresPairedReviewRendererEvidence()
    {
        var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;

        foreach (var scenarioId in FreeWVisualEvidenceManifestNormalizer.ReviewRendererScenarioIds)
        {
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
        }
    }

    [Fact]
    public void DefaultExpectedScenarios_RequiresPairedFieldRendererEvidence()
    {
        var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;

        foreach (var scenarioId in FreeWVisualEvidenceManifestNormalizer.FieldRendererScenarioIds)
        {
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
        }
    }

    [Fact]
    public void DefaultExpectedScenarios_RequiresPairedTableRendererEvidence()
    {
        var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;

        foreach (var scenarioId in FreeWVisualEvidenceManifestNormalizer.TableRendererScenarioIds)
        {
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
        }
    }

    [Fact]
    public void DefaultExpectedScenarios_RequiresPairedDrawingObjectRendererEvidence()
    {
        var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;

        foreach (var scenarioId in FreeWVisualEvidenceManifestNormalizer.DrawingObjectRendererScenarioIds)
        {
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == 1);
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == 1);
        }
    }

    [Fact]
    public void DefaultExpectedScenarios_RequiresPairedChartSmartArtRendererEvidence()
    {
        var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;

        foreach (var scenarioId in FreeWVisualEvidenceManifestNormalizer.ChartSmartArtRendererScenarioIds)
        {
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == 1);
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == 1);
        }
    }

    [Fact]
    public void DefaultExpectedScenarios_RequiresPairedWordArtWatermarkRendererEvidence()
    {
        var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;

        foreach (var scenarioId in FreeWVisualEvidenceManifestNormalizer.WordArtWatermarkRendererScenarioIds)
        {
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == 1);
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == 1);
        }
    }

    [Fact]
    public void WordBaselineGenerationPlan_CoversBoundedGeneratedCorpus()
    {
        var root = CreateTempRoot();
        try
        {
            var plan = FreeWWordBaselineEvidencePlanner.BuildGenerationPlan(
                Path.Combine(root, "fixtures"),
                Path.Combine(root, "word-baseline"));

            plan.WordApplicationProgId.Should().Be("Word.Application");
            plan.MaxPagesPerDocument.Should().Be(3);
            plan.ExpectedFixtureCount.Should().Be(21);
            plan.ExpectedBaselinePngCount.Should().Be(63);
            plan.Fixtures.Select(f => f.DocumentName).Should().Contain([
                "f2-hf-basic.docx",
                "field-page-number-variants.docx",
                "references-heavy-fields.docx",
                "table-layout-complex.docx",
                "table-pagination-repeat-header.docx",
                "drawing-objects-complex.docx",
                "object-format-position-size-style.docx",
                "chart-smartart-complex.docx",
                "wordart-watermark-stress.docx",
                "wordart-picture-watermark-layout.docx",
                "backstage-print-preview-fidelity.docx",
                "backstage-pdf-export-fidelity.docx"]);
            plan.Fixtures.Single(f => f.ScenarioId == "backstage-print-preview-fidelity")
                .ExpectedBaselinePaths.Should().Contain("backstage-print-preview-fidelity/backstage-print-preview_p1.png");
            plan.Fixtures.Single(f => f.ScenarioId == "backstage-pdf-export-fidelity")
                .ExpectedBaselinePaths.Should().Contain("backstage-pdf-export-fidelity/backstage-pdf-export_p1.png");
            plan.Fixtures.Single(f => f.ScenarioId == "table-pagination-repeat-header")
                .ExpectedBaselinePaths.Should().Contain("table-pagination-repeat-header/table-pagination-repeat-header_p2.png");
            plan.Fixtures.Single(f => f.ScenarioId == "field-page-number-variants")
                .ExpectedBaselinePaths.Should().Contain("field-page-number-variants/field-page-number-variants_p1.png");
            plan.Fixtures.Single(f => f.ScenarioId == "references-heavy-fields")
                .ExpectedBaselinePaths.Should().Contain("references-heavy-fields/references-heavy-fields_p2.png");
            plan.Fixtures.Single(f => f.ScenarioId == "object-format-position-size-style")
                .ExpectedBaselinePaths.Should().Contain("object-format-position-size-style/object-format-position-size-style_p1.png");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WordBaselineScope_LimitsComparisonsToGeneratedCorpusScenarios()
    {
        var root = CreateTempRoot();
        try
        {
            var generatedCorpusRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "f2-hf-basic",
                pageNumber: 1,
                pageCount: 1);
            var repeatHeaderRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                "table-pagination-repeat-header",
                pageNumber: 2,
                pageCount: 2);
            var avaloniaOnlyRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                "page-composition-print-layout",
                pageNumber: 1,
                pageCount: 1);
            var manifestDir = Path.Combine(root, "manifest");
            FreeWVisualEvidencePlanner.WriteManifest(
                manifestDir,
                [generatedCorpusRow, repeatHeaderRow, avaloniaOnlyRow],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(manifestDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-hf-basic",
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "table-pagination-repeat-header",
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "page-composition-print-layout",
                        1)
                ]);

            var rows = summary.Evidence;
            FreeWWordBaselineEvidencePlanner.ShouldCompareToWordBaseline(
                rows.Single(r => r.ScenarioId == "f2-hf-basic"),
                FreeWWordBaselineEvidencePlanner.BaselineScopeGeneratedCorpus).Should().BeTrue();
            FreeWWordBaselineEvidencePlanner.ShouldCompareToWordBaseline(
                rows.Single(r => r.ScenarioId == "table-pagination-repeat-header"),
                FreeWWordBaselineEvidencePlanner.BaselineScopeGeneratedCorpus).Should().BeTrue();
            FreeWWordBaselineEvidencePlanner.ShouldCompareToWordBaseline(
                rows.Single(r => r.ScenarioId == "page-composition-print-layout"),
                FreeWWordBaselineEvidencePlanner.BaselineScopeGeneratedCorpus).Should().BeFalse();
            rows.Should().OnlyContain(row =>
                FreeWWordBaselineEvidencePlanner.ShouldCompareToWordBaseline(row, FreeWWordBaselineEvidencePlanner.BaselineScopeAll));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WordBaselineRunnerScript_GuardsWordComAndAllowsNoWordSummaryPath()
    {
        var scriptPath = FindRepoFile("tools", "Run-FreeWWordBaselineEvidence.ps1");
        var source = File.ReadAllText(scriptPath);

        source.Should().Contain("Test-ComProgIdAvailable");
        source.Should().Contain("[type]::GetTypeFromProgID($ProgId, $false)");
        source.Should().Contain("-AllowMissingWord");
        source.Should().Contain("--word-baseline-scope");
        source.Should().Contain("generated-corpus");
        source.Should().Contain("--word-baseline-unavailable-reason");
        source.Should().Contain("_word_baseline_unavailable.json");
        source.Should().Contain("status = \"word-baseline-unavailable\"");
        source.Should().Contain("evidenceMode = \"no-word-fallback\"");
        source.Should().Contain("baselineEvidenceClass = \"word-baseline-unavailable\"");
        source.Should().Contain("authoritativeWordPngParity = $false");
        source.Should().Contain("summaryRowStatus = \"word-baseline-unavailable\"");
        source.Should().Contain("passed = $true");
        source.Should().Contain("function Write-WordBaselineUnavailableSummary");
        source.Should().Contain("MS Word baseline PNG generation failed");
        source.Should().Contain("Word baseline mode: no-word-fallback");
        source.Should().Contain("Word baseline mode: real-word-png-comparison");
        source.Should().Contain("FreeW.VisualEvidenceSummary.csproj");
    }

    [Fact]
    public void BuildPageExpectation_UsesSharedGeometryAndExpectedOutputName()
    {
        var page = new PageSettings
        {
            WidthPt = 612,
            HeightPt = 792,
            MarginLeftPt = 72,
            MarginRightPt = 72,
            MarginTopPt = 72,
            MarginBottomPt = 72,
            ColumnCount = 2,
            ColumnSpacingPt = 36,
            ColumnsLineBetween = true,
            PageBorder = new PageBorder("#000080", 3),
            WatermarkOptions = new WatermarkOptions("DRAFT")
            {
                FontColorHex = "#808080",
                Opacity = 0.4,
                Layout = WatermarkLayout.Diagonal
            }
        };

        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "f2-hf-basic",
            page,
            pageNumber: 2,
            pageCount: 3,
            outputName: "actual.png",
            headerSlotName: "header",
            footerSlotName: "footer",
            sectionOrdinal: 2,
            sectionRelativePageNumber: 1);

        expectation.ExpectedOutputName.Should().Be("f2-hf-basic_p2.png");
        expectation.LayoutKind.Should().Be(nameof(DocumentViewLayoutKind.PrintLayout));
        expectation.HeaderSlotName.Should().Be("header");
        expectation.FooterSlotName.Should().Be("footer");
        expectation.Composition.ExpectsHeadersFooters.Should().BeTrue();
        expectation.Geometry.PageWidthDip.Should().BeApproximately(816, 0.01);
        expectation.Geometry.ContentWidthDip.Should().BeApproximately(624, 0.01);
        expectation.Geometry.TextAreaHeightDip.Should().BeApproximately(864, 0.01);
        expectation.Features.Section.OwnerId.Should().Be("section-2");
        expectation.Features.Section.SectionOrdinal.Should().Be(2);
        expectation.Features.Section.SectionRelativePageNumber.Should().Be(1);
        expectation.Features.Columns.Count.Should().Be(2);
        expectation.Features.Columns.GapDip.Should().BeApproximately(48, 0.01);
        expectation.Features.Columns.LineBetween.Should().BeTrue();
        expectation.Features.PageBorder.Present.Should().BeTrue();
        expectation.Features.PageBorder.ColorHex.Should().Be("#000080");
        expectation.Features.PageBorder.WidthDip.Should().BeApproximately(4, 0.01);
        expectation.Features.Watermark.Present.Should().BeTrue();
        expectation.Features.Watermark.Text.Should().Be("DRAFT");
        expectation.Features.Watermark.Layout.Should().Be(nameof(WatermarkLayout.Diagonal));
    }

    [Fact]
    public void BuildPageExpectation_RecordsSharedFieldPageNumberVariants()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument();
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "field-page-number-variants",
            document.Page,
            pageNumber: 2,
            pageCount: 3,
            outputName: "field-page-number-variants_p2.png",
            headerSlotName: "even-header",
            footerSlotName: "even-footer",
            document: document);

        expectation.ExpectedOutputName.Should().Be("field-page-number-variants_p2.png");
        expectation.Composition.ExpectsHeadersFooters.Should().BeTrue();
        expectation.HeaderSlotName.Should().Be("even-header");
        expectation.FooterSlotName.Should().Be("even-footer");
        expectation.Fields.SimpleFieldCount.Should().Be(14);
        expectation.Fields.ComplexFieldCount.Should().Be(4);
        expectation.Fields.HasPageFields.Should().BeTrue();
        expectation.Fields.HasNumPagesFields.Should().BeTrue();
        expectation.Fields.HasDocumentPropertyFields.Should().BeTrue();
        expectation.Fields.HasComplexFields.Should().BeTrue();
        expectation.Fields.HasHeaderFooterFields.Should().BeTrue();
        expectation.Fields.ComplexFieldKeywords.Should().Contain(["PAGE", "NUMPAGES", "TITLE", "AUTHOR"]);
    }

    [Fact]
    public void BuildPageExpectation_RecordsSharedComplexTableLayout()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument();
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "table-layout-complex",
            document.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "table-layout-complex_p1.png",
            document: document);

        expectation.Composition.ExpectsTables.Should().BeTrue();
        expectation.Tables.TableCount.Should().Be(1);
        expectation.Tables.TotalRows.Should().Be(5);
        expectation.Tables.MaxGridColumnCount.Should().Be(4);
        expectation.Tables.EstimatedPageCount.Should().Be(1);
        expectation.Tables.HasPaginationPlan.Should().BeTrue();
        expectation.Tables.HasHeaderRow.Should().BeTrue();
        expectation.Tables.RepeatsHeaderRow.Should().BeTrue();
        expectation.Tables.HasBandedRows.Should().BeTrue();
        expectation.Tables.HasMergedCells.Should().BeTrue();
        expectation.Tables.HasVerticalMerges.Should().BeTrue();
        expectation.Tables.HasCellShading.Should().BeTrue();
        expectation.Tables.HasCustomCellBorders.Should().BeTrue();
        expectation.Tables.HasCellMargins.Should().BeTrue();
        expectation.Tables.HasCellSpacing.Should().BeTrue();
        expectation.Tables.HasVerticalText.Should().BeTrue();
        expectation.Tables.HasVerticalAlignment.Should().BeTrue();
        expectation.Tables.HasPreferredWidths.Should().BeTrue();
        expectation.Tables.HasNamedStyle.Should().BeTrue();
        expectation.Tables.Tables.Single().TableStyleId.Should().Be("GridTable4");
        expectation.Tables.Tables.Single().ColumnWidthsDip.Should().HaveCount(4);
        expectation.Tables.Tables.Single().Cells.Should().Contain(cell =>
            cell.GridSpan == 2 && cell.RowSpan == 1);
        expectation.Tables.Tables.Single().Cells.Should().Contain(cell =>
            cell.RowSpan == 2 && cell.IsVerticalMergeContinuation == false);
    }

    [Fact]
    public void BuildPageExpectation_RecordsSharedTablePaginationPlan()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "table-pagination-repeat-header",
            document.Page,
            pageNumber: 2,
            pageCount: 2,
            outputName: "table-pagination-repeat-header_p2.png",
            document: document);

        expectation.Composition.ExpectsTables.Should().BeTrue();
        expectation.Tables.TableCount.Should().Be(1);
        expectation.Tables.EstimatedPageCount.Should().Be(2);
        expectation.Tables.HasPaginationPlan.Should().BeTrue();
        expectation.Tables.HasMultiPageTables.Should().BeTrue();
        expectation.Tables.HasRepeatedHeaderPages.Should().BeTrue();
        expectation.Tables.HasKeepTogetherRows.Should().BeTrue();
        var page2 = expectation.Tables.PaginationPlans.Single().Pages[1];
        page2.RepeatedHeaderRowIndexes.Should().Equal(0);
        page2.RenderRows[0].Should().Match<DocumentTablePaginationRenderRowPlan>(row =>
            row.SourceRowIndex == 0
            && row.IsRepeatedHeader
            && row.StartsPlannedPage
            && row.PageNumber == 2);
    }

    [Fact]
    public void BuildPageExpectation_RecordsSharedDrawingObjectLayout()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildDrawingObjectsCompositionDocument();
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "drawing-objects-complex",
            document.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "drawing-objects-complex_p1.png",
            availableWidthDip: 960,
            document: document);

        expectation.Composition.ExpectsFloatingObjects.Should().BeTrue();
        expectation.DrawingObjects.FloatingObjectCount.Should().Be(6);
        expectation.DrawingObjects.BehindTextCount.Should().Be(1);
        expectation.DrawingObjects.InFrontCount.Should().Be(5);
        expectation.DrawingObjects.HasImages.Should().BeTrue();
        expectation.DrawingObjects.HasShapes.Should().BeTrue();
        expectation.DrawingObjects.HasCharts.Should().BeTrue();
        expectation.DrawingObjects.HasSmartArt.Should().BeTrue();
        expectation.DrawingObjects.HasWordArt.Should().BeTrue();
        expectation.DrawingObjects.HasGroups.Should().BeTrue();
        expectation.DrawingObjects.HasSquareWrap.Should().BeTrue();
        expectation.DrawingObjects.HasTopAndBottomWrap.Should().BeTrue();
        expectation.DrawingObjects.HasZOrder.Should().BeTrue();
        expectation.DrawingObjects.Objects.Select(o => o.TypeTag).Should().Contain([
            "Image",
            "Shape",
            "Chart",
            "SmartArt",
            "WordArt",
            "Group"]);
        expectation.DrawingObjects.Effects.EffectObjectCount.Should().Be(3);
        expectation.DrawingObjects.Effects.ShapeEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.ImageEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.WordArtEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.RenderedGroupChildEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.RenderedGroupChildShapeEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.RenderedGroupChildWordArtEffectObjectCount.Should().Be(0);
        expectation.DrawingObjects.Effects.PlannedGroupChildEffectObjectCount.Should().Be(0);
        expectation.DrawingObjects.Effects.PlannedGroupChildShapeEffectObjectCount.Should().Be(0);
        expectation.DrawingObjects.Effects.PlannedGroupChildWordArtEffectObjectCount.Should().Be(0);
        expectation.DrawingObjects.Effects.HasShadow.Should().BeTrue();
        expectation.DrawingObjects.Effects.HasGlow.Should().BeTrue();
        expectation.DrawingObjects.Effects.HasReflection.Should().BeTrue();
        expectation.DrawingObjects.Effects.HasArtisticEffect.Should().BeTrue();
        expectation.DrawingObjects.Effects.EffectSummaries.Should().Contain([
            "Shape:shadow",
            "Image:shadow+glow+reflection+artistic:GlowDiffused",
            "WordArt:glow"]);
        expectation.DrawingObjects.Effects.RenderedGroupChildEffectSummaries.Should().Contain(
            "GroupChild0:Shape:glow");
        expectation.DrawingObjects.Effects.PlannedGroupChildEffectSummaries.Should().BeEmpty();
    }

    [Fact]
    public void BuildPageExpectation_RecordsSharedObjectFormatPositionSizeStyle()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildObjectFormatPositionSizeStyleDocument();
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "object-format-position-size-style",
            document.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "object-format-position-size-style_p1.png",
            availableWidthDip: 960,
            document: document);

        expectation.Composition.ExpectsFloatingObjects.Should().BeTrue();
        expectation.DrawingObjects.FloatingObjectCount.Should().Be(3);
        expectation.DrawingObjects.BehindTextCount.Should().Be(1);
        expectation.DrawingObjects.InFrontCount.Should().Be(2);
        expectation.DrawingObjects.HasImages.Should().BeTrue();
        expectation.DrawingObjects.HasShapes.Should().BeTrue();
        expectation.DrawingObjects.HasWordArt.Should().BeTrue();
        expectation.DrawingObjects.HasSquareWrap.Should().BeTrue();
        expectation.DrawingObjects.HasTopAndBottomWrap.Should().BeTrue();
        expectation.DrawingObjects.HasZOrder.Should().BeTrue();
        expectation.DrawingObjects.AltTextObjectCount.Should().Be(3);
        expectation.DrawingObjects.AltTextSummaries.Should().Contain([
            "Image:Square wrapped sample picture with glow reflection soft edge and artistic effect",
            "Shape:Behind text callout with shadow and bevel",
            "WordArt:Top and bottom wrapped WordArt format label"]);

        var image = expectation.DrawingObjects.Objects.Single(o => o.Kind == DocumentFloatingObjectKind.Image);
        image.Wrapping.Should().Be(ImageWrapping.Square);
        image.ZOrderIndex.Should().Be(5);
        image.Rect.WidthDip.Should().BeApproximately(176, 0.001);
        image.Rect.HeightDip.Should().BeApproximately(112, 0.001);

        var shape = expectation.DrawingObjects.Objects.Single(o => o.Kind == DocumentFloatingObjectKind.Shape);
        shape.BehindText.Should().BeTrue();
        shape.ZOrderIndex.Should().Be(1);

        var wordArt = expectation.DrawingObjects.Objects.Single(o => o.Kind == DocumentFloatingObjectKind.WordArt);
        wordArt.Wrapping.Should().Be(ImageWrapping.TopAndBottom);
        wordArt.ZOrderIndex.Should().Be(9);

        expectation.DrawingObjects.Effects.ShapeEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.ImageEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.WordArtEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.HasShadow.Should().BeTrue();
        expectation.DrawingObjects.Effects.HasGlow.Should().BeTrue();
        expectation.DrawingObjects.Effects.HasReflection.Should().BeTrue();
        expectation.DrawingObjects.Effects.HasSoftEdge.Should().BeTrue();
        expectation.DrawingObjects.Effects.HasBevel.Should().BeTrue();
        expectation.DrawingObjects.Effects.HasArtisticEffect.Should().BeTrue();
        expectation.DrawingObjects.Effects.EffectSummaries.Should().Contain([
            "Shape:shadow+bevel",
            "Image:shadow+glow+reflection+soft-edge+bevel+artistic:GlowDiffused",
            "WordArt:glow"]);
    }

    [Fact]
    public void BuildPageExpectation_RecordsSharedChartSmartArtPlans()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildChartSmartArtCompositionDocument();
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "chart-smartart-complex",
            document.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "chart-smartart-complex_p1.png",
            document: document);

        expectation.ChartSmartArt.ChartCount.Should().Be(2);
        expectation.ChartSmartArt.SmartArtCount.Should().Be(1);
        expectation.ChartSmartArt.HasChartPalette.Should().BeTrue();
        expectation.ChartSmartArt.HasChartQuickLayout.Should().BeTrue();
        expectation.ChartSmartArt.HasMarkerOnlyScatter.Should().BeTrue();
        expectation.ChartSmartArt.HasLegend.Should().BeTrue();
        expectation.ChartSmartArt.HasGridlines.Should().BeTrue();
        expectation.ChartSmartArt.HasDataLabels.Should().BeTrue();
        expectation.ChartSmartArt.HasAxisTitles.Should().BeTrue();
        expectation.ChartSmartArt.HasPlotAreaFill.Should().BeTrue();
        expectation.ChartSmartArt.HasSmartArtLayout.Should().BeTrue();
        expectation.ChartSmartArt.HasSmartArtColorScheme.Should().BeTrue();
        expectation.ChartSmartArt.HasSmartArtStyle.Should().BeTrue();
        expectation.ChartSmartArt.SmartArtNodeCount.Should().Be(3);
        expectation.ChartSmartArt.DistinctSmartArtFillCount.Should().BeGreaterThan(1);
        expectation.ChartSmartArt.Charts.Should().Contain(plan =>
            plan.Kind == ChartKind.Scatter &&
            plan.GeometryKind == ChartVisualGeometryKind.MarkerOnly);
        expectation.ChartSmartArt.SmartArts.Single().LayoutId.Should().Be("stepup1");
        expectation.ChartSmartArt.SmartArts.Single().Nodes.Select(node => node.FillHex)
            .Should().ContainInOrder("#38517D", "#486DAF", "#679AD6");
    }

    [Fact]
    public void BuildPageExpectation_RecordsSharedWordArtWatermarkStress()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildWordArtWatermarkStressDocument();
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "wordart-watermark-stress",
            document.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "wordart-watermark-stress_p1.png",
            availableWidthDip: 960,
            document: document);

        expectation.Composition.ExpectsFloatingObjects.Should().BeTrue();
        expectation.Composition.ExpectsWatermark.Should().BeTrue();
        expectation.Composition.ExpectsPageBorder.Should().BeTrue();
        expectation.Features.PageBorder.Present.Should().BeTrue();
        expectation.Features.PageBorder.ColorHex.Should().Be("#1F4E79");
        expectation.Features.Watermark.Present.Should().BeTrue();
        expectation.Features.Watermark.Text.Should().Be("CONFIDENTIAL");
        expectation.DrawingObjects.FloatingObjectCount.Should().Be(3);
        expectation.DrawingObjects.HasShapes.Should().BeTrue();
        expectation.DrawingObjects.HasWordArt.Should().BeTrue();
        expectation.DrawingObjects.HasSquareWrap.Should().BeTrue();
        expectation.DrawingObjects.HasZOrder.Should().BeTrue();
        expectation.DrawingObjects.Objects.Count(o => o.TypeTag == "WordArt").Should().Be(2);
        expectation.DrawingObjects.Effects.ShapeEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.WordArtEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.HasShadow.Should().BeTrue();
        expectation.DrawingObjects.Effects.HasGlow.Should().BeTrue();
        expectation.DrawingObjects.Effects.EffectSummaries.Should().Contain(["Shape:shadow", "WordArt:glow"]);
    }

    [Fact]
    public void BuildPageExpectation_RecordsSharedWordArtPictureWatermarkLayout()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildWordArtPictureWatermarkLayoutDocument();
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "wordart-picture-watermark-layout",
            document.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "wordart-picture-watermark-layout_p1.png",
            availableWidthDip: 960,
            document: document);

        expectation.Composition.ExpectsColumns.Should().BeTrue();
        expectation.Composition.ExpectsPageBorder.Should().BeTrue();
        expectation.Composition.ExpectsWatermark.Should().BeTrue();
        expectation.Composition.ExpectsFloatingObjects.Should().BeTrue();
        expectation.Features.Columns.Count.Should().Be(2);
        expectation.Features.Columns.LineBetween.Should().BeTrue();
        expectation.Features.PageBorder.Present.Should().BeTrue();
        expectation.Features.PageBorder.ColorHex.Should().Be("#1F4E79");
        expectation.Features.Watermark.Present.Should().BeTrue();
        expectation.Features.Watermark.IsPicture.Should().BeTrue();
        expectation.Features.Watermark.Layout.Should().Be(nameof(WatermarkLayout.Horizontal));
        expectation.Features.Watermark.Opacity.Should().BeApproximately(0.38, 0.001);
        expectation.DrawingObjects.FloatingObjectCount.Should().Be(1);
        expectation.DrawingObjects.InFrontCount.Should().Be(1);
        expectation.DrawingObjects.HasWordArt.Should().BeTrue();
        expectation.DrawingObjects.Objects.Single().Kind.Should().Be(DocumentFloatingObjectKind.WordArt);
    }

    [Fact]
    public void PictureWatermarkLayoutPlanner_CentersScalesAndClampsOpacity()
    {
        var options = new WatermarkOptions(string.Empty)
        {
            ImageBytes = [1, 2, 3],
            ScalePct = 40,
            Layout = WatermarkLayout.Diagonal,
            Opacity = 1.25
        };

        var plan = WatermarkVisualPlanner.BuildPictureLayout(
            options,
            pageWidthDip: 1000,
            pageHeightDip: 800,
            sourceWidthDip: 200,
            sourceHeightDip: 100);

        plan.Should().NotBeNull();
        plan!.WidthDip.Should().BeApproximately(400, 0.001);
        plan.HeightDip.Should().BeApproximately(200, 0.001);
        plan.XDip.Should().BeApproximately(300, 0.001);
        plan.YDip.Should().BeApproximately(300, 0.001);
        plan.Opacity.Should().Be(1);
        plan.RotationDegrees.Should().Be(-45);
    }

    [Fact]
    public void ComputePixelStats_AndTrustGuard_RejectBlankAllBackgroundCapture()
    {
        var blank = new byte[20 * 20 * 4];
        for (var i = 0; i < blank.Length; i += 4)
        {
            blank[i + 0] = 255;
            blank[i + 1] = 255;
            blank[i + 2] = 255;
            blank[i + 3] = 255;
        }

        var stats = FreeWVisualEvidencePlanner.ComputePixelStats(
            blank,
            width: 20,
            height: 20,
            stride: 20 * 4,
            FreeWVisualEvidencePixelFormat.Bgra32);
        var row = BuildRow(stats, byteLength: 1_024);

        row.Trust.Passed.Should().BeFalse();
        row.Trust.Failures.Should().Contain(f => f.Contains("distinct sampled colors", StringComparison.Ordinal));
        row.Trust.Failures.Should().Contain(f => f.Contains("non-background pixel ratio", StringComparison.Ordinal));
        row.Trust.Failures.Should().Contain(f => f.Contains("dominant color ratio", StringComparison.Ordinal));
        Action act = () => FreeWVisualEvidencePlanner.EnsureTrusted(row);
        act.Should().Throw<InvalidOperationException>().WithMessage("*blank.png*failed trust checks*");
    }

    [Fact]
    public void BuildManifest_SerializesStableSchemaAndTrustedRows()
    {
        var pixels = new byte[20 * 20 * 4];
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                var offset = (y * 20 + x) * 4;
                if (x is >= 2 and <= 17 && y is >= 8 and <= 12)
                {
                    pixels[offset + 0] = (byte)(x % 3 == 0 ? 32 : 0);
                    pixels[offset + 1] = (byte)(y % 2 == 0 ? 32 : 0);
                    pixels[offset + 2] = (byte)(x % 5 == 0 ? 160 : 0);
                }
                else
                {
                    pixels[offset + 0] = 255;
                    pixels[offset + 1] = 255;
                    pixels[offset + 2] = 255;
                }
                pixels[offset + 3] = 255;
            }
        }

        var stats = FreeWVisualEvidencePlanner.ComputePixelStats(
            pixels,
            width: 20,
            height: 20,
            stride: 20 * 4,
            FreeWVisualEvidencePixelFormat.Bgra32);
        var row = BuildRow(stats, byteLength: 2_048, outputName: "f2-hf-basic_p1.png");

        row.Trust.Passed.Should().BeTrue();

        var manifest = FreeWVisualEvidencePlanner.BuildManifest(
            [row],
            new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        var json = FreeWVisualEvidencePlanner.ToJson(manifest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("schemaId").GetString().Should().Be("freew.visual-evidence.v1");
        root.GetProperty("schemaVersion").GetInt32().Should().Be(FreeWVisualEvidencePlanner.SchemaVersion);
        root.GetProperty("product").GetString().Should().Be("FreeW");
        root.GetProperty("scenarios").GetArrayLength().Should().Be(1);
        var evidence = root.GetProperty("evidence")[0];
        evidence.GetProperty("scenarioId").GetString().Should().Be("f2-hf-basic");
        evidence.GetProperty("trust").GetProperty("passed").GetBoolean().Should().BeTrue();
        evidence.GetProperty("pageExpectation").GetProperty("features").GetProperty("section").GetProperty("ownerId")
            .GetString().Should().Be("section-1");
        evidence.GetProperty("pageExpectation").GetProperty("drawingObjects").GetProperty("floatingObjectCount")
            .GetInt32().Should().Be(0);
        evidence.GetProperty("pageExpectation").GetProperty("fields").GetProperty("simpleFieldCount")
            .GetInt32().Should().Be(0);
        evidence.GetProperty("pageExpectation").GetProperty("tableOfAuthorities").GetProperty("entryCount")
            .GetInt32().Should().Be(0);
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RelativizesOutputsAndComputesHashes()
    {
        var root = CreateTempRoot();
        try
        {
            var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;
            var rows = expected
                .SelectMany(e => Enumerable.Range(1, e.MinimumExpectedOutputs)
                    .Select(page => BuildFileBackedRow(
                        root,
                        e.HostId,
                        e.ScenarioId,
                        page,
                        e.MinimumExpectedOutputs)))
                .ToList();
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                rows.Where(r => r.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId).ToList(),
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                rows.Where(r => r.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId).ToList(),
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root);

            summary.Trust.Passed.Should().BeTrue(string.Join(Environment.NewLine, summary.Trust.Failures));
            summary.Sources.Should().HaveCount(2);
            summary.Scenarios.Should().OnlyContain(s => s.Trust.Passed);
            summary.Evidence.Should().HaveCount(expected.Sum(e => e.MinimumExpectedOutputs));
            summary.Evidence.Should().OnlyContain(e => !Path.IsPathRooted(e.OutputPath));
            summary.Evidence.Should().OnlyContain(e => e.OutputPath.Contains('/', StringComparison.Ordinal));
            summary.Evidence.Should().OnlyContain(e => e.Sha256.Length == 64);

            var first = summary.Evidence.Single(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId
                && e.ScenarioId == "f2-hf-basic"
                && e.PageNumber == 1);
            first.OutputPath.Should().Be("wpf/f2-hf-basic_p1.png");
            first.ByteLength.Should().Be(2_048);
            first.Sha256.Should().Be(ComputeSha256(Path.Combine(root, first.OutputPath.Replace('/', Path.DirectorySeparatorChar))));

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(summary);
            json.Should().NotContain(Path.GetFileName(root));
            json.Should().Contain("f2-hf-basic");

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(summary);
            markdown.Should().Contain("Scenario Coverage");
            markdown.Should().Contain("avalonia-page-layout-shot");
            markdown.Should().Contain("1 rendered grouped child effect object(s): GroupChild0:Shape:glow");
            markdown.Should().NotContain("planned grouped child effect object(s)");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresMatchingRenderedGroupedChildEffectEvidence()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "drawing-objects-complex";
            var wpfRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);
            var avaloniaRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);
            var avaloniaWithDifferentGroupChildEffect = avaloniaRow with
            {
                PageExpectation = avaloniaRow.PageExpectation with
                {
                    DrawingObjects = avaloniaRow.PageExpectation.DrawingObjects with
                    {
                        Effects = avaloniaRow.PageExpectation.DrawingObjects.Effects with
                        {
                            RenderedGroupChildEffectSummaries = ["GroupChild0:Shape:shadow"]
                        }
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaWithDifferentGroupChildEffect],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        1)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("drawing-object renderer pair 'drawing-objects-complex' page 1", StringComparison.Ordinal)
                && f.Contains("rendered grouped child effect summaries differ", StringComparison.Ordinal)
                && f.Contains("WPF 'GroupChild0:Shape:glow'", StringComparison.Ordinal)
                && f.Contains("Avalonia 'GroupChild0:Shape:shadow'", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresMatchingObjectFormatAltTextEvidence()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "object-format-position-size-style";
            var wpfRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);
            var avaloniaRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);
            var avaloniaWithDifferentAltText = avaloniaRow with
            {
                PageExpectation = avaloniaRow.PageExpectation with
                {
                    DrawingObjects = avaloniaRow.PageExpectation.DrawingObjects with
                    {
                        AltTextSummaries = ["Image:stale object-format alt text"]
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaWithDifferentAltText],
                new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        1)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("drawing-object renderer pair 'object-format-position-size-style' page 1", StringComparison.Ordinal)
                && f.Contains("alt text summaries differ", StringComparison.Ordinal)
                && f.Contains("stale object-format alt text", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresMatchingChartSmartArtPlanEvidence()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "chart-smartart-complex";
            var wpfRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);
            var avaloniaRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);
            var avaloniaPlan = avaloniaRow.PageExpectation.ChartSmartArt;
            var alteredSmartArt = avaloniaPlan.SmartArts.Single() with
            {
                Nodes = avaloniaPlan.SmartArts.Single().Nodes
                    .Select((node, index) => index == 1 ? node with { FillHex = "#101010" } : node)
                    .ToList()
            };
            var avaloniaWithDifferentSmartArtPlan = avaloniaRow with
            {
                PageExpectation = avaloniaRow.PageExpectation with
                {
                    ChartSmartArt = avaloniaPlan with
                    {
                        SmartArts = [alteredSmartArt]
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaWithDifferentSmartArtPlan],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        1)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("chart/SmartArt renderer pair 'chart-smartart-complex' page 1", StringComparison.Ordinal)
                && f.Contains("SmartArt plan signatures differ", StringComparison.Ordinal)
                && f.Contains("#486DAF", StringComparison.Ordinal)
                && f.Contains("#101010", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresMatchingTablePlanEvidence()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "table-layout-complex";
            var wpfRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);
            var avaloniaRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);
            var avaloniaTablePlan = avaloniaRow.PageExpectation.Tables.Tables.Single();
            var avaloniaWithDifferentTablePlan = avaloniaRow with
            {
                PageExpectation = avaloniaRow.PageExpectation with
                {
                    Tables = avaloniaRow.PageExpectation.Tables with
                    {
                        Tables = [avaloniaTablePlan with { TableStyleId = "AlteredTableStyle" }]
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaWithDifferentTablePlan],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        1)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("table renderer pair 'table-layout-complex' page 1", StringComparison.Ordinal)
                && f.Contains("table plan signatures differ", StringComparison.Ordinal)
                && f.Contains("GridTable4", StringComparison.Ordinal)
                && f.Contains("AlteredTableStyle", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresMatchingFieldPlanEvidence()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "field-page-number-variants";
            var wpfRows = Enumerable.Range(1, 3)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    page,
                    pageCount: 3))
                .ToList();
            var avaloniaRows = Enumerable.Range(1, 3)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    page,
                    pageCount: 3))
                .ToList();
            avaloniaRows[1] = avaloniaRows[1] with
            {
                PageExpectation = avaloniaRows[1].PageExpectation with
                {
                    Fields = avaloniaRows[1].PageExpectation.Fields with
                    {
                        ComplexFieldKeywords = ["AUTHOR", "NUMPAGES", "PAGE"]
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        3),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        3)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("field renderer pair 'field-page-number-variants' page 2", StringComparison.Ordinal)
                && f.Contains("complex field keywords differ", StringComparison.Ordinal)
                && f.Contains("TITLE", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_ReportsMissingExpectedScenario()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "f2-hf-basic",
                pageNumber: 1,
                pageCount: 1);
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [row],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-hf-basic",
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-comments",
                        1)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Scenarios.Single(s => s.ScenarioId == "f2-comments").Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("f2-comments", StringComparison.Ordinal)
                && f.Contains("expected at least 1 trusted output", StringComparison.Ordinal));
            Action act = () => FreeWVisualEvidenceManifestNormalizer.EnsureSummaryTrusted(summary);
            act.Should().Throw<InvalidOperationException>().WithMessage("*f2-comments*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_DoesNotInferCoverageFromMissingCaptureFiles()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "f2-hf-basic",
                pageNumber: 1,
                pageCount: 1);
            File.Delete(Path.Combine(wpfDir, row.OutputName));
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [row],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-hf-basic",
                        1)
                ]);

            var scenario = summary.Scenarios.Single(s => s.ScenarioId == "f2-hf-basic");
            scenario.ActualOutputs.Should().Be(1);
            scenario.TrustedOutputs.Should().Be(0);
            scenario.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("output file 'wpf/f2-hf-basic_p1.png' does not exist", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("expected at least 1 trusted output", StringComparison.Ordinal)
                && f.Contains("found 0", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_ReportsMissingBackstageRendererPair()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "backstage-print-preview-fidelity",
                pageNumber: 1,
                pageCount: 2);
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [row],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "backstage-print-preview-fidelity",
                        2),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "backstage-print-preview-fidelity",
                        2)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("wpf-fidelity-render/backstage-print-preview-fidelity", StringComparison.Ordinal)
                && f.Contains("expected at least 2 trusted output", StringComparison.Ordinal)
                && f.Contains("found 1", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("avalonia-page-layout-shot/backstage-print-preview-fidelity", StringComparison.Ordinal)
                && f.Contains("expected at least 2 trusted output", StringComparison.Ordinal)
                && f.Contains("found 0", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("backstage renderer pair 'backstage-print-preview-fidelity'", StringComparison.Ordinal)
                && f.Contains("missing Avalonia page(s): p1, p2", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RejectsBackstagePlaceholderFallbackRows()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "backstage-pdf-export-fidelity";
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        pageNumber: 1,
                        pageCount: 2),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        pageNumber: 2,
                        pageCount: 2)
                ],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var avaloniaP1 = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 2);
            var placeholderFallback = avaloniaP1 with
            {
                HostMetadata = new Dictionary<string, string>
                {
                    ["renderer"] = "FreeW.PageLayoutShot",
                    ["captureSource"] = "skia-fallback-placeholder",
                    ["viewMode"] = "PrintLayout"
                }
            };
            var avaloniaP2 = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                scenarioId,
                pageNumber: 2,
                pageCount: 2);
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [placeholderFallback, avaloniaP2],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        2),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        2)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Evidence.Single(row =>
                row.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                row.ScenarioId == scenarioId &&
                row.PageNumber == 1)
                .Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("backstage renderer evidence cannot use placeholder capture metadata", StringComparison.Ordinal)
                && f.Contains("skia-fallback-placeholder", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("backstage renderer pair 'backstage-pdf-export-fidelity'", StringComparison.Ordinal)
                && f.Contains("missing Avalonia page(s): p1", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RejectsBackstageRowsWithoutRealCaptureSource()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "backstage-print-preview-fidelity",
                pageNumber: 1,
                pageCount: 2);
            var metadataOnlyRow = row with
            {
                HostMetadata = new Dictionary<string, string>
                {
                    ["renderer"] = "FreeW.FidelityRender"
                }
            };
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [metadataOnlyRow],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "backstage-print-preview-fidelity",
                        1)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Evidence.Single().Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("backstage renderer evidence must declare real captureSource metadata", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("wpf-fidelity-render/backstage-print-preview-fidelity", StringComparison.Ordinal)
                && f.Contains("expected at least 1 trusted output", StringComparison.Ordinal)
                && f.Contains("found 0", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresBackstageWorkflowMetadata()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "backstage-print-preview-fidelity",
                pageNumber: 1,
                pageCount: 1);
            var missingWorkflow = row with
            {
                HostMetadata = new Dictionary<string, string>
                {
                    ["renderer"] = "FreeW.FidelityRender",
                    ["captureSource"] = "software-renderer"
                }
            };
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [missingWorkflow],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "backstage-print-preview-fidelity",
                        1)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Evidence.Single().Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("must declare backstageWorkflow 'print-preview'", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RejectsCrossWiredBackstageWorkflowMetadata()
    {
        var root = CreateTempRoot();
        try
        {
            var avaloniaDir = Path.Combine(root, "avalonia");
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                "backstage-pdf-export-fidelity",
                pageNumber: 1,
                pageCount: 1);
            var crossWired = row with
            {
                HostMetadata = new Dictionary<string, string>
                {
                    ["renderer"] = "FreeW.PageLayoutShot",
                    ["captureSource"] = "avalonia-render-target",
                    ["backstageWorkflow"] = "print-preview"
                }
            };
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [crossWired],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "backstage-pdf-export-fidelity",
                        1)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Evidence.Single().Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("must use backstageWorkflow 'pdf-export', found 'print-preview'", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_ReportsMissingRequiredBackstageRendererPages()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "backstage-print-preview-fidelity",
                        pageNumber: 1,
                        pageCount: 3),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "backstage-print-preview-fidelity",
                        pageNumber: 2,
                        pageCount: 3)
                ],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "backstage-print-preview-fidelity",
                        pageNumber: 1,
                        pageCount: 3),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "backstage-print-preview-fidelity",
                        pageNumber: 3,
                        pageCount: 3)
                ],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "backstage-print-preview-fidelity",
                        2),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "backstage-print-preview-fidelity",
                        2)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("backstage renderer pair 'backstage-print-preview-fidelity'", StringComparison.Ordinal)
                && f.Contains("missing Avalonia page(s): p2", StringComparison.Ordinal));
            summary.Trust.Failures.Should().NotContain(f =>
                f.Contains("missing WPF page(s): p3", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ToMarkdown_IncludesDeterministicBackstagePrintEvidenceReadiness()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "backstage-print-preview-fidelity";
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        pageNumber: 1,
                        pageCount: 2),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        pageNumber: 2,
                        pageCount: 2)
                ],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var avaloniaP1 = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 2);
            File.Delete(Path.Combine(avaloniaDir, avaloniaP1.OutputName));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaP1],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        2),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        2)
                ]);

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(summary);

            markdown.Should().Contain("## Backstage Print Evidence Readiness");
            markdown.Should().Contain(
                "| backstage-print-preview-fidelity | wpf-fidelity-render | 1 | trusted | wpf/backstage-print-preview_p1.png | ready |");
            markdown.Should().Contain(
                "| backstage-print-preview-fidelity | avalonia-page-layout-shot | 1 | failed | avalonia/backstage-print-preview_p1.png | output file 'avalonia/backstage-print-preview_p1.png' does not exist |");
            markdown.Should().Contain(
                "| backstage-print-preview-fidelity | avalonia-page-layout-shot | 2 | missing | - | no normalized row |");
            markdown.Should().NotContain("| backstage-pdf-export-fidelity |");

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(summary);
            using var doc = JsonDocument.Parse(json);
            var readiness = doc.RootElement
                .GetProperty("backstagePrintEvidenceReadiness")
                .EnumerateArray()
                .ToArray();
            readiness.Should().Contain(row =>
                row.GetProperty("hostId").GetString() == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                row.GetProperty("pageNumber").GetInt32() == 1 &&
                row.GetProperty("status").GetString() == "failed" &&
                row.GetProperty("notes").GetString() == "output file 'avalonia/backstage-print-preview_p1.png' does not exist");
            var evidence = doc.RootElement
                .GetProperty("evidence")
                .EnumerateArray()
                .ToArray();
            evidence.Should().Contain(row =>
                row.GetProperty("hostId").GetString() == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                row.GetProperty("scenarioId").GetString() == "backstage-print-preview-fidelity" &&
                row.GetProperty("pageNumber").GetInt32() == 1 &&
                row.GetProperty("hostMetadata").GetProperty("backstageWorkflow").GetString() == "print-preview");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_ReportsMissingRequiredReviewRendererPages()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-comments",
                        pageNumber: 1,
                        pageCount: 2)
                ],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "f2-comments",
                        pageNumber: 2,
                        pageCount: 2)
                ],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-comments",
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "f2-comments",
                        1)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("review renderer pair 'f2-comments'", StringComparison.Ordinal)
                && f.Contains("missing Avalonia page(s): p1", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_ValidatesPairedSectionGeometryMetadata()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var wpfRows = new[]
            {
                BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    "f2-section-landscape",
                    pageNumber: 1,
                    pageCount: 2),
                BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    "f2-section-landscape",
                    pageNumber: 2,
                    pageCount: 2)
            };
            var avaloniaP1 = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                "f2-section-landscape",
                pageNumber: 1,
                pageCount: 2);
            var avaloniaP2 = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                "f2-section-landscape",
                pageNumber: 2,
                pageCount: 2);
            var badAvaloniaP2 = avaloniaP2 with
            {
                PageExpectation = avaloniaP2.PageExpectation with
                {
                    Features = avaloniaP2.PageExpectation.Features with
                    {
                        Section = new FreeWVisualSectionExpectation("section-1", 1, 1)
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaP1, badAvaloniaP2],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-section-landscape",
                        2),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "f2-section-landscape",
                        2)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("section-geometry renderer pair 'f2-section-landscape' page 2", StringComparison.Ordinal)
                && f.Contains("section ordinals differ", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_AllowsPairedWordArtWatermarkMetadata()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            const string scenarioId = "wordart-picture-watermark-layout";
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        pageNumber: 1,
                        pageCount: 1)
                ],
                new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        pageNumber: 1,
                        pageCount: 1)
                ],
                new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        1)
                ]);

            summary.Trust.Passed.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RejectsCrossHostWordArtWatermarkMetadataDrift()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            const string scenarioId = "wordart-picture-watermark-layout";
            var wpfRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);
            var avaloniaRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);
            var driftedAvaloniaRow = avaloniaRow with
            {
                PageExpectation = avaloniaRow.PageExpectation with
                {
                    Features = avaloniaRow.PageExpectation.Features with
                    {
                        Watermark = avaloniaRow.PageExpectation.Features.Watermark with
                        {
                            Text = "STALE WATERMARK"
                        }
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [driftedAvaloniaRow],
                new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        1)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("WordArt watermark renderer pair 'wordart-picture-watermark-layout' page 1", StringComparison.Ordinal)
                && f.Contains("page feature signatures differ", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_AllowsBackstageRendererExtraPagesAndDifferentCaptureDimensions()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "backstage-pdf-export-fidelity",
                        pageNumber: 1,
                        pageCount: 2),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "backstage-pdf-export-fidelity",
                        pageNumber: 3,
                        pageCount: 3),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "backstage-pdf-export-fidelity",
                        pageNumber: 2,
                        pageCount: 3)
                ],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "backstage-pdf-export-fidelity",
                        pageNumber: 1,
                        pageCount: 2,
                        pixelWidth: 24,
                        pixelHeight: 20),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "backstage-pdf-export-fidelity",
                        pageNumber: 2,
                        pageCount: 2)
                ],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "backstage-pdf-export-fidelity",
                        2),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "backstage-pdf-export-fidelity",
                        2)
                ]);

            summary.Trust.Passed.Should().BeTrue();
            summary.Evidence.Where(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                e.ScenarioId == "backstage-pdf-export-fidelity")
                .Should().HaveCount(3);
            summary.Evidence.Single(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                e.ScenarioId == "backstage-pdf-export-fidelity" &&
                e.PageNumber == 1)
                .PixelWidth.Should().Be(24);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ComputeBaselineComparisonMetrics_EvaluatesNamedTolerance()
    {
        var baseline = BuildBgraPixels(2, 2, (10, 10, 10));
        var actual = baseline.ToArray();
        actual[2] = 30;

        var metrics = FreeWVisualBaselineComparisonPlanner.ComputeMetrics(
            actual,
            actualWidth: 2,
            actualHeight: 2,
            actualStride: 8,
            FreeWVisualEvidencePixelFormat.Bgra32,
            baseline,
            baselineWidth: 2,
            baselineHeight: 2,
            baselineStride: 8,
            FreeWVisualEvidencePixelFormat.Bgra32,
            changedPixelDeltaThreshold: 8);

        metrics.DimensionsMatch.Should().BeTrue();
        metrics.BaselineResized.Should().BeFalse();
        metrics.ComparedPixels.Should().Be(4);
        metrics.ChangedPixels.Should().Be(1);
        metrics.MeanAbsoluteChannelDelta.Should().BeApproximately(1.6667, 0.0001);
        metrics.MeanAbsoluteGrayscaleDelta.Should().BeApproximately(1.495, 0.0001);
        metrics.ChangedPixelRatio.Should().Be(0.25);

        var strict = new FreeWVisualBaselineComparisonTolerance(
            "unit-strict",
            ChangedPixelDeltaThreshold: 8,
            MaxMeanAbsoluteChannelDelta: 5,
            MaxMeanAbsoluteGrayscaleDelta: 5,
            MaxChangedPixelRatio: 0.10,
            RequireDimensionMatch: true);
        var strictTrust = FreeWVisualBaselineComparisonPlanner.EvaluateTolerance(metrics, strict);

        strictTrust.Passed.Should().BeFalse();
        strictTrust.Failures.Should().Contain(f =>
            f.Contains("changed pixel ratio", StringComparison.Ordinal)
            && f.Contains("unit-strict", StringComparison.Ordinal));

        var lenient = strict with { Name = "unit-lenient", MaxChangedPixelRatio = 0.25 };
        FreeWVisualBaselineComparisonPlanner.EvaluateTolerance(metrics, lenient)
            .Passed.Should().BeTrue();
    }

    [Fact]
    public void BuildMissingBaselineComparison_FailsSummaryTrustTruthfully()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "f2-hf-basic",
                pageNumber: 1,
                pageCount: 1);
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [row],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-hf-basic",
                        1)
                ]);

            var comparison = FreeWVisualBaselineComparisonPlanner.BuildMissingBaselineComparison(
                summary.Evidence.Single());
            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                [comparison]);

            withBaseline.Trust.Passed.Should().BeFalse();
            withBaseline.BaselineComparisons.Single().Status.Should().Be(
                FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus);
            withBaseline.BaselineComparisons.Single().BaselineId.Should().Be(
                "f2-hf-basic/p1/f2-hf-basic_p1.png");
            withBaseline.BaselineComparisons.Single().BaselinePath.Should().BeEmpty();
            withBaseline.BaselineComparisons.Single().CandidateBaselinePaths.Should().Contain([
                "f2-hf-basic/f2-hf-basic_p1.png",
                "f2-hf-basic_p1.png"]);
            withBaseline.BaselineComparisons.Single().SkipReason.Should().Contain(
                "missing Word baseline PNG");
            withBaseline.Trust.Failures.Should().Contain(f =>
                f.Contains("missing Word baseline PNG", StringComparison.Ordinal)
                && f.Contains("f2-hf-basic/p1/f2-hf-basic_p1.png", StringComparison.Ordinal));
            Action act = () => FreeWVisualEvidenceManifestNormalizer.EnsureSummaryTrusted(withBaseline);
            act.Should().Throw<InvalidOperationException>().WithMessage("*missing Word baseline PNG*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildUnavailableBaselineComparison_KeepsNoWordSummaryTrusted()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "f2-hf-basic",
                pageNumber: 1,
                pageCount: 1);
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [row],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-hf-basic",
                        1)
                ]);

            var comparison = FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                summary.Evidence.Single(),
                FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                "COM ProgID 'Word.Application' is not registered");
            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                [comparison]);

            withBaseline.Trust.Passed.Should().BeTrue();
            var unavailable = withBaseline.BaselineComparisons.Single();
            unavailable.Status.Should().Be(FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
            unavailable.Metrics.Should().BeNull();
            unavailable.SkipReason.Should().Contain("Word.Application");
            unavailable.CandidateBaselinePaths.Should().Contain("f2-hf-basic/f2-hf-basic_p1.png");

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            var triageItem = doc.RootElement.GetProperty("wordBaselineTriage")[0];
            triageItem.GetProperty("status").GetString()
                .Should().Be("word-baseline-unavailable");
            triageItem.GetProperty("triageStatus").GetString()
                .Should().Be("word-unavailable");
            triageItem.GetProperty("note").GetString()
                .Should().Contain("Word.Application");
            var baselineComparison = doc.RootElement.GetProperty("baselineComparisons")[0];
            baselineComparison.GetProperty("status").GetString()
                .Should().Be("word-baseline-unavailable");
            baselineComparison.GetProperty("baselineEvidenceClass").GetString()
                .Should().Be("word-baseline-unavailable");
            baselineComparison.GetProperty("baselineEvidenceDescription").GetString()
                .Should().Contain("no authoritative Word PNG parity claimed");
            baselineComparison.GetProperty("skipReason").GetString()
                .Should().Contain("Word.Application");

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Word Baseline Triage");
            markdown.Should().Contain("Word baseline unavailable: 1 row(s). Trust remains passed for unavailable rows.");
            markdown.Should().Contain("Unavailable reason(s): COM ProgID 'Word.Application' is not registered");
            markdown.Should().Contain("Triage counts: word-unavailable=1");
            markdown.Should().Contain("Status counts: word-baseline-unavailable=1");
            markdown.Should().Contain("Evidence class counts: word-baseline-unavailable=1");
            markdown.Should().Contain("word-baseline-unavailable=Word COM or baseline generation unavailable; no authoritative Word PNG parity claimed");
            markdown.Should().Contain("COM ProgID 'Word.Application' is not registered");
            markdown.Should().Contain("f2-hf-basic/f2-hf-basic_p1.png");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReferencesHeavyNoWordSummary_ReportsToaPageNumberEvidenceBlocker()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "references-heavy-fields",
                pageNumber: 1,
                pageCount: 2);
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [row],
                new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero));
            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "references-heavy-fields",
                        1)
                ]);

            var comparison = FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                summary.Evidence.Single(),
                FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                "COM ProgID 'Word.Application' is not registered");
            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                [comparison]);

            withBaseline.Trust.Passed.Should().BeTrue();
            var blocker = withBaseline.RemainingEvidenceBlockers.Should().ContainSingle().Subject;
            blocker.BlockerId.Should().Be("references-heavy-toa-page-number-fidelity");
            blocker.ScenarioId.Should().Be("references-heavy-fields");
            blocker.Area.Should().Be("TOA page-number fidelity");
            blocker.Status.Should().Be(FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
            blocker.RequiredEvidence.Should().Contain("real MS Word PNG comparisons");
            blocker.Reason.Should().Contain("Word.Application");
            blocker.SemanticEvidence.Should().Contain(evidence =>
                evidence.Contains("Example v. FreeW, 123 F.4th 456 (2026) -> 1, 2", StringComparison.Ordinal));
            blocker.SemanticEvidence.Should().Contain(evidence =>
                evidence.Contains("Free Software Evidence Act, 42 U.S.C. 2026 -> 1", StringComparison.Ordinal));
            blocker.RequiresWordBaseline.Should().BeTrue();
            blocker.RelatedBaselineStatuses.Should().Contain(
                FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
            blocker.CandidateBaselinePaths.Should().Contain("references-heavy-fields/references-heavy-fields_p1.png");
            blocker.Trust.Passed.Should().BeTrue();

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            var jsonBlocker = doc.RootElement.GetProperty("remainingEvidenceBlockers")[0];
            jsonBlocker.GetProperty("blockerId").GetString()
                .Should().Be("references-heavy-toa-page-number-fidelity");
            jsonBlocker.GetProperty("status").GetString()
                .Should().Be("word-baseline-unavailable");
            jsonBlocker.GetProperty("semanticEvidence").GetArrayLength().Should().Be(2);
            jsonBlocker.GetProperty("requiresWordBaseline").GetBoolean().Should().BeTrue();
            jsonBlocker.GetProperty("trust").GetProperty("passed").GetBoolean()
                .Should().BeTrue();

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Remaining Evidence Blockers");
            markdown.Should().Contain("references-heavy-toa-page-number-fidelity");
            markdown.Should().Contain("TOA page-number fidelity");
            markdown.Should().Contain("Example v. FreeW, 123 F.4th 456 (2026) -> 1, 2");
            markdown.Should().Contain("yes");
            markdown.Should().Contain("COM ProgID 'Word.Application' is not registered");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WordBaselineTriage_SurfacesFailedMissingAndDecodeRowsAheadOfSkippedRows()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-hf-basic",
                        pageNumber: 1,
                        pageCount: 1),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "table-layout-complex",
                        pageNumber: 1,
                        pageCount: 1)
                ],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "page-composition-columns",
                        pageNumber: 1,
                        pageCount: 1),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "page-composition-web-layout",
                        pageNumber: 1,
                        pageCount: 1)
                ],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-hf-basic",
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "table-layout-complex",
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "page-composition-columns",
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "page-composition-web-layout",
                        1)
                ]);

            var failedRow = summary.Evidence.Single(row =>
                row.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId
                && row.ScenarioId == "f2-hf-basic");
            var decodeRow = summary.Evidence.Single(row =>
                row.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId
                && row.ScenarioId == "table-layout-complex");
            var missingRow = summary.Evidence.Single(row =>
                row.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId
                && row.ScenarioId == "page-composition-columns");
            var skippedRow = summary.Evidence.Single(row =>
                row.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId
                && row.ScenarioId == "page-composition-web-layout");
            var baseline = BuildBgraPixels(2, 2, (10, 10, 10));
            var actual = baseline.ToArray();
            actual[2] = 80;
            var failed = FreeWVisualBaselineComparisonPlanner.BuildBaselineComparison(
                failedRow,
                "f2-hf-basic/f2-hf-basic_p1.png",
                FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(failedRow),
                FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                actual,
                actualWidth: 2,
                actualHeight: 2,
                actualStride: 8,
                FreeWVisualEvidencePixelFormat.Bgra32,
                baseline,
                baselineWidth: 2,
                baselineHeight: 2,
                baselineStride: 8,
                FreeWVisualEvidencePixelFormat.Bgra32);
            failed.Status.Should().Be(FreeWVisualBaselineComparisonPlanner.FailedStatus);
            failed.Metrics.Should().NotBeNull();
            FreeWVisualBaselineComparisonPlanner.ClassifyBaselineEvidence(failed)
                .Should().Be(FreeWVisualBaselineComparisonPlanner.RealWordPngComparisonFailedClass);
            FreeWVisualBaselineComparisonPlanner.DescribeBaselineEvidence(failed)
                .Should().Contain("metrics and tolerance failures are recorded");
            var decode = FreeWVisualBaselineComparisonPlanner.BuildDecodeFailure(
                decodeRow,
                "table-layout-complex/table-layout-complex_p1.png",
                FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(decodeRow),
                FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                "could not decode visual evidence or Word baseline PNG");
            var missing = FreeWVisualBaselineComparisonPlanner.BuildMissingBaselineComparison(missingRow);
            var skipped = FreeWVisualBaselineComparisonPlanner.BuildSkippedBaselineComparison(skippedRow);

            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                [skipped, missing, decode, failed]);

            withBaseline.WordBaselineTriage.Select(item => item.Status).Take(3)
                .Should().BeEquivalentTo([
                    FreeWVisualBaselineComparisonPlanner.FailedStatus,
                    FreeWVisualBaselineComparisonPlanner.DecodeFailedStatus,
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus
                ]);
            withBaseline.WordBaselineTriage.Select(item => item.TriageStatus).Take(3)
                .Should().Equal([
                    "needs-render-review",
                    "needs-baseline",
                    "needs-decode-fix"
                ]);
            withBaseline.WordBaselineTriage.Last().Status.Should().Be(
                FreeWVisualBaselineComparisonPlanner.SkippedStatus);

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("Triage counts:");
            markdown.Should().Contain("needs-render-review=1");
            markdown.Should().Contain("needs-decode-fix=1");
            markdown.Should().Contain("needs-baseline=1");
            markdown.Should().Contain("not-in-scope=1");
            markdown.Should().Contain("Evidence class counts:");
            markdown.Should().Contain("real-word-png-comparison-failed=1");
            markdown.Should().Contain("png-decode-failed=1");
            markdown.Should().Contain("word-png-baseline-missing=1");
            markdown.Should().Contain("scenario-skipped-or-unmapped=1");
            markdown.Should().Contain("Skipped rows hidden from triage table: 1.");
            markdown.Should().NotContain("| avalonia-page-layout-shot | page-composition-web-layout | p1/freew_web_layout.png | not-in-scope | skipped |");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WordBaselineTriage_SortsComparedRowsByChangedPixelRatioAcrossWpfAndAvalonia()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "table-layout-complex",
                        pageNumber: 1,
                        pageCount: 1)
                ],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "table-layout-complex",
                        pageNumber: 1,
                        pageCount: 1)
                ],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "table-layout-complex",
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "table-layout-complex",
                        1)
                ]);

            var tolerance = new FreeWVisualBaselineComparisonTolerance(
                "unit-wide",
                ChangedPixelDeltaThreshold: 8,
                MaxMeanAbsoluteChannelDelta: 255,
                MaxMeanAbsoluteGrayscaleDelta: 255,
                MaxChangedPixelRatio: 1,
                RequireDimensionMatch: true);
            var baseline = BuildBgraPixels(2, 2, (10, 10, 10));
            var wpfActual = BuildChangedBgraPixels(changedPixels: 1);
            var avaloniaActual = BuildChangedBgraPixels(changedPixels: 3);
            var wpfRow = summary.Evidence.Single(row =>
                row.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId);
            var avaloniaRow = summary.Evidence.Single(row =>
                row.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId);
            var wpfComparison = FreeWVisualBaselineComparisonPlanner.BuildBaselineComparison(
                wpfRow,
                "table-layout-complex/table-layout-complex_p1.png",
                FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(wpfRow),
                tolerance,
                wpfActual,
                actualWidth: 2,
                actualHeight: 2,
                actualStride: 8,
                FreeWVisualEvidencePixelFormat.Bgra32,
                baseline,
                baselineWidth: 2,
                baselineHeight: 2,
                baselineStride: 8,
                FreeWVisualEvidencePixelFormat.Bgra32);
            var avaloniaComparison = FreeWVisualBaselineComparisonPlanner.BuildBaselineComparison(
                avaloniaRow,
                "table-layout-complex/table-layout-complex_p1.png",
                FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(avaloniaRow),
                tolerance,
                avaloniaActual,
                actualWidth: 2,
                actualHeight: 2,
                actualStride: 8,
                FreeWVisualEvidencePixelFormat.Bgra32,
                baseline,
                baselineWidth: 2,
                baselineHeight: 2,
                baselineStride: 8,
                FreeWVisualEvidencePixelFormat.Bgra32);

            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                [wpfComparison, avaloniaComparison]);

            withBaseline.WordBaselineTriage.Select(item => item.HostId).Should().Equal([
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId
            ]);
            withBaseline.WordBaselineTriage.Select(item => item.ChangedPixelRatio).Should().Equal([
                0.75,
                0.25
            ]);
            withBaseline.WordBaselineTriage.Select(item => item.ChangedPixels).Should().Equal([
                3,
                1
            ]);
            withBaseline.WordBaselineTriage.Select(item => item.ComparedPixels).Should().Equal([
                4,
                4
            ]);

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("| avalonia-page-layout-shot | table-layout-complex | p1/table-layout-complex_p1.png | within-tolerance | passed | 3/4 (75.000 %) |");
            markdown.Should().Contain("| wpf-fidelity-render | table-layout-complex | p1/table-layout-complex_p1.png | within-tolerance | passed | 1/4 (25.000 %) |");
            markdown.IndexOf("## Word Baseline Triage", StringComparison.Ordinal)
                .Should().BeLessThan(markdown.IndexOf("## Word Baseline Comparison", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WithBaselineComparisons_SerializesJsonAndMarkdownSummary()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "f2-hf-basic",
                pageNumber: 1,
                pageCount: 1);
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [row],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-hf-basic",
                        1)
                ]);
            var normalizedRow = summary.Evidence.Single();
            var pixels = BuildBgraPixels(2, 2, (24, 48, 72));
            var comparison = FreeWVisualBaselineComparisonPlanner.BuildBaselineComparison(
                normalizedRow,
                "f2-hf-basic/f2-hf-basic_p1.png",
                FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(normalizedRow),
                FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                pixels,
                actualWidth: 2,
                actualHeight: 2,
                actualStride: 8,
                FreeWVisualEvidencePixelFormat.Bgra32,
                pixels,
                baselineWidth: 2,
                baselineHeight: 2,
                baselineStride: 8,
                FreeWVisualEvidencePixelFormat.Bgra32);

            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                [comparison]);

            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.BaselineComparisons.Single().Status.Should().Be(
                FreeWVisualBaselineComparisonPlanner.PassedStatus);

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(
                FreeWVisualEvidenceManifestNormalizer.SummarySchemaVersion);
            var triageItem = doc.RootElement.GetProperty("wordBaselineTriage")[0];
            triageItem.GetProperty("status").GetString().Should().Be("passed");
            triageItem.GetProperty("triageStatus").GetString().Should().Be("within-tolerance");
            triageItem.GetProperty("baselineId").GetString()
                .Should().Be("f2-hf-basic/p1/f2-hf-basic_p1.png");
            triageItem.GetProperty("baselinePathSummary").GetString()
                .Should().Be("f2-hf-basic/f2-hf-basic_p1.png");
            triageItem.GetProperty("changedPixels").GetInt64().Should().Be(0);
            triageItem.GetProperty("comparedPixels").GetInt64().Should().Be(4);
            triageItem.GetProperty("changedPixelRatio").GetDouble().Should().Be(0);
            triageItem.GetProperty("toleranceSummary").GetString()
                .Should().Contain("word-png-default");
            var baselineComparison = doc.RootElement.GetProperty("baselineComparisons")[0];
            baselineComparison.GetProperty("status").GetString().Should().Be("passed");
            baselineComparison.GetProperty("baselineEvidenceClass").GetString()
                .Should().Be("real-word-png-compared");
            baselineComparison.GetProperty("baselineEvidenceDescription").GetString()
                .Should().Contain("compared within tolerance");
            baselineComparison.GetProperty("baselineId").GetString()
                .Should().Be("f2-hf-basic/p1/f2-hf-basic_p1.png");
            baselineComparison.GetProperty("baselineScenarioId").GetString()
                .Should().Be("f2-hf-basic");
            baselineComparison.GetProperty("tolerance").GetProperty("name").GetString()
                .Should().Be("word-png-default");
            baselineComparison.GetProperty("metrics").GetProperty("changedPixels")
                .GetInt64().Should().Be(0);
            baselineComparison.GetProperty("metrics").GetProperty("meanAbsoluteChannelDelta")
                .GetDouble().Should().Be(0);

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.IndexOf("## Word Baseline Triage", StringComparison.Ordinal)
                .Should().BeLessThan(markdown.IndexOf("## Word Baseline Comparison", StringComparison.Ordinal));
            markdown.Should().Contain("| wpf-fidelity-render | f2-hf-basic | p1/f2-hf-basic_p1.png | within-tolerance | passed | 0/4 (0.000 %) | 0/0 | word-png-default: changed <= 2.000 %, mean <= 3/3, pixel delta > 8, dimensions must match | f2-hf-basic/f2-hf-basic_p1.png | - |");
            markdown.Should().Contain("Word Baseline Comparison");
            markdown.Should().Contain("Triage counts: within-tolerance=1");
            markdown.Should().Contain("Status counts: passed=1");
            markdown.Should().Contain("Evidence class counts: real-word-png-compared=1");
            markdown.Should().Contain("real-word-png-compared=real Word PNG baseline available and compared within tolerance");
            markdown.Should().Contain("f2-hf-basic/p1/f2-hf-basic_p1.png");
            markdown.Should().Contain("word-png-default");
            markdown.Should().Contain("pixel delta > 8");
            markdown.Should().Contain("0/4 (0.000 %)");
            markdown.Should().Contain("0.000 %");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FreeWVisualEvidenceRow BuildRow(
        FreeWVisualPixelStats stats,
        long byteLength,
        string outputName = "blank.png")
    {
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "f2-hf-basic",
            new PageSettings(),
            pageNumber: 1,
            pageCount: 1,
            outputName: outputName);
        var capture = new FreeWVisualEvidenceCapture(
            ScenarioId: "f2-hf-basic",
            HostId: "test-host",
            OutputName: outputName,
            OutputPath: outputName,
            PixelWidth: stats.Width,
            PixelHeight: stats.Height,
            ByteLength: byteLength,
            PixelStats: stats,
            PageExpectation: expectation,
            HostMetadata: new Dictionary<string, string> { ["renderer"] = "test" });

        return FreeWVisualEvidencePlanner.BuildEvidenceRow(capture);
    }

    private static FreeWVisualEvidenceRow BuildFileBackedRow(
        string root,
        string hostId,
        string scenarioId,
        int pageNumber,
        int pageCount,
        int pixelWidth = 20,
        int pixelHeight = 20)
    {
        var scenario = FreeWVisualEvidencePlanner.ResolveScenario(scenarioId);
        var outputName = FreeWVisualEvidencePlanner.ExpectedOutputName(scenarioId, pageNumber);
        var outputDir = Path.Combine(
            root,
            hostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId ? "wpf" : "avalonia");
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, outputName);
        var bytes = Enumerable.Range(0, 2_048).Select(i => (byte)(i % 251)).ToArray();
        File.WriteAllBytes(outputPath, bytes);

        var stats = BuildTrustedStats(pixelWidth, pixelHeight);
        var document = DocumentForScenario(scenarioId);
        var sectionPage = document is not null
            ? FreeWVisualEvidencePlanner
                .BuildSectionGeometryPagePlans(document, pageCount)
                .FirstOrDefault(page => page.PageNumber == pageNumber)
            : null;
        var page = sectionPage?.Page ?? document?.Page ?? PageForScenario(scenarioId);
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            scenarioId,
            page,
            pageNumber,
            pageCount,
            outputName,
            scenario.LayoutKind,
            sectionOrdinal: sectionPage?.SectionOrdinal,
            sectionRelativePageNumber: sectionPage?.SectionRelativePageNumber,
            sectionOwnerId: sectionPage?.SectionOwnerId,
            document: document);
        var capture = new FreeWVisualEvidenceCapture(
            ScenarioId: scenarioId,
            HostId: hostId,
            OutputName: outputName,
            OutputPath: outputPath,
            PixelWidth: stats.Width,
            PixelHeight: stats.Height,
            ByteLength: bytes.LongLength,
            PixelStats: stats,
            PageExpectation: expectation,
            HostMetadata: BuildFileBackedHostMetadata(hostId, scenarioId));

        return FreeWVisualEvidencePlanner.BuildEvidenceRow(capture);
    }

    private static Dictionary<string, string> BuildFileBackedHostMetadata(string hostId, string scenarioId)
    {
        var metadata = new Dictionary<string, string> { ["renderer"] = hostId };
        if (!FreeWVisualEvidenceManifestNormalizer.BackstageRendererScenarioIds
            .Contains(scenarioId, StringComparer.OrdinalIgnoreCase))
        {
            return metadata;
        }

        metadata["captureSource"] = hostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId
            ? "software-renderer"
            : "avalonia-render-target";
        metadata["backstageWorkflow"] = scenarioId switch
        {
            "backstage-print-preview-fidelity" => "print-preview",
            "backstage-pdf-export-fidelity" => "pdf-export",
            _ => throw new InvalidOperationException($"Unsupported backstage visual evidence scenario: {scenarioId}")
        };
        return metadata;
    }

    private static TextDocument? DocumentForScenario(string scenarioId) =>
        FreeWVisualEvidencePlanner.NormalizeScenarioId(scenarioId) switch
        {
            "f2-footnotes" => FreeWVisualEvidenceDocumentFactory.BuildFootnotePlacementDocument(),
            "f2-endnotes" => FreeWVisualEvidenceDocumentFactory.BuildEndnotePlacementDocument(),
            "field-page-number-variants" => FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument(),
            "references-heavy-fields" => FreeWVisualEvidenceDocumentFactory.BuildReferencesHeavyFieldDocument(),
            "f2-tracked-changes" => FreeWVisualEvidenceDocumentFactory.BuildTrackedChangesReviewDocument(),
            "f2-comments" => FreeWVisualEvidenceDocumentFactory.BuildCommentsReviewDocument(),
            "table-layout-complex" => FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument(),
            "table-pagination-repeat-header" => FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument(),
            "drawing-objects-complex" => FreeWVisualEvidenceDocumentFactory.BuildDrawingObjectsCompositionDocument(),
            "object-format-position-size-style" => FreeWVisualEvidenceDocumentFactory.BuildObjectFormatPositionSizeStyleDocument(),
            "chart-smartart-complex" => FreeWVisualEvidenceDocumentFactory.BuildChartSmartArtCompositionDocument(),
            "wordart-watermark-stress" => FreeWVisualEvidenceDocumentFactory.BuildWordArtWatermarkStressDocument(),
            "wordart-picture-watermark-layout" => FreeWVisualEvidenceDocumentFactory.BuildWordArtPictureWatermarkLayoutDocument(),
            "f2-section-landscape" => FreeWVisualEvidenceDocumentFactory.BuildSectionGeometryDocument(),
            "backstage-print-preview-fidelity" => FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument(
                "Backstage Print Preview Fidelity",
                "Synthetic print preview renderer capture"),
            "backstage-pdf-export-fidelity" => FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument(
                "Backstage PDF Export Fidelity",
                "Synthetic PDF export renderer capture"),
            "page-composition-floating-image" => BuildFloatingImageDocument(),
            _ => null
        };

    private static TextDocument BuildFloatingImageDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Floating image evidence"));
        paragraph.Runs.Add(Run.FromImage(new InlineImage([1, 2, 3, 4], widthPt: 96, heightPt: 48)
        {
            Wrapping = ImageWrapping.Square,
            HorizontalOffsetPt = 24,
            VerticalOffsetPt = 12,
            ZOrderIndex = 3
        }));
        document.Blocks.Add(paragraph);
        return document;
    }

    private static FreeWVisualPixelStats BuildTrustedStats()
    {
        var pixels = BuildTrustedPixels();

        return FreeWVisualEvidencePlanner.ComputePixelStats(
            pixels,
            width: 20,
            height: 20,
            stride: 20 * 4,
            FreeWVisualEvidencePixelFormat.Bgra32);
    }

    private static FreeWVisualPixelStats BuildTrustedStats(int width, int height)
    {
        var pixels = BuildTrustedPixels(width, height);

        return FreeWVisualEvidencePlanner.ComputePixelStats(
            pixels,
            width: width,
            height: height,
            stride: width * 4,
            FreeWVisualEvidencePixelFormat.Bgra32);
    }

    private static byte[] BuildTrustedPixels()
    {
        return BuildTrustedPixels(20, 20);
    }

    private static byte[] BuildTrustedPixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                if (x is >= 2 and <= 17 && y is >= 8 and <= 12)
                {
                    pixels[offset + 0] = (byte)(x % 3 == 0 ? 32 : 0);
                    pixels[offset + 1] = (byte)(y % 2 == 0 ? 32 : 0);
                    pixels[offset + 2] = (byte)(x % 5 == 0 ? 160 : 0);
                }
                else
                {
                    pixels[offset + 0] = 255;
                    pixels[offset + 1] = 255;
                    pixels[offset + 2] = 255;
                }
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    private static byte[] BuildBgraPixels(
        int width,
        int height,
        (byte R, byte G, byte B) color)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 0] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    private static byte[] BuildChangedBgraPixels(int changedPixels)
    {
        var pixels = BuildBgraPixels(2, 2, (10, 10, 10));
        for (var pixel = 0; pixel < Math.Min(changedPixels, 4); pixel++)
            pixels[(pixel * 4) + 2] = 80;

        return pixels;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "FreeWVisualEvidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string FindRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find repo file: " + string.Join(Path.DirectorySeparatorChar, segments));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static PageSettings PageForScenario(string scenarioId)
    {
        var page = new PageSettings();
        if (scenarioId.Contains("columns", StringComparison.OrdinalIgnoreCase))
        {
            page.ColumnCount = 2;
            page.ColumnSpacingPt = 36;
            page.ColumnsLineBetween = true;
        }

        if (scenarioId.Contains("border-watermark", StringComparison.OrdinalIgnoreCase))
        {
            page.PageBorder = new PageBorder("#000080", 3);
            page.WatermarkOptions = new WatermarkOptions("DRAFT")
            {
                FontColorHex = "#808080",
                Opacity = 0.4,
                Layout = WatermarkLayout.Diagonal
            };
        }

        return page;
    }
}
