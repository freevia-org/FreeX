using System.Text.Json;
using System.Security.Cryptography;
using FreeW.Core.Model;
using FreeW.App.Presentation.Dialogs;
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
            "f2-hf-images",
            "field-page-number-variants",
            "references-heavy-fields",
            "legal-reference-section-page-numbers",
            "equation-structures",
            "f2-footnotes",
            "f2-endnotes",
            "f2-columns",
            "f2-border-watermark",
            "f2-section-landscape",
            "f2-tracked-changes",
            "f2-comments",
            "f2-01-float-wrap",
            "review-proofing-visual-depth",
            "review-protection-proofing-comments-only",
            "review-compare-visual-proof",
            "review-combine-visual-proof",
            "table-layout-complex",
            "table-pagination-repeat-header",
            "table-page-composition-stress",
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

        var reviewProofingScenario = FreeWVisualEvidencePlanner.ResolveScenario("review-proofing-visual-depth");
        reviewProofingScenario.ExpectedFeatureTags.Should().Contain([
            "review",
            "proofing",
            "tracked-changes",
            "format-revisions",
            "comment-replies",
            "resolved-comments",
            "table-comment-anchors",
            "proofing-language",
            "proofing-diagnostics",
            "proofing-adornments",
            "proofing-underline-intent"]);
        reviewProofingScenario.ExpectedOutputNamePattern.Should().Be("review-proofing-visual-depth_p{page}.png");
        reviewProofingScenario.Composition.ExpectsTrackedChanges.Should().BeTrue();
        reviewProofingScenario.Composition.ExpectsComments.Should().BeTrue();

        var reviewProtectionScenario = FreeWVisualEvidencePlanner.ResolveScenario("review-protection-proofing-comments-only");
        reviewProtectionScenario.ExpectedFeatureTags.Should().Contain([
            "review",
            "proofing",
            "comments-only-protection",
            "marked-as-final",
            "final-advisory-read-only",
            "review-protection-state",
            "protection-command-matrix",
            "proofing-replacement-blocked",
            "comment-workflow-blocked"]);
        reviewProtectionScenario.ExpectedFeatureTags.Should().Contain([
            "proofing-adornments",
            "proofing-underline-intent"]);
        reviewProtectionScenario.ExpectedOutputNamePattern.Should()
            .Be("review-protection-proofing-comments-only_p{page}.png");
        reviewProtectionScenario.Composition.ExpectsTrackedChanges.Should().BeTrue();
        reviewProtectionScenario.Composition.ExpectsComments.Should().BeTrue();

        var reviewCompareScenario = FreeWVisualEvidencePlanner.ResolveScenario("review-compare-visual-proof");
        reviewCompareScenario.ExpectedFeatureTags.Should().Contain([
            "review",
            "compare",
            "document-compare",
            "compare-semantics",
            "compare-authorship"]);
        reviewCompareScenario.ExpectedOutputNamePattern.Should().Be("review-compare-visual-proof_p{page}.png");
        reviewCompareScenario.Composition.ExpectsTrackedChanges.Should().BeTrue();

        var reviewCombineScenario = FreeWVisualEvidencePlanner.ResolveScenario("review-combine-visual-proof");
        reviewCombineScenario.ExpectedFeatureTags.Should().Contain([
            "review",
            "combine",
            "document-combine",
            "combine-semantics",
            "multi-author-revisions"]);
        reviewCombineScenario.ExpectedOutputNamePattern.Should().Be("review-combine-visual-proof_p{page}.png");
        reviewCombineScenario.Composition.ExpectsTrackedChanges.Should().BeTrue();

        var fieldScenario = FreeWVisualEvidencePlanner.ResolveScenario("field-page-number-variants");
        fieldScenario.ExpectedFeatureTags.Should().Contain([
            "fields",
            "page-number-fields",
            "numpages-fields",
            "document-property-fields",
            "complex-fields",
            "header-footer-fields"]);
        fieldScenario.ExpectedFeatureTags.Should().Contain([
            "resolved-header-footer-field-text",
            "chapter-prefixed-page-number-fields"]);
        fieldScenario.ExpectedOutputNamePattern.Should().Be("field-page-number-variants_p{page}.png");
        fieldScenario.MinimumExpectedOutputs.Should().Be(4);
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

        var legalReferenceScenario = FreeWVisualEvidencePlanner.ResolveScenario("legal-reference-section-page-numbers");
        legalReferenceScenario.ExpectedFeatureTags.Should().Contain([
            "references",
            "toa-fields",
            "generated-toa-page-references",
            "section-formatted-page-numbers",
            "legal-authorities"]);
        legalReferenceScenario.ExpectedOutputNamePattern.Should().Be("legal-reference-section-page-numbers_p{page}.png");
        legalReferenceScenario.MinimumExpectedOutputs.Should().Be(2);

        var equationScenario = FreeWVisualEvidencePlanner.ResolveScenario("equation-structures");
        equationScenario.ExpectedFeatureTags.Should().Contain([
            "equations",
            "officemath",
            "math-run-structures",
            "shared-equation-visual-planner",
            "scripts",
            "fractions",
            "radicals",
            "n-ary-operators",
            "matrices",
            "equation-arrays",
            "accents",
            "bars",
            "delimiters",
            "group-characters",
            "function-apply"]);
        equationScenario.ExpectedOutputNamePattern.Should().Be("equation-structures_p{page}.png");
        equationScenario.MinimumExpectedOutputs.Should().Be(1);

        var floatingScenario = FreeWVisualEvidencePlanner.ResolveScenario("page-composition-floating-image");
        floatingScenario.ExpectedFeatureTags.Should().Contain(["floating-objects", "top-bottom-wrap", "behind-text", "in-front"]);
        floatingScenario.Composition.ExpectsFloatingObjects.Should().BeTrue();

        var wpfFloatingWrapScenario = FreeWVisualEvidencePlanner.ResolveScenario("f2-01-float-wrap");
        wpfFloatingWrapScenario.ExpectedFeatureTags.Should().Contain(["floating-image", "square-wrap", "tight-wrap", "text-wrap-around"]);
        wpfFloatingWrapScenario.ExpectedOutputNamePattern.Should().Be("f2-01-float-wrap_p{page}.png");
        wpfFloatingWrapScenario.Composition.ExpectsFloatingObjects.Should().BeTrue();

        var hfImageScenario = FreeWVisualEvidencePlanner.ResolveScenario("f2-hf-images");
        hfImageScenario.ExpectedFeatureTags.Should().Contain(["header-footer", "header-footer-images", "multi-section"]);
        hfImageScenario.ExpectedOutputNamePattern.Should().Be("f2-hf-images_p{page}.png");
        hfImageScenario.MinimumExpectedOutputs.Should().Be(2);
        hfImageScenario.Composition.ExpectsHeadersFooters.Should().BeTrue();
        FreeWVisualEvidenceManifestNormalizer.SectionPageSurfaceRendererScenarioIds.Should().Contain("f2-hf-images");

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
        tableScenario.ExpectedFeatureTags.Should().Contain([
            "table-layout",
            "merged-cells",
            "repeat-header-row",
            "cell-shading",
            "table-fill-signatures",
            "style-derived-header-fill"
        ]);
        tableScenario.ExpectedOutputNamePattern.Should().Be("table-layout-complex_p{page}.png");
        tableScenario.Composition.ExpectsTables.Should().BeTrue();

        var tablePaginationScenario = FreeWVisualEvidencePlanner.ResolveScenario("table-pagination-repeat-header");
        tablePaginationScenario.ExpectedFeatureTags.Should().Contain(["table-pagination", "repeat-header-row", "keep-rows"]);
        tablePaginationScenario.ExpectedOutputNamePattern.Should().Be("table-pagination-repeat-header_p{page}.png");
        tablePaginationScenario.MinimumExpectedOutputs.Should().Be(2);
        tablePaginationScenario.Composition.ExpectsTables.Should().BeTrue();

        var tablePageCompositionScenario = FreeWVisualEvidencePlanner.ResolveScenario("table-page-composition-stress");
        tablePageCompositionScenario.ExpectedFeatureTags.Should().Contain([
            "table-pagination",
            "repeat-header-row",
            "keep-rows",
            "cell-borders",
            "table-fill-signatures",
            "style-derived-header-fill",
            "header-footer-fields",
            "page-border",
            "watermark",
            "caption",
            "footnotes"]);
        tablePageCompositionScenario.ExpectedOutputNamePattern.Should().Be("table-page-composition-stress_p{page}.png");
        tablePageCompositionScenario.MinimumExpectedOutputs.Should().Be(3);
        tablePageCompositionScenario.Composition.ExpectsTables.Should().BeTrue();
        tablePageCompositionScenario.Composition.ExpectsHeadersFooters.Should().BeTrue();
        tablePageCompositionScenario.Composition.ExpectsPageBorder.Should().BeTrue();
        tablePageCompositionScenario.Composition.ExpectsWatermark.Should().BeTrue();
        FreeWVisualEvidenceManifestNormalizer.TableRendererScenarioIds.Should().Contain("table-page-composition-stress");

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
            "grouped-child-wordart-effects",
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
        chartSmartArtScenario.ExpectedFeatureTags.Should().Contain([
            "chart-smartart",
            "chart-palette",
            "quick-layout",
            "scatter-markers",
            "chart-visual-signature",
            "chart-data",
            "smartart-style",
            "smartart-node-fills",
            "smartart-polygon-geometry",
            "smartart-visual-signature"]);
        chartSmartArtScenario.ExpectedOutputNamePattern.Should().Be("chart-smartart-complex_p{page}.png");
        chartSmartArtScenario.MinimumExpectedOutputs.Should().Be(2);

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
    public void SharedFloatingWrapFactory_BuildsWpfFloatWrapEvidenceContract()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildFloatingWrapEvidenceDocument();
        var floatingImages = document.Blocks
            .OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(r => r.Image is not null)
            .Select(r => r.Image!)
            .ToList();

        floatingImages.Should().HaveCount(2);
        floatingImages.Select(image => image.Wrapping).Should().BeEquivalentTo([
            ImageWrapping.Square,
            ImageWrapping.Tight
        ]);
        floatingImages.Should().OnlyContain(image =>
            image.VerticalAnchor == VerticalAnchor.Page &&
            image.HorizontalAnchor == HorizontalAnchor.Margin &&
            !string.IsNullOrWhiteSpace(image.AltText));

        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "f2-01-float-wrap",
            document.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "f2-01-float-wrap_p1.png",
            document: document);

        expectation.DrawingObjects.FloatingObjectCount.Should().Be(2);
        expectation.DrawingObjects.HasImages.Should().BeTrue();
        expectation.DrawingObjects.HasSquareWrap.Should().BeTrue();
        expectation.DrawingObjects.Objects.Should().Contain(o => o.Wrapping == ImageWrapping.Tight);
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
            pageNumber: 2,
            pageCount: 2,
            outputName: "f2-endnotes_p2.png",
            hasEndnotes: true,
            document: endnotes);
        endnoteExpectation.ExpectedOutputName.Should().Be("f2-endnotes_p2.png");
        endnoteExpectation.HasEndnotes.Should().BeTrue();
        endnoteExpectation.IsSyntheticPage.Should().BeFalse();
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
        DocumentNoteRegionPlanner.FootnoteSeparatorWidthDip.Should().Be(192.0);
        footnotePlan.SeparatorWidthDip.Should().Be(DocumentNoteRegionPlanner.FootnoteSeparatorWidthDip);
        footnotePlan.Rows.Should().ContainSingle();
        footnotePlan.Rows[0].Label.Should().Be("1");
        footnotePlan.Rows[0].Text.Should().Contain("bottom of page 1");
        footnotePlan.EstimatedHeightDip.Should().BeGreaterThan(0);

        var endnotePlan = DocumentNoteRegionPlanner.BuildEndnoteRegion(
            endnotes,
            DocumentNoteRegionPlanner.EndnoteIdsForSyntheticPage(endnotes),
            pageNumber: 2,
            contentWidth,
            isSyntheticPage: true);

        endnotePlan.Kind.Should().Be(DocumentNoteRegionKind.Endnotes);
        endnotePlan.IsSyntheticPage.Should().BeTrue();
        endnotePlan.Heading.Should().Be("Endnotes");
        endnotePlan.SeparatorWidthDip.Should().Be(DocumentNoteRegionPlanner.FootnoteSeparatorWidthDip);
        endnotePlan.Rows.Select(r => r.Label).Should().ContainInOrder("1", "2");
        endnotePlan.Rows.Select(r => r.Text).Should().Contain(t => t.Contains("very end of the document"));
    }

    [Fact]
    public void SharedNoteRegionPlanner_FragmentsLongFootnotesWithoutDroppingWords()
    {
        var document = new TextDocument();
        document.Footnotes[1] = new Footnote(1, string.Join(" ", Enumerable.Range(1, 80).Select(i => $"word{i}")));

        var plan = DocumentNoteRegionPlanner.BuildFootnoteContinuation(
            document,
            [1],
            firstPageNumber: 1,
            contentWidthDip: 192,
            firstAvailableHeightDip: 36,
            continuationAvailableHeightDip: 30);

        plan.Pages.Should().HaveCountGreaterThan(1);
        plan.Pages[0].SeparatorKind.Should().Be(DocumentFootnoteSeparatorKind.Initial);
        plan.Pages[1].SeparatorKind.Should().Be(DocumentFootnoteSeparatorKind.Continuation);
        plan.Pages.SelectMany(page => page.Fragments).Where(fragment => fragment.StartsNote)
            .Should().ContainSingle(fragment => fragment.Label == "1");
        string.Join(" ", plan.Pages.SelectMany(page => page.Fragments).Select(fragment => fragment.Text))
            .Should().Be(string.Join(" ", Enumerable.Range(1, 80).Select(i => $"word{i}")));
        plan.Pages[^1].Fragments[^1].EndsNote.Should().BeTrue();
    }

    [Fact]
    public void SharedNoteRegionPlanner_ConvertsContinuationFragmentToRendererPlan()
    {
        var page = new DocumentFootnoteContinuationPagePlan(
            PageNumber: 3,
            SeparatorKind: DocumentFootnoteSeparatorKind.Continuation,
            AvailableHeightDip: 120,
            EstimatedHeightDip: 84,
            Fragments: [
                new DocumentFootnoteContinuationFragment(1, 1, null, "continued words", false, false, 42),
                new DocumentFootnoteContinuationFragment(1, 1, null, "final words", false, true, 42)
            ]);

        var region = DocumentNoteRegionPlanner.BuildFootnoteContinuationRegion(page, contentWidthDip: 480);

        region.Kind.Should().Be(DocumentNoteRegionKind.Footnotes);
        region.PageNumber.Should().Be(3);
        region.IsSyntheticPage.Should().BeFalse();
        region.Heading.Should().BeNull();
        region.SeparatorWidthDip.Should().Be(DocumentNoteRegionPlanner.FootnoteSeparatorWidthDip);
        region.Rows.Select(row => row.Label).Should().Equal(string.Empty, string.Empty);
        region.Rows.Select(row => row.Text).Should().Equal("continued words", "final words");
        region.EstimatedHeightDip.Should().BeGreaterThanOrEqualTo(84);
    }

    [Fact]
    public void SharedNoteRegionPlanner_UsesDocumentWideFootnoteSequenceForLaterPageFragments()
    {
        var document = new TextDocument();
        document.Footnotes[1] = new Footnote(1, "First page footnote.");
        document.Footnotes[2] = new Footnote(2, "Later page footnote.");

        var plan = DocumentNoteRegionPlanner.BuildFootnoteContinuation(
            document,
            [2],
            firstPageNumber: 3,
            contentWidthDip: 240,
            firstAvailableHeightDip: 48,
            continuationAvailableHeightDip: 48);

        plan.Pages.SelectMany(page => page.Fragments)
            .Single(fragment => fragment.StartsNote)
            .Label.Should().Be("2");
    }

    [Fact]
    public void SharedNoteRegionPlanner_ResumesBodyBeforeFinalContinuationFragment()
    {
        var first = new DocumentFootnoteContinuationPlan([
            new DocumentFootnoteContinuationPagePlan(1, DocumentFootnoteSeparatorKind.Initial, 80, 80, []),
            new DocumentFootnoteContinuationPagePlan(2, DocumentFootnoteSeparatorKind.Continuation, 240, 240, []),
            new DocumentFootnoteContinuationPagePlan(3, DocumentFootnoteSeparatorKind.Continuation, 240, 240, [])
        ]);
        var last = new DocumentFootnoteContinuationPlan([
            new DocumentFootnoteContinuationPagePlan(4, DocumentFootnoteSeparatorKind.Initial, 80, 80, []),
            new DocumentFootnoteContinuationPagePlan(5, DocumentFootnoteSeparatorKind.Continuation, 240, 240, [])
        ]);

        var physical = DocumentNoteRegionPlanner.BuildFootnotePhysicalPagePlan(4, new Dictionary<int, DocumentFootnoteContinuationPlan>
        {
            [0] = first,
            [2] = last
        });

        physical.PhysicalPageCount.Should().Be(5);
        physical.PhysicalPageForBodyPage(0).Should().Be(0);
        physical.PhysicalPageForBodyPage(1).Should().Be(2);
        physical.PhysicalPageForBodyPage(2).Should().Be(3);
        physical.PhysicalPageForBodyPage(3).Should().Be(4);
        physical.Pages.Single(page => page.LogicalBodyPageIndex == 1)
            .FootnotePage!.SeparatorKind.Should().Be(DocumentFootnoteSeparatorKind.Continuation);
        physical.Pages.Single(page => page.LogicalBodyPageIndex == 3)
            .FootnotePage!.SeparatorKind.Should().Be(DocumentFootnoteSeparatorKind.Continuation);
        physical.Pages.Where(page => page.IsContinuationOnly)
            .Select(page => page.FootnotePage!.SeparatorKind)
            .Should().Equal(DocumentFootnoteSeparatorKind.Continuation);
    }

    [Fact]
    public void SharedReviewFactories_BuildF2ReviewContracts()
    {
        var tracked = FreeWVisualEvidenceDocumentFactory.BuildTrackedChangesReviewDocument();
        var comments = FreeWVisualEvidenceDocumentFactory.BuildCommentsReviewDocument();
        var reviewProofing = FreeWVisualEvidenceDocumentFactory.BuildReviewProofingVisualDepthDocument();
        var reviewProtection = FreeWVisualEvidenceDocumentFactory.BuildReviewProtectionProofingEvidenceDocument();
        var reviewCompare = FreeWVisualEvidenceDocumentFactory.BuildReviewCompareVisualProofDocument();
        var reviewCombine = FreeWVisualEvidenceDocumentFactory.BuildReviewCombineVisualProofDocument();

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

        var reviewEntries = RevisionList.Enumerate(reviewProofing);
        reviewEntries.Select(entry => entry.Kind).Should().Contain([
            RevisionEntryKind.Insertion,
            RevisionEntryKind.Deletion,
            RevisionEntryKind.Formatting]);
        reviewEntries.Select(entry => entry.Author).Should().Contain(["Maya", "Noah", "Priya"]);

        reviewProofing.Comments.Keys.Should().BeEquivalentTo([10, 12, 20]);
        reviewProofing.Comments[10].Resolved.Should().BeFalse();
        reviewProofing.Comments[10].Replies.Should().ContainSingle();
        reviewProofing.Comments[12].Resolved.Should().BeTrue();
        reviewProofing.Comments[12].Replies.Should().HaveCount(2);
        reviewProofing.Comments[20].Resolved.Should().BeTrue();
        reviewProofing.Comments[20].Replies.Should().ContainSingle();

        var commentReferenceIds = ParagraphsInDocument(reviewProofing)
            .SelectMany(p => p.Runs)
            .Where(r => r.IsCommentReference)
            .Select(r => r.CommentId!.Value)
            .ToList();
        commentReferenceIds.Should().ContainInOrder(10, 12, 20);

        var tableCommentIds = reviewProofing.Blocks
            .OfType<Table>()
            .SelectMany(t => t.Rows)
            .SelectMany(r => r.Cells)
            .SelectMany(c => c.Paragraphs)
            .SelectMany(p => p.Runs)
            .Where(r => r.CommentId is not null)
            .Select(r => r.CommentId!.Value)
            .ToList();
        tableCommentIds.Should().Contain(20);

        var proofingDiagnostics = ProofingDiagnosticPlanner.Build(reviewProofing, spellCheckEnabled: true);
        proofingDiagnostics.Select(diagnostic => diagnostic.NormalizedWord)
            .Should().Contain(["teh", "recieve", "acommodate", "the"]);
        proofingDiagnostics.Select(diagnostic => diagnostic.Kind)
            .Should().Contain([ProofingDiagnosticKind.Spelling, ProofingDiagnosticKind.Grammar]);
        proofingDiagnostics.Select(diagnostic => diagnostic.LanguageTag)
            .Should().Contain(["en-US", "en-GB", "fr-FR"]);

        var reviewProofingExpectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "review-proofing-visual-depth",
            reviewProofing.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "review-proofing-visual-depth_p1.png",
            document: reviewProofing);
        reviewProofingExpectation.ExpectedOutputName.Should().Be("review-proofing-visual-depth_p1.png");
        reviewProofingExpectation.Composition.ExpectsTrackedChanges.Should().BeTrue();
        reviewProofingExpectation.Composition.ExpectsComments.Should().BeTrue();
        reviewProofingExpectation.ProofingDiagnostics.DiagnosticCount.Should().Be(4);
        reviewProofingExpectation.ProofingDiagnostics.SpellingCount.Should().Be(3);
        reviewProofingExpectation.ProofingDiagnostics.GrammarCount.Should().Be(1);
        reviewProofingExpectation.ProofingDiagnostics.HasSpelling.Should().BeTrue();
        reviewProofingExpectation.ProofingDiagnostics.HasGrammar.Should().BeTrue();
        reviewProofingExpectation.ProofingDiagnostics.Kinds.Should().BeEquivalentTo(["Grammar", "Spelling"]);
        reviewProofingExpectation.ProofingDiagnostics.LanguageTags.Should().BeEquivalentTo(["en-GB", "en-US", "fr-FR"]);
        reviewProofingExpectation.ProofingDiagnostics.StableSignatures.Should().Contain([
            "kind=Spelling|word=teh|normalized=teh|language=en-US|block=5|run=1|runOffset=0|paragraphOffset=22|length=3",
            "kind=Spelling|word=recieve|normalized=recieve|language=en-GB|block=5|run=2|runOffset=0|paragraphOffset=26|length=7",
            "kind=Spelling|word=acommodate|normalized=acommodate|language=fr-FR|block=5|run=3|runOffset=0|paragraphOffset=34|length=10",
            "kind=Grammar|word=the|normalized=the|language=en-US|block=5|run=5|runOffset=0|paragraphOffset=49|length=3"
        ]);
        reviewProofingExpectation.ProofingDiagnostics.AdornmentCount.Should().Be(4);
        reviewProofingExpectation.ProofingDiagnostics.SpellingAdornmentCount.Should().Be(3);
        reviewProofingExpectation.ProofingDiagnostics.GrammarAdornmentCount.Should().Be(1);
        reviewProofingExpectation.ProofingDiagnostics.HasSpellingUnderline.Should().BeTrue();
        reviewProofingExpectation.ProofingDiagnostics.HasGrammarUnderline.Should().BeTrue();
        reviewProofingExpectation.ProofingDiagnostics.AdornmentStableSignatures.Should().Contain([
            "diagnostic=kind=Spelling|word=teh|normalized=teh|language=en-US|block=5|run=1|runOffset=0|paragraphOffset=22|length=3|adornment=spelling-squiggle|style=wavy|color=#D13438|block=5|run=1|runOffset=0|paragraphStart=22|paragraphEnd=25|length=3",
            "diagnostic=kind=Grammar|word=the|normalized=the|language=en-US|block=5|run=5|runOffset=0|paragraphOffset=49|length=3|adornment=grammar-squiggle|style=wavy|color=#2B579A|block=5|run=5|runOffset=0|paragraphStart=49|paragraphEnd=52|length=3"
        ]);
        reviewProofingExpectation.ProofingDiagnostics.Adornments.Should().Contain(adornment =>
            adornment.AdornmentKind == "spelling-squiggle"
            && adornment.UnderlineStyle == "wavy"
            && adornment.ColorHex == "#D13438"
            && adornment.ParagraphStartOffset == 22
            && adornment.ParagraphEndOffset == 25);
        reviewProofingExpectation.ProofingDiagnostics.Adornments.Should().Contain(adornment =>
            adornment.AdornmentKind == "grammar-squiggle"
            && adornment.UnderlineStyle == "wavy"
            && adornment.ColorHex == "#2B579A"
            && adornment.ParagraphStartOffset == 49
            && adornment.ParagraphEndOffset == 52);

        reviewProtection.Protection.Mode.Should().Be(ProtectionMode.CommentsOnly);
        reviewProtection.MarkedAsFinal.Should().BeTrue();
        var reviewProtectionExpectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "review-protection-proofing-comments-only",
            reviewProtection.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "review-protection-proofing-comments-only_p1.png",
            document: reviewProtection);
        reviewProtectionExpectation.ExpectedOutputName.Should().Be("review-protection-proofing-comments-only_p1.png");
        reviewProtectionExpectation.Composition.ExpectsTrackedChanges.Should().BeTrue();
        reviewProtectionExpectation.Composition.ExpectsComments.Should().BeTrue();
        reviewProtectionExpectation.ProofingDiagnostics.DiagnosticCount.Should().Be(4);
        reviewProtectionExpectation.ProofingDiagnostics.AdornmentCount.Should().Be(4);
        reviewProtectionExpectation.ReviewProtection.ProtectionMode.Should().Be(nameof(ProtectionMode.CommentsOnly));
        reviewProtectionExpectation.ReviewProtection.IsProtected.Should().BeTrue();
        reviewProtectionExpectation.ReviewProtection.IsMarkedAsFinal.Should().BeTrue();
        reviewProtectionExpectation.ReviewProtection.MarkAsFinal.IsChecked.Should().BeTrue();
        reviewProtectionExpectation.ReviewProtection.RestrictEditing.IsChecked.Should().BeTrue();
        reviewProtectionExpectation.ReviewProtection.IsBodyEditingLocked.Should().BeTrue();
        reviewProtectionExpectation.ReviewProtection.IsBodyFormattingLocked.Should().BeTrue();
        reviewProtectionExpectation.ReviewProtection.IsHistoryLocked.Should().BeTrue();
        reviewProtectionExpectation.ReviewProtection.IsCommentWorkflowAllowed.Should().BeFalse();
        reviewProtectionExpectation.ReviewProtection.Operations.Should().Contain(operation =>
            operation.Operation == nameof(RestrictEditingOperationKind.BodyTextEdit)
            && operation.MutationKind == "None"
            && !operation.IsAllowed
            && operation.BlockReason == nameof(RestrictEditingBlockReason.MarkedAsFinal));
        reviewProtectionExpectation.ReviewProtection.Operations.Should().Contain(operation =>
            operation.Operation == nameof(RestrictEditingOperationKind.BodyFormatting)
            && operation.MutationKind == "None"
            && !operation.IsAllowed
            && operation.BlockReason == nameof(RestrictEditingBlockReason.MarkedAsFinal));
        reviewProtectionExpectation.ReviewProtection.Operations.Should().Contain(operation =>
            operation.Operation == nameof(RestrictEditingOperationKind.ProofingReplacement)
            && operation.MutationKind == "None"
            && !operation.IsAllowed
            && operation.BlockReason == nameof(RestrictEditingBlockReason.MarkedAsFinal));
        reviewProtectionExpectation.ReviewProtection.Operations.Should().Contain(operation =>
            operation.Operation == nameof(RestrictEditingOperationKind.HistoryUndo)
            && operation.MutationKind == nameof(DocumentCommandMutationKind.BodyText)
            && !operation.IsAllowed
            && operation.BlockReason == nameof(RestrictEditingBlockReason.MarkedAsFinal));
        reviewProtectionExpectation.ReviewProtection.Operations.Should().Contain(operation =>
            operation.Operation == nameof(RestrictEditingOperationKind.HistoryRedo)
            && operation.MutationKind == nameof(DocumentCommandMutationKind.BodyFormatting)
            && !operation.IsAllowed
            && operation.BlockReason == nameof(RestrictEditingBlockReason.MarkedAsFinal));
        reviewProtectionExpectation.ReviewProtection.Operations.Should().Contain(operation =>
            operation.Operation == nameof(RestrictEditingOperationKind.CommentInsert)
            && !operation.IsAllowed
            && operation.BlockReason == nameof(RestrictEditingBlockReason.MarkedAsFinal));
        reviewProtectionExpectation.ReviewProtection.Operations.Should().Contain(operation =>
            operation.Operation == nameof(RestrictEditingOperationKind.CommentReply)
            && !operation.IsAllowed
            && operation.BlockReason == nameof(RestrictEditingBlockReason.MarkedAsFinal));
        reviewProtectionExpectation.ReviewProtection.Operations.Should().Contain(operation =>
            operation.Operation == nameof(RestrictEditingOperationKind.CommentResolve)
            && !operation.IsAllowed
            && operation.BlockReason == nameof(RestrictEditingBlockReason.MarkedAsFinal));
        reviewProtectionExpectation.ReviewProtection.Operations.Should().Contain(operation =>
            operation.Operation == nameof(RestrictEditingOperationKind.CommentDelete)
            && !operation.IsAllowed
            && operation.BlockReason == nameof(RestrictEditingBlockReason.MarkedAsFinal));
        reviewProtectionExpectation.ReviewProtection.Operations.Should().Contain(operation =>
            operation.Operation == nameof(RestrictEditingOperationKind.HistoryUndo)
            && operation.MutationKind == nameof(DocumentCommandMutationKind.Comment)
            && !operation.IsAllowed
            && operation.BlockReason == nameof(RestrictEditingBlockReason.MarkedAsFinal));
        reviewProtectionExpectation.ReviewProtection.StableSignatures.Should().Contain([
            "operation=BodyTextEdit|mutation=None|allowed=0|requiresTrackedChanges=0|blockReason=MarkedAsFinal|protection=CommentsOnly",
            "operation=BodyFormatting|mutation=None|allowed=0|requiresTrackedChanges=0|blockReason=MarkedAsFinal|protection=CommentsOnly",
            "operation=ProofingReplacement|mutation=None|allowed=0|requiresTrackedChanges=0|blockReason=MarkedAsFinal|protection=CommentsOnly",
            "operation=HistoryUndo|mutation=BodyText|allowed=0|requiresTrackedChanges=0|blockReason=MarkedAsFinal|protection=CommentsOnly",
            "operation=CommentInsert|mutation=None|allowed=0|requiresTrackedChanges=0|blockReason=MarkedAsFinal|protection=CommentsOnly"
        ]);

        var compareEntries = RevisionList.Enumerate(reviewCompare);
        compareEntries.Should().NotBeEmpty();
        compareEntries.Select(entry => entry.Kind).Should().Contain([RevisionEntryKind.Insertion, RevisionEntryKind.Deletion]);
        compareEntries.Select(entry => entry.Author).Should().OnlyContain(author => author == "Riley");
        var reviewCompareExpectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "review-compare-visual-proof",
            reviewCompare.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "review-compare-visual-proof_p1.png",
            document: reviewCompare);
        reviewCompareExpectation.ExpectedOutputName.Should().Be("review-compare-visual-proof_p1.png");
        reviewCompareExpectation.Composition.ExpectsTrackedChanges.Should().BeTrue();
        reviewCompareExpectation.ReviewCompareCombine.Operation.Should().Be("compare");
        reviewCompareExpectation.ReviewCompareCombine.HasCompareSemantics.Should().BeTrue();
        reviewCompareExpectation.ReviewCompareCombine.AuthorCount.Should().Be(1);
        reviewCompareExpectation.ReviewCompareCombine.Authors.Should().Contain("Riley");
        reviewCompareExpectation.ReviewCompareCombine.InsertionCount.Should().BeGreaterThan(0);
        reviewCompareExpectation.ReviewCompareCombine.DeletionCount.Should().BeGreaterThan(0);
        reviewCompareExpectation.ReviewCompareCombine.StableSignatures.Should()
            .Contain(signature => signature.Contains("operation=compare", StringComparison.Ordinal)
                && signature.Contains("author=Riley", StringComparison.Ordinal));
        reviewCompareExpectation.ReviewCompareCombine.HasRetainedModelSafety.Should().BeTrue();
        reviewCompareExpectation.ReviewCompareCombine.HasPreservedSettings.Should().BeTrue();
        reviewCompareExpectation.ReviewCompareCombine.HasPreservedCustomProperties.Should().BeTrue();
        reviewCompareExpectation.ReviewCompareCombine.PreservedPartCount.Should().Be(1);
        reviewCompareExpectation.ReviewCompareCombine.PreservedContentTypeDefaultCount.Should().Be(0);
        reviewCompareExpectation.ReviewCompareCombine.RetainedModelSafetySignatures.Should().Contain([
            "operation=compare|preserved=settings",
            "operation=compare|preserved=custom-properties",
            "operation=compare|preserved=part:/customXml/freew-review-safety.xml"
        ]);

        var combineEntries = RevisionList.Enumerate(reviewCombine);
        combineEntries.Should().NotBeEmpty();
        combineEntries.Select(entry => entry.Kind).Should().Contain([RevisionEntryKind.Insertion, RevisionEntryKind.Deletion]);
        combineEntries.Select(entry => entry.Author).Should().Contain(["Alice", "Bob"]);
        var reviewCombineExpectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "review-combine-visual-proof",
            reviewCombine.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "review-combine-visual-proof_p1.png",
            document: reviewCombine);
        reviewCombineExpectation.ExpectedOutputName.Should().Be("review-combine-visual-proof_p1.png");
        reviewCombineExpectation.Composition.ExpectsTrackedChanges.Should().BeTrue();
        reviewCombineExpectation.ReviewCompareCombine.Operation.Should().Be("combine");
        reviewCombineExpectation.ReviewCompareCombine.HasCombineSemantics.Should().BeTrue();
        reviewCombineExpectation.ReviewCompareCombine.AuthorCount.Should().BeGreaterThanOrEqualTo(2);
        reviewCombineExpectation.ReviewCompareCombine.Authors.Should().Contain(["Alice", "Bob"]);
        reviewCombineExpectation.ReviewCompareCombine.InsertionCount.Should().BeGreaterThan(0);
        reviewCombineExpectation.ReviewCompareCombine.DeletionCount.Should().BeGreaterThan(0);
        reviewCombineExpectation.ReviewCompareCombine.StableSignatures.Should()
            .Contain(signature => signature.Contains("operation=combine", StringComparison.Ordinal)
                && signature.Contains("author=Alice", StringComparison.Ordinal));
        reviewCombineExpectation.ReviewCompareCombine.StableSignatures.Should()
            .Contain(signature => signature.Contains("operation=combine", StringComparison.Ordinal)
                && signature.Contains("author=Bob", StringComparison.Ordinal));
        reviewCombineExpectation.ReviewCompareCombine.HasRetainedModelSafety.Should().BeTrue();
        reviewCombineExpectation.ReviewCompareCombine.HasPreservedSettings.Should().BeTrue();
        reviewCombineExpectation.ReviewCompareCombine.HasPreservedCustomProperties.Should().BeTrue();
        reviewCombineExpectation.ReviewCompareCombine.PreservedPartCount.Should().Be(1);
        reviewCombineExpectation.ReviewCompareCombine.PreservedContentTypeDefaultCount.Should().Be(0);
        reviewCombineExpectation.ReviewCompareCombine.RetainedModelSafetySignatures.Should().Contain([
            "operation=combine|preserved=settings",
            "operation=combine|preserved=custom-properties",
            "operation=combine|preserved=part:/customXml/freew-review-safety.xml"
        ]);
    }

    [Fact]
    public void SharedFieldPageNumberFactory_BuildsFieldVariantContracts()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument();

        document.Page.DifferentFirstPage.Should().BeTrue();
        document.Page.DifferentOddEvenPages.Should().BeTrue();
        document.Page.PageNumberFormat.Should().Be(PageNumberFormat.Decimal);
        document.Page.PageNumberStartAt.Should().Be(1);
        document.Page.PageNumberChapterStyleLevel.Should().Be(1);
        document.Page.PageNumberChapterSeparator.Should().Be(PageNumberChapterSeparator.Hyphen);
        document.Blocks.OfType<Paragraph>().First().Formatting.ListKind.Should().Be(ListKind.MultiLevel);
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
        fields.ComplexFieldResultSignatures.Should().Contain([
            "AUTHOR=FreeW Visual Evidence",
            "NUMPAGES=4",
            "PAGE=1",
            "TITLE=Field Page Number Evidence"]);

        var pageExpectations = Enumerable.Range(1, 4)
            .Select(page => FreeWVisualEvidencePlanner.BuildPageExpectation(
                "field-page-number-variants",
                document.Page,
                page,
                pageCount: 4,
                outputName: $"field-page-number-variants_p{page}.png",
                document: document))
            .ToList();

        pageExpectations.SelectMany(expectation => expectation.Fields.HeaderFooterResolvedFieldSignatures)
            .Where(signature => signature.Contains("field=PAGE", StringComparison.Ordinal))
            .Should().Contain([
                "slot=first-header|page=1|section=1|sectionPage=1|paragraph=0|run=1|field=PAGE|text=1-1",
                "slot=even-header|page=2|section=1|sectionPage=2|paragraph=0|run=1|field=PAGE|text=1-2",
                "slot=header|page=3|section=1|sectionPage=3|paragraph=0|run=1|field=PAGE|text=1-3",
                "slot=even-header|page=4|section=1|sectionPage=4|paragraph=0|run=1|field=PAGE|text=1-4"]);
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
        fields.ComplexFieldResultSignatures.Should().Contain([
            "BIBLIOGRAPHY=References",
            "TOA=Cases\\t1, 2"]);
        var toa = FreeWVisualEvidencePlanner.BuildTableOfAuthoritiesExpectation(document);
        toa.EntryCount.Should().Be(2);
        toa.EntryWithPageReferenceCount.Should().Be(2);
        toa.CategoryCount.Should().Be(2);
        toa.Categories.Should().BeEquivalentTo(["Cases", "Statutes"]);
        toa.HasGeneratedTable.Should().BeTrue();
        toa.HasPageReferences.Should().BeTrue();
        toa.HasExplicitPageNumbers.Should().BeTrue();
        toa.PageReferenceSignatures.Should().BeEquivalentTo([
            "category=Cases|entry=Example v. FreeW, 123 F.4th 456 (2026)|kind=explicit-page-numbers|pages=1,2|text=1, 2",
            "category=Statutes|entry=Free Software Evidence Act, 42 U.S.C. 2026|kind=explicit-page-numbers|pages=1|text=1"
        ]);
        var caseToa = toa.PageReferences.Should().ContainSingle(reference =>
            reference.Category == "Cases"
            && reference.EntryText == "Example v. FreeW, 123 F.4th 456 (2026)"
            && reference.PageReferenceText == "1, 2").Subject;
        caseToa.PageNumbers.Should().Equal(1, 2);
        caseToa.DisplayedPageReferences.Should().Equal("1", "2");
        caseToa.PageReferenceKind.Should().Be("explicit-page-numbers");
        caseToa.HasPageReferenceSentinel.Should().BeTrue();
        caseToa.StableSignature.Should().Be(
            "category=Cases|entry=Example v. FreeW, 123 F.4th 456 (2026)|kind=explicit-page-numbers|pages=1,2|text=1, 2");
        var statuteToa = toa.PageReferences.Should().ContainSingle(reference =>
            reference.Category == "Statutes"
            && reference.EntryText == "Free Software Evidence Act, 42 U.S.C. 2026"
            && reference.PageReferenceText == "1").Subject;
        statuteToa.PageNumbers.Should().Equal(1);
        statuteToa.PageReferenceKind.Should().Be("explicit-page-numbers");
        statuteToa.HasPageReferenceSentinel.Should().BeTrue();
        statuteToa.StableSignature.Should().Be(
            "category=Statutes|entry=Free Software Evidence Act, 42 U.S.C. 2026|kind=explicit-page-numbers|pages=1|text=1");

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
        expectation.TableOfAuthorities.PageReferenceSignatures.Should().BeEquivalentTo(toa.PageReferenceSignatures);
    }

    [Fact]
    public void SharedLegalReferenceFactory_BuildsSectionFormattedToaPageReferenceEvidence()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildLegalReferenceSectionPageNumbersDocument();

        document.Sections.Should().HaveCount(2);
        document.Sections[0].Page.PageNumberFormat.Should().Be(PageNumberFormat.LowerRoman);
        document.Sections[0].Page.PageNumberStartAt.Should().Be(1);
        document.Page.PageNumberFormat.Should().Be(PageNumberFormat.Decimal);
        document.Page.PageNumberStartAt.Should().Be(1);
        document.Blocks.OfType<Paragraph>()
            .Should().Contain(p => TableOfAuthorities.IsTableOfAuthoritiesParagraph(p));
        document.Blocks.OfType<Paragraph>()
            .Where(p => p.StyleId == TableOfAuthorities.EntryStyleId)
            .Select(p => p.PlainText)
            .Should().Contain([
                "Matter of Sectioned Pages, 101 F. Supp. 3d 2026 (D. FreeW)\ti, 1",
                "Restart Numbering Act, 7 FreeW Code 13\t1"
            ]);
        document.Blocks.OfType<Paragraph>()
            .Count(p => p.PlainText.StartsWith("Main section reference body paragraph ", StringComparison.Ordinal))
            .Should().BeGreaterThanOrEqualTo(18);

        var pageReferences = PageNumberFormatDialogPlanner.BuildCitationPageReferencePlans(document);
        pageReferences.Should().Contain(reference =>
            reference.PhysicalPageNumber == 1
            && reference.SectionRelativePageNumber == 1
            && reference.DisplayText == "i");
        pageReferences.Should().Contain(reference =>
            reference.PhysicalPageNumber == 2
            && reference.SectionRelativePageNumber == 1
            && reference.DisplayText == "1");

        var fields = FreeWVisualEvidencePlanner.BuildFieldExpectation(document);
        fields.ComplexFieldKeywords.Should().Contain("TOA");
        fields.ComplexFieldResultSignatures.Should().Contain("TOA=Cases\\ti, 1");

        var toa = FreeWVisualEvidencePlanner.BuildTableOfAuthoritiesExpectation(document);
        toa.EntryCount.Should().Be(2);
        toa.EntryWithPageReferenceCount.Should().Be(2);
        toa.HasGeneratedTable.Should().BeTrue();
        toa.HasPageReferences.Should().BeTrue();
        toa.HasExplicitPageNumbers.Should().BeTrue();
        toa.PageReferenceSignatures.Should().BeEquivalentTo([
            "category=Cases|entry=Matter of Sectioned Pages, 101 F. Supp. 3d 2026 (D. FreeW)|kind=section-formatted-page-numbers|pages=1,2|text=i, 1",
            "category=Statutes|entry=Restart Numbering Act, 7 FreeW Code 13|kind=explicit-page-numbers|pages=2|text=1"
        ]);

        var caseToa = toa.PageReferences.Should().ContainSingle(reference =>
            reference.Category == "Cases"
            && reference.EntryText == "Matter of Sectioned Pages, 101 F. Supp. 3d 2026 (D. FreeW)").Subject;
        caseToa.PageReferenceText.Should().Be("i, 1");
        caseToa.PageNumbers.Should().Equal(1, 2);
        caseToa.DisplayedPageReferences.Should().Equal("1", "i");
        caseToa.PageReferenceKind.Should().Be("section-formatted-page-numbers");
        caseToa.HasPageReferenceSentinel.Should().BeTrue();

        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "legal-reference-section-page-numbers",
            document.Page,
            pageNumber: 2,
            pageCount: 2,
            outputName: "legal-reference-section-page-numbers_p2.png",
            document: document);
        expectation.ExpectedOutputName.Should().Be("legal-reference-section-page-numbers_p2.png");
        expectation.TableOfAuthorities.PageReferenceSignatures.Should().BeEquivalentTo(toa.PageReferenceSignatures);
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
        document.Sections[0].Page.PageNumberFormat = PageNumberFormat.LowerRoman;
        document.Sections[0].Page.PageNumberStartAt = 3;
        document.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        document.Page.PageNumberStartAt = 10;

        var surfacePlans = FreeWVisualEvidencePlanner.BuildSectionGeometrySurfacePlans(document, pageCount: 2);

        surfacePlans.Should().HaveCount(2);
        surfacePlans[0].RenderStatus.Should().Be(FreeWVisualEvidencePlanner.SectionGeometryPageSurfaceRenderStatus);
        surfacePlans[0].Orientation.Should().Be("portrait");
        surfacePlans[0].SourceBlockIndexes.Should().Equal(0, 1, 2, 3, 4, 5, 6);
        surfacePlans[0].Document.Page.WidthPt.Should().Be(612);
        surfacePlans[0].Document.Page.HeightPt.Should().Be(792);
        surfacePlans[0].Document.Page.Landscape.Should().BeFalse();
        surfacePlans[0].Document.Page.PageNumberFormat.Should().Be(PageNumberFormat.LowerRoman);
        surfacePlans[0].Document.Page.PageNumberStartAt.Should().Be(3);
        surfacePlans[0].CaptureWidthDip.Should().BeApproximately(864, 0.01);
        surfacePlans[0].CaptureHeightDip.Should().BeApproximately(1104, 0.01);

        surfacePlans[1].Orientation.Should().Be("landscape");
        surfacePlans[1].SourceBlockIndexes.Should().Equal(7, 8, 9, 10, 11, 12);
        surfacePlans[1].Document.Page.WidthPt.Should().Be(792);
        surfacePlans[1].Document.Page.HeightPt.Should().Be(612);
        surfacePlans[1].Document.Page.Landscape.Should().BeTrue();
        surfacePlans[1].Document.Page.PageNumberFormat.Should().Be(PageNumberFormat.UpperRoman);
        surfacePlans[1].Document.Page.PageNumberStartAt.Should().Be(10);
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
    public void EquationStructuresDocument_CoversModeledEquationKinds()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildEquationStructuresDocument();

        var equations = document.Blocks
            .OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.Equation is not null)
            .Select(run => run.Equation!)
            .ToList();
        var kinds = equations
            .SelectMany(equation => equation.Runs)
            .Select(run => run.Kind)
            .Distinct()
            .ToList();

        equations.Should().HaveCountGreaterThanOrEqualTo(7);
        kinds.Should().Contain([
            MathRunKind.Text,
            MathRunKind.Superscript,
            MathRunKind.Subscript,
            MathRunKind.SubSuperscript,
            MathRunKind.Fraction,
            MathRunKind.Radical,
            MathRunKind.NAry,
            MathRunKind.Accent,
            MathRunKind.Bar,
            MathRunKind.Delimiter,
            MathRunKind.Matrix,
            MathRunKind.EquationArray,
            MathRunKind.FunctionApply,
            MathRunKind.GroupChar]);
    }

    [Fact]
    public void EquationStructuresExpectation_IncludesSharedGeometryContract()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildEquationStructuresDocument();

        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "equation-structures",
            document.Page,
            pageNumber: 1,
            pageCount: 1,
            outputName: "equation-structures_p1.png",
            document: document);

        var equations = expectation.Equations;
        equations.EquationCount.Should().BeGreaterThanOrEqualTo(8);
        equations.ElementKindCounts.Should().Contain([
            "EquationArray=1",
            "Fraction=1",
            "FunctionApply=2",
            "Matrix=1",
            "NAry=2",
            "Radical=1"]);
        equations.SegmentRoleCounts.Should().Contain([
            "FractionBar=1",
            "MatrixCell=6",
            "NAryLowerLimit=2",
            "RadicalDegree=1",
            "Superscript=2"]);
        equations.BaselineRoleCounts.Should().Contain([
            "Normal=51",
            "Subscript=4",
            "Superscript=5"]);
        equations.ElementGeometrySignatures.Should().Contain(signature =>
            signature.Contains("geometry=fraction", StringComparison.Ordinal)
            && signature.Contains("stackGapEm=0.12", StringComparison.Ordinal)
            && signature.Contains("barThicknessEm=0.05", StringComparison.Ordinal));
        equations.ElementGeometrySignatures.Should().Contain(signature =>
            signature.Contains("geometry=radical", StringComparison.Ordinal)
            && signature.Contains("degree=3", StringComparison.Ordinal)
            && signature.Contains("radicand=x + 1", StringComparison.Ordinal));
        equations.ElementGeometrySignatures.Should().Contain(signature =>
            signature.Contains("geometry=nary", StringComparison.Ordinal)
            && signature.Contains("operator=\u2211", StringComparison.Ordinal)
            && signature.Contains("lower=i=1", StringComparison.Ordinal)
            && signature.Contains("upper=n", StringComparison.Ordinal));
        equations.ElementGeometrySignatures.Should().Contain(signature =>
            signature.Contains("geometry=matrix", StringComparison.Ordinal)
            && signature.Contains("rows=2", StringComparison.Ordinal)
            && signature.Contains("columns=2", StringComparison.Ordinal)
            && signature.Contains("cells=4", StringComparison.Ordinal));
        equations.ElementGeometrySignatures.Should().Contain(signature =>
            signature.Contains("geometry=equationarray", StringComparison.Ordinal)
            && signature.Contains("rows=2", StringComparison.Ordinal)
            && signature.Contains("cells=2", StringComparison.Ordinal));
        equations.ElementGeometrySignatures.Should().Contain(signature =>
            signature.Contains("geometry=accent", StringComparison.Ordinal)
            && signature.Contains("mark=^", StringComparison.Ordinal));
        equations.ElementGeometrySignatures.Should().Contain(signature =>
            signature.Contains("geometry=delimiter", StringComparison.Ordinal)
            && signature.Contains("open=[", StringComparison.Ordinal)
            && signature.Contains("close=]", StringComparison.Ordinal));
        equations.ElementGeometrySignatures.Should().Contain(signature =>
            signature.Contains("geometry=function-apply", StringComparison.Ordinal)
            && signature.Contains("name=sin", StringComparison.Ordinal)
            && signature.Contains("argument=x + y", StringComparison.Ordinal));
        equations.SpacingGeometrySignatures.Should().Contain(signature =>
            signature.Contains("spacing=script", StringComparison.Ordinal)
            && signature.Contains("hasSuperscript=1", StringComparison.Ordinal)
            && signature.Contains("horizontalGapEm=0.06", StringComparison.Ordinal));
        equations.SpacingGeometrySignatures.Should().Contain(signature =>
            signature.Contains("spacing=fraction", StringComparison.Ordinal)
            && signature.Contains("layout=vertical-stack", StringComparison.Ordinal)
            && signature.Contains("stackGapEm=0.12", StringComparison.Ordinal)
            && signature.Contains("barOverhangEm=0.08", StringComparison.Ordinal));
        equations.SpacingGeometrySignatures.Should().Contain(signature =>
            signature.Contains("spacing=radical", StringComparison.Ordinal)
            && signature.Contains("degreePresent=1", StringComparison.Ordinal)
            && signature.Contains("overbarClearanceEm=0.06", StringComparison.Ordinal));
        equations.SpacingGeometrySignatures.Should().Contain(signature =>
            signature.Contains("spacing=nary", StringComparison.Ordinal)
            && signature.Contains("limitPlacement=above-below", StringComparison.Ordinal)
            && signature.Contains("operandGapEm=0.16", StringComparison.Ordinal));
        equations.SpacingGeometrySignatures.Should().Contain(signature =>
            signature.Contains("spacing=matrix", StringComparison.Ordinal)
            && signature.Contains("rowGapEm=0.08", StringComparison.Ordinal)
            && signature.Contains("columnGapEm=0.85", StringComparison.Ordinal)
            && signature.Contains("delimiterGapEm=0.12", StringComparison.Ordinal));
        equations.SpacingGeometrySignatures.Should().Contain(signature =>
            signature.Contains("spacing=equationarray", StringComparison.Ordinal)
            && signature.Contains("rowGapEm=0.08", StringComparison.Ordinal)
            && signature.Contains("delimiterGapEm=0", StringComparison.Ordinal));
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
    public void NotePlacementVisualProofScenarioIds_CoverFocusedNoWordProofFamily()
    {
        FreeWVisualEvidenceManifestNormalizer.NotePlacementVisualProofScenarioIds.Should().Equal(
            "f2-footnotes",
            "f2-endnotes");

        FreeWVisualEvidenceManifestNormalizer.NotePlacementVisualProofScenarioIds.Should().OnlyContain(
            scenarioId => FreeWVisualEvidenceManifestNormalizer.NoteRendererScenarioIds.Contains(
                scenarioId,
                StringComparer.OrdinalIgnoreCase));
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
    public void SectionGeometryVisualProofScenarioIds_CoverFocusedNoWordProofFamily()
    {
        FreeWVisualEvidenceManifestNormalizer.SectionGeometryVisualProofScenarioIds.Should().Equal(
            "f2-section-landscape");

        FreeWVisualEvidenceManifestNormalizer.SectionGeometryVisualProofScenarioIds.Should().OnlyContain(
            scenarioId => FreeWVisualEvidenceManifestNormalizer.SectionGeometryRendererScenarioIds.Contains(
                scenarioId,
                StringComparer.OrdinalIgnoreCase));
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
    public void DefaultExpectedScenarios_RequiresPairedEquationRendererEvidence()
    {
        var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;

        foreach (var scenarioId in FreeWVisualEvidenceManifestNormalizer.EquationRendererScenarioIds)
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
    public void DefaultExpectedScenarios_RequiresPairedHeaderFooterImageEvidence()
    {
        var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;
        var imageScenarioIds = FreeWVisualEvidenceManifestNormalizer.HeaderFooterRendererScenarioIds
            .Where(scenarioId => FreeWVisualEvidencePlanner
                .ResolveScenario(scenarioId)
                .ExpectedFeatureTags
                .Contains("header-footer-images", StringComparer.OrdinalIgnoreCase));

        foreach (var scenarioId in imageScenarioIds)
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
    public void HeaderFooterImageVisualProofScenarioIds_CoverFocusedNoWordProofFamily()
    {
        FreeWVisualEvidenceManifestNormalizer.HeaderFooterImageVisualProofScenarioIds.Should().Equal(
        [
            "f2-hf-images"
        ]);

        FreeWVisualEvidenceManifestNormalizer.HeaderFooterImageVisualProofScenarioIds.Should().OnlyContain(
            scenarioId => FreeWVisualEvidenceManifestNormalizer.HeaderFooterRendererScenarioIds.Contains(
                scenarioId,
                StringComparer.OrdinalIgnoreCase));
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
                e.MinimumExpectedOutputs == FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
        }

        FreeWVisualEvidenceManifestNormalizer.DrawingObjectVisualProofScenarioIds.Should().Contain([
            "drawing-objects-complex",
            "object-format-position-size-style",
            "chart-smartart-complex",
            "wordart-watermark-stress",
            "wordart-picture-watermark-layout"]);
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
                e.MinimumExpectedOutputs == 2);
            expected.Should().Contain(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                e.ScenarioId == scenarioId &&
                e.MinimumExpectedOutputs == 2);
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
    public void WordArtWatermarkVisualProofScenarioIds_CoverFocusedNoWordProofFamily()
    {
        FreeWVisualEvidenceManifestNormalizer.WordArtWatermarkVisualProofScenarioIds.Should().Equal(
        [
            "wordart-watermark-stress",
            "wordart-picture-watermark-layout"
        ]);

        FreeWVisualEvidenceManifestNormalizer.WordArtWatermarkVisualProofScenarioIds.Should().OnlyContain(
            scenarioId => FreeWVisualEvidenceManifestNormalizer.DrawingObjectVisualProofScenarioIds.Contains(
                scenarioId,
                StringComparer.OrdinalIgnoreCase));
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
            plan.ExpectedFixtureCount.Should().Be(30);
            plan.ExpectedBaselinePngCount.Should().Be(90);
            plan.Fixtures.Select(f => f.DocumentName).Should().Contain([
                "f2-hf-basic.docx",
                "f2-hf-images.docx",
                "f2-01-float-wrap.docx",
                "field-page-number-variants.docx",
                "references-heavy-fields.docx",
                "legal-reference-section-page-numbers.docx",
                "equation-structures.docx",
                "review-proofing-visual-depth.docx",
                "review-protection-proofing-comments-only.docx",
                "review-compare-visual-proof.docx",
                "review-combine-visual-proof.docx",
                "table-layout-complex.docx",
                "table-pagination-repeat-header.docx",
                "table-page-composition-stress.docx",
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
            plan.Fixtures.Single(f => f.ScenarioId == "table-page-composition-stress")
                .ExpectedBaselinePaths.Should().Contain("table-page-composition-stress/table-page-composition-stress_p2.png");
            plan.Fixtures.Single(f => f.ScenarioId == "table-page-composition-stress")
                .ExpectedBaselinePaths.Should().Contain("table-page-composition-stress/table-page-composition-stress_p3.png");
            plan.Fixtures.Single(f => f.ScenarioId == "field-page-number-variants")
                .ExpectedBaselinePaths.Should().Contain("field-page-number-variants/field-page-number-variants_p1.png");
            plan.Fixtures.Single(f => f.ScenarioId == "references-heavy-fields")
                .ExpectedBaselinePaths.Should().Contain("references-heavy-fields/references-heavy-fields_p2.png");
            plan.Fixtures.Single(f => f.ScenarioId == "legal-reference-section-page-numbers")
                .ExpectedBaselinePaths.Should().Contain("legal-reference-section-page-numbers/legal-reference-section-page-numbers_p2.png");
            plan.Fixtures.Single(f => f.ScenarioId == "equation-structures")
                .ExpectedBaselinePaths.Should().Contain("equation-structures/equation-structures_p1.png");
            plan.Fixtures.Single(f => f.ScenarioId == "f2-hf-images")
                .ExpectedBaselinePaths.Should().Contain("f2-hf-images/f2-hf-images_p2.png");
            plan.Fixtures.Single(f => f.ScenarioId == "f2-01-float-wrap")
                .ExpectedBaselinePaths.Should().Contain("f2-01-float-wrap/f2-01-float-wrap_p1.png");
            plan.Fixtures.Single(f => f.ScenarioId == "review-proofing-visual-depth")
                .ExpectedBaselinePaths.Should().Contain("review-proofing-visual-depth/review-proofing-visual-depth_p1.png");
            plan.Fixtures.Single(f => f.ScenarioId == "review-protection-proofing-comments-only")
                .ExpectedBaselinePaths.Should().Contain("review-protection-proofing-comments-only/review-protection-proofing-comments-only_p1.png");
            plan.Fixtures.Single(f => f.ScenarioId == "review-compare-visual-proof")
                .ExpectedBaselinePaths.Should().Contain("review-compare-visual-proof/review-compare-visual-proof_p1.png");
            plan.Fixtures.Single(f => f.ScenarioId == "review-combine-visual-proof")
                .ExpectedBaselinePaths.Should().Contain("review-combine-visual-proof/review-combine-visual-proof_p1.png");
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
        source.Should().Contain("[switch]$UseVisibleWordPublish");
        source.Should().Contain("Export-WordPdfsVisible.ps1");
        source.Should().Contain("-WordApplicationProgId");
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
            pageCount: 4,
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
        expectation.HeaderFooters.Slots.Single(slot => slot.SlotName == "even-header")
            .Lines.Single().Text.Should().Be("Even header page 1-2 of 4");
        expectation.Fields.HeaderFooterResolvedFieldSignatures.Should().Contain([
            "slot=even-header|page=2|section=1|sectionPage=2|paragraph=0|run=1|field=PAGE|text=1-2",
            "slot=even-header|page=2|section=1|sectionPage=2|paragraph=0|run=3|field=NUMPAGES|text=4"]);
    }

    [Fact]
    public void BuildPageExpectation_RecordsSharedHeaderFooterImageEvidence()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildMultiSectionHeaderFooterImageDocument();

        var page1 = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "f2-hf-images",
            document.Page,
            pageNumber: 1,
            pageCount: 2,
            outputName: "f2-hf-images_p1.png",
            headerSlotName: "header",
            document: document);
        var page2 = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "f2-hf-images",
            document.Page,
            pageNumber: 2,
            pageCount: 2,
            outputName: "f2-hf-images_p2.png",
            headerSlotName: "header",
            document: document);

        page1.HeaderFooters.HasImages.Should().BeTrue();
        page1.HeaderFooters.ImageCount.Should().Be(1);
        page1.HeaderFooters.SlotNames.Should().Contain("header");
        page1.HeaderFooters.ImageSignatures.Single().Should().Contain("section=1");
        page1.HeaderFooters.ImageSignatures.Single().Should().Contain("slot=header");
        page1.HeaderFooters.ImageSignatures.Single().Should().Contain("alt=Section One Letterhead");
        page1.HeaderFooters.Slots.Single().Lines.Single().Runs
            .Should().Contain(run => run.Kind == HeaderFooterVisualPlanner.ImageRunKind
                && run.WidthDip > 0
                && run.HeightDip > 0);

        page2.HeaderFooters.ImageCount.Should().Be(1);
        page2.HeaderFooters.ImageSignatures.Single().Should().Contain("section=2");
        page2.HeaderFooters.ImageSignatures.Single().Should().Contain("align=Right");
        page2.HeaderFooters.ImageSignatures.Single().Should().Contain("alt=Section Two Letterhead");
    }

    [Fact]
    public void BuildSectionGeometrySurfacePlans_PreserveSelectedSectionHeaderFooterImages()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildMultiSectionHeaderFooterImageDocument();

        var surfaces = FreeWVisualEvidencePlanner.BuildSectionGeometrySurfacePlans(document, pageCount: 2);

        surfaces.Should().HaveCount(2);
        surfaces[0].PagePlan.SectionOrdinal.Should().Be(1);
        surfaces[1].PagePlan.SectionOrdinal.Should().Be(2);

        var page1HeaderFooters = FreeWVisualEvidencePlanner.BuildHeaderFooterExpectation(
            surfaces[0].Document,
            pageNumber: 1,
            pageCount: 1);
        var page2HeaderFooters = FreeWVisualEvidencePlanner.BuildHeaderFooterExpectation(
            surfaces[1].Document,
            pageNumber: 1,
            pageCount: 1);

        page1HeaderFooters.HasImages.Should().BeTrue();
        page1HeaderFooters.ImageSignatures.Single().Should().Contain("alt=Section One Letterhead");
        page1HeaderFooters.ImageSignatures.Single().Should().Contain("align=Left");
        page2HeaderFooters.HasImages.Should().BeTrue();
        page2HeaderFooters.ImageSignatures.Single().Should().Contain("alt=Section Two Letterhead");
        page2HeaderFooters.ImageSignatures.Single().Should().Contain("align=Right");
        page2HeaderFooters.ImageSignatures.Single().Should().Contain("bytes=");

        surfaces[1].Document.Blocks
            .OfType<Paragraph>()
            .Select(p => p.PlainText)
            .Should().Contain(text => text.Contains("Section 2 Header Image", StringComparison.Ordinal));
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
        var headerCellFill = expectation.Tables.Tables.Single().Cells
            .Single(cell => cell.RowIndex == 0 && cell.CellIndex == 0)
            .EffectiveFill;
        headerCellFill.StyleDerivedFillSource.Should().Be("style-derived-header");
        headerCellFill.StyleDerivedFillHex.Should().Be("#2F5496");
        headerCellFill.EffectiveFillHex.Should().Be("#2F5496");
        headerCellFill.StyleDerivedBold.Should().BeTrue();
        headerCellFill.EffectiveBold.Should().BeTrue();
        expectation.Tables.TableCellFillSignatures.Count.Should().BeGreaterThan(8);
        expectation.Tables.TableCellFillSignatures.Should().Contain(signature =>
            signature.Contains("source=style-derived-header", StringComparison.Ordinal)
            && signature.Contains("fill=#2F5496", StringComparison.Ordinal)
            && signature.Contains("gridSpan=2", StringComparison.Ordinal));
        expectation.Tables.TableCellFillSignatures.Should().Contain(signature =>
            signature.Contains("source=style-derived-banded-row", StringComparison.Ordinal)
            && signature.Contains("fill=#BDD7EE", StringComparison.Ordinal)
            && signature.Contains("row=1", StringComparison.Ordinal));
        expectation.Tables.TableCellFillSignatures.Should().Contain(signature =>
            signature.Contains("source=explicit-cell", StringComparison.Ordinal)
            && signature.Contains("fill=#EAF2F8", StringComparison.Ordinal)
            && signature.Contains("row=1", StringComparison.Ordinal));
        expectation.Tables.TableCellFillSignatures.Should().Contain(signature =>
            signature.Contains("source=explicit-cell", StringComparison.Ordinal)
            && signature.Contains("fill=#D9EAD3", StringComparison.Ordinal)
            && signature.Contains("row=4", StringComparison.Ordinal));
        expectation.Tables.Tables.Single().Cells.Should().Contain(cell =>
            cell.GridSpan == 2 && cell.RowSpan == 1);
        expectation.Tables.Tables.Single().Cells.Should().Contain(cell =>
            cell.RowSpan == 2 && cell.IsVerticalMergeContinuation == false);
    }

    [Fact]
    public void BuildTableExpectation_PreservesExplicitHeaderShadingThatMatchesStyleFill()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = new Table
        {
            Formatting = new TableFormatting
            {
                HeaderRow = true,
                BandedRows = true
            },
            TableStyleId = "GridTable4"
        };
        table.Rows.Add(new TableRow
        {
            Cells =
            {
                new TableCell("Header") { ShadingColorHex = "#2F5496" },
                new TableCell("Header 2")
            }
        });
        table.Rows.Add(new TableRow
        {
            Cells =
            {
                new TableCell("Body 1"),
                new TableCell("Body 2")
            }
        });
        document.Blocks.Add(table);

        var expectation = FreeWVisualEvidencePlanner.BuildTableExpectation(document);
        var headerCell = expectation.Tables.Single().Cells.Single(cell => cell.RowIndex == 0 && cell.CellIndex == 0);

        headerCell.ShadingColorHex.Should().Be("#2F5496");
        headerCell.EffectiveFill.ExplicitFillHex.Should().Be("#2F5496");
        headerCell.EffectiveFill.StyleDerivedFillSource.Should().Be("style-derived-header");
        headerCell.EffectiveFill.StyleDerivedFillHex.Should().Be("#2F5496");
        headerCell.EffectiveFill.EffectiveFillSource.Should().Be("explicit-cell");
        expectation.TableCellFillSignatures.Should().Contain(signature =>
            signature.Contains("row=0", StringComparison.Ordinal)
            && signature.Contains("cell=0", StringComparison.Ordinal)
            && signature.Contains("source=style-derived-header", StringComparison.Ordinal)
            && signature.Contains("fill=#2F5496", StringComparison.Ordinal));
        expectation.TableCellFillSignatures.Should().Contain(signature =>
            signature.Contains("row=0", StringComparison.Ordinal)
            && signature.Contains("cell=0", StringComparison.Ordinal)
            && signature.Contains("source=explicit-cell", StringComparison.Ordinal)
            && signature.Contains("fill=#2F5496", StringComparison.Ordinal));
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
    public void BuildPageExpectation_RecordsSharedTablePageCompositionStressPlan()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildTablePageCompositionStressDocument();
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "table-page-composition-stress",
            document.Page,
            pageNumber: 2,
            pageCount: 3,
            outputName: "table-page-composition-stress_p2.png",
            headerSlotName: "header",
            footerSlotName: "footer",
            document: document);

        document.Page.PageBorder.Should().NotBeNull();
        document.Page.WatermarkOptions.Should().NotBeNull();
        document.FinalSectionHeadersFooters.Header.Should().NotBeNull();
        document.FinalSectionHeadersFooters.Footer.Should().NotBeNull();
        document.FinalSectionHeadersFooters.Header!.PlainText.Should().Contain(" of 3");
        document.Footnotes.Should().ContainKey(1);
        var table = document.Blocks.OfType<Table>().Single();
        table.Rows[3].Cells[3].Paragraphs.Single().PlainText.Should()
            .Be("Page 2 should repeat the header row inside the same page chrome.");
        table.Rows[7].Cells[3].Paragraphs.Single().PlainText.Should()
            .Be("Page 3 should repeat the header row before the caption and closing text.");
        document.Blocks.OfType<Paragraph>().Should().Contain(p =>
            p.StyleId == Captions.StyleId &&
            p.PlainText.StartsWith("Table 1:", StringComparison.Ordinal));
        document.Blocks.OfType<Paragraph>().Should().Contain(p =>
            p.PlainText.Contains("three trusted rows", StringComparison.Ordinal));

        expectation.Composition.ExpectsTables.Should().BeTrue();
        expectation.Composition.ExpectsHeadersFooters.Should().BeTrue();
        expectation.Composition.ExpectsPageBorder.Should().BeTrue();
        expectation.Composition.ExpectsWatermark.Should().BeTrue();
        expectation.HeaderSlotName.Should().Be("header");
        expectation.FooterSlotName.Should().Be("footer");
        expectation.Features.PageBorder.Present.Should().BeTrue();
        expectation.Features.PageBorder.ColorHex.Should().Be("#24536B");
        expectation.Features.Watermark.Present.Should().BeTrue();
        expectation.Features.Watermark.Text.Should().Be("TABLE REVIEW");
        expectation.Fields.HasPageFields.Should().BeTrue();
        expectation.Fields.HasNumPagesFields.Should().BeTrue();
        expectation.Fields.HasHeaderFooterFields.Should().BeTrue();
        expectation.Tables.TableCount.Should().Be(1);
        expectation.Tables.EstimatedPageCount.Should().Be(3);
        expectation.Tables.HasMultiPageTables.Should().BeTrue();
        expectation.Tables.HasRepeatedHeaderPages.Should().BeTrue();
        expectation.Tables.HasKeepTogetherRows.Should().BeTrue();
        expectation.Tables.HasCustomCellBorders.Should().BeTrue();
        expectation.Tables.HasCellMargins.Should().BeTrue();
        expectation.Tables.HasCellSpacing.Should().BeTrue();
        expectation.Tables.HasNamedStyle.Should().BeTrue();
        expectation.Tables.Tables.Single().TableStyleId.Should().Be("GridTable1Light");
        expectation.Tables.TableCellFillSignatures.Should().Contain(signature =>
            signature.Contains("source=style-derived-header", StringComparison.Ordinal)
            && signature.Contains("fill=#4472C4", StringComparison.Ordinal));
        expectation.Tables.TableCellFillSignatures.Should().Contain(signature =>
            signature.Contains("source=explicit-cell", StringComparison.Ordinal)
            && signature.Contains("fill=#F8FBFD", StringComparison.Ordinal));
        expectation.Tables.Tables.Single().Cells.Should().Contain(cell =>
            cell.HasCustomBorders && cell.ShadingColorHex == "#F8FBFD");
        var page2 = expectation.Tables.PaginationPlans.Single().Pages[1];
        page2.RepeatedHeaderRowIndexes.Should().Equal(0);
        page2.RenderRows[0].Should().Match<DocumentTablePaginationRenderRowPlan>(row =>
            row.SourceRowIndex == 0
            && row.IsRepeatedHeader
            && row.StartsPlannedPage
            && row.PageNumber == 2);
        expectation.Tables.PaginationPlans.Single().Pages[2].RepeatedHeaderRowIndexes.Should().Equal(0);
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
        expectation.DrawingObjects.GroupChildren.ChildCount.Should().Be(5);
        expectation.DrawingObjects.GroupChildren.ImageChildCount.Should().Be(1);
        expectation.DrawingObjects.GroupChildren.ShapeChildCount.Should().Be(1);
        expectation.DrawingObjects.GroupChildren.ChartChildCount.Should().Be(1);
        expectation.DrawingObjects.GroupChildren.SmartArtChildCount.Should().Be(1);
        expectation.DrawingObjects.GroupChildren.WordArtChildCount.Should().Be(1);
        expectation.DrawingObjects.GroupChildren.HasMixedTypedChildren.Should().BeTrue();
        expectation.DrawingObjects.GroupChildren.ChildKindSummaries.Should().Contain([
            "Group0Child0:Image",
            "Group0Child1:Shape",
            "Group0Child2:Chart",
            "Group0Child3:WordArt",
            "Group0Child4:SmartArt"]);
        expectation.DrawingObjects.GroupChildren.ChildVisualSignatures.Should().Contain(signature =>
            signature.StartsWith("Group0Child2:Chart:", StringComparison.Ordinal)
            && signature.Contains("kind=Column", StringComparison.Ordinal));
        expectation.DrawingObjects.GroupChildren.ChildVisualSignatures.Should().Contain(signature =>
            signature.StartsWith("Group0Child4:SmartArt:", StringComparison.Ordinal)
            && signature.Contains("nodes=Plan:", StringComparison.Ordinal));
        expectation.DrawingObjects.Effects.EffectObjectCount.Should().Be(3);
        expectation.DrawingObjects.Effects.ShapeEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.ImageEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.WordArtEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.RenderedGroupChildEffectObjectCount.Should().Be(2);
        expectation.DrawingObjects.Effects.RenderedGroupChildShapeEffectObjectCount.Should().Be(1);
        expectation.DrawingObjects.Effects.RenderedGroupChildWordArtEffectObjectCount.Should().Be(1);
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
            "GroupChild1:Shape:glow");
        expectation.DrawingObjects.Effects.RenderedGroupChildEffectSummaries.Should().Contain(
            "GroupChild3:WordArt:glow");
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
        expectation.ChartSmartArt.SmartArtCount.Should().Be(2);
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
        expectation.ChartSmartArt.SmartArtNodeCount.Should().Be(7);
        expectation.ChartSmartArt.DistinctSmartArtFillCount.Should().BeGreaterThan(1);
        expectation.ChartSmartArt.ChartVisualSignatures.Should().HaveCount(2);
        expectation.ChartSmartArt.ChartDataSignatures.Should().HaveCount(2);
        expectation.ChartSmartArt.ChartDataSignatures.Should().Contain(
            "kind=Column|categories=4|categoryLabels=Q1,Q2,Q3,Q4|series=1|points=4|seriesData=0:Revenue=1.4,1.8,1.6,2.2");
        expectation.ChartSmartArt.ChartDataSignatures.Should().Contain(
            "kind=Scatter|categories=4|categoryLabels=155,160,165,170|series=1|points=4|seriesData=0:Sample=52,58,62,66");
        expectation.ChartSmartArt.ChartVisualSignatures.Should().Contain(signature =>
            signature.Contains("kind=Column", StringComparison.Ordinal) &&
            signature.Contains("colorScheme=mono-blue", StringComparison.Ordinal) &&
            signature.Contains("quickLayout=9", StringComparison.Ordinal) &&
            signature.Contains("plotFill=1", StringComparison.Ordinal) &&
            signature.Contains("dataLabels=1", StringComparison.Ordinal) &&
            signature.Contains("axisTitles=1", StringComparison.Ordinal) &&
            signature.Contains("palette=#214A82,#2E5FAA,#4472C4,#6C8FD1,#A9C1E7,#D6E4F4", StringComparison.Ordinal));
        expectation.ChartSmartArt.ChartVisualSignatures.Should().Contain(signature =>
            signature.Contains("kind=Scatter", StringComparison.Ordinal) &&
            signature.Contains("geometry=MarkerOnly", StringComparison.Ordinal) &&
            signature.Contains("markers=1", StringComparison.Ordinal) &&
            signature.Contains("categoryAxis=Height", StringComparison.Ordinal) &&
            signature.Contains("valueAxis=Weight", StringComparison.Ordinal));
        expectation.ChartSmartArt.SmartArtVisualSignatures.Should().HaveCount(2);
        expectation.ChartSmartArt.SmartArtVisualSignatures.Should().Contain(signature =>
            signature.Contains("layout=orgchart1", StringComparison.Ordinal) &&
            signature.Contains("hierarchy=maxDepth=2/nodes=3/connectors=2", StringComparison.Ordinal) &&
            signature.Contains("colorScheme=accent1", StringComparison.Ordinal) &&
            signature.Contains("style=intense1", StringComparison.Ordinal) &&
            signature.Contains("#1F3864", StringComparison.Ordinal) &&
            signature.Contains("path=", StringComparison.Ordinal));
        expectation.ChartSmartArt.SmartArtVisualSignatures.Should().Contain(signature =>
            signature.Contains("layout=pyramid1", StringComparison.Ordinal) &&
            signature.Contains("colorScheme=accent2", StringComparison.Ordinal) &&
            signature.Contains("style=flat1", StringComparison.Ordinal) &&
            signature.Contains("geometry=kind=Pyramid", StringComparison.Ordinal) &&
            signature.Contains("polygons=0=", StringComparison.Ordinal) &&
            signature.Contains("polygons=", StringComparison.Ordinal));
        expectation.ChartSmartArt.Charts.Should().Contain(plan =>
            plan.Kind == ChartKind.Scatter &&
            plan.GeometryKind == ChartVisualGeometryKind.MarkerOnly);
        var smartArt = expectation.ChartSmartArt.SmartArts.Single(plan => plan.LayoutId == "orgchart1");
        smartArt.LayoutId.Should().Be("orgchart1");
        smartArt.HierarchyGeometry.Should().NotBeNull();
        smartArt.HierarchyGeometry!.MaxDepth.Should().Be(2);
        smartArt.HierarchyGeometry.Connectors.Should().HaveCount(2);
        smartArt.Nodes.Select(node => node.FillHex)
            .Should().ContainInOrder("#1F3864", "#1F3864", "#1F3864");
        var pyramid = expectation.ChartSmartArt.SmartArts.Single(plan => plan.LayoutId == "pyramid1");
        pyramid.LayoutGeometry.Should().NotBeNull();
        pyramid.LayoutGeometry!.Kind.Should().Be(SmartArtLayoutGeometryKind.Pyramid);
        pyramid.LayoutGeometry.Nodes.Should().HaveCount(4);
        pyramid.LayoutGeometry.Nodes.Should().OnlyContain(node => node.HasPolygon);
    }

    [Fact]
    public void ReadManifest_DeserializesSmartArtLayoutPolygonGeometry()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var document = TextDocument.CreateEmpty();
            var smartArt = SmartArt.Create(SmartArtKind.List, ["Top", "Middle", "Lower", "Base"]);
            smartArt.LayoutId = "pyramid1";
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromSmartArt(smartArt));
            document.Blocks.Add(paragraph);
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "chart-smartart-complex",
                pageNumber: 1,
                pageCount: 1,
                documentOverride: document);
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [row],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

            var manifest = FreeWVisualEvidenceManifestNormalizer.ReadManifest(
                Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName));

            var geometry = manifest.Evidence.Single()
                .PageExpectation.ChartSmartArt.SmartArts.Single()
                .LayoutGeometry;
            geometry.Should().NotBeNull();
            geometry!.Kind.Should().Be(SmartArtLayoutGeometryKind.Pyramid);
            geometry.Nodes.Should().HaveCount(4);
            geometry.Nodes.Should().OnlyContain(node => node.HasPolygon);
            geometry.Nodes[0].PolygonPoints.Select(point => (point.X, point.Y)).Should().ContainInOrder(
                (136.5, 6),
                (163.5, 6),
                (186, 39),
                (114, 39));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
    public void PictureWatermarkLayoutPlanner_UsesNativeVmlShapeSizeBeforeAutoFit()
    {
        var options = new WatermarkOptions(string.Empty)
        {
            ImageBytes = [1, 2, 3],
            ScalePct = 40,
            NativeVmlPictureWidthPt = 468,
            NativeVmlPictureHeightPt = 281,
            Layout = WatermarkLayout.Horizontal,
            Opacity = 0.65
        };

        var plan = WatermarkVisualPlanner.BuildPictureLayout(
            options,
            pageWidthDip: 816,
            pageHeightDip: 1056,
            sourceWidthDip: 1,
            sourceHeightDip: 1);

        plan.Should().NotBeNull();
        plan!.WidthDip.Should().BeApproximately(624, 0.001);
        plan.HeightDip.Should().BeApproximately(374.667, 0.001);
        plan.XDip.Should().BeApproximately(96, 0.001);
        plan.YDip.Should().BeApproximately(340.667, 0.001);
        plan.Opacity.Should().BeApproximately(0.65, 0.001);
        plan.RotationDegrees.Should().Be(0);
    }

    [Fact]
    public void PictureWatermarkLayoutPlanner_SuppressesRecoloredNativeVmlPicturePaint()
    {
        var plan = WatermarkVisualPlanner.BuildPictureLayout(
            new WatermarkOptions(string.Empty)
            {
                ImageBytes = [1, 2, 3],
                NativeVmlPictureWidthPt = 468,
                NativeVmlPictureHeightPt = 281,
                NativeVmlPictureRecolor = true
            },
            pageWidthDip: 816,
            pageHeightDip: 1056,
            sourceWidthDip: 120,
            sourceHeightDip: 72);

        plan.Should().BeNull();
    }

    [Fact]
    public void PictureWatermarkLayoutPlanner_UsesMeasuredDrawingMlAlphaForTheImportedPictureSignature()
    {
        var options = new WatermarkOptions(string.Empty)
        {
            ImageBytes = [1],
            ScalePct = 48,
            Layout = WatermarkLayout.Horizontal,
            Opacity = 0.38
        };

        var plan = WatermarkVisualPlanner.BuildPictureLayout(
            options,
            pageWidthDip: 816,
            pageHeightDip: 1056,
            sourceWidthDip: 120,
            sourceHeightDip: 72);

        plan.Should().NotBeNull();
        plan!.Opacity.Should().BeApproximately(0.40, 0.001);
    }

    [Fact]
    public void TextWatermarkLayoutPlanner_UsesWordsFixedVmlShape()
    {
        var plan = WatermarkVisualPlanner.BuildTextLayout(
            new WatermarkOptions("TABLE REVIEW") { Layout = WatermarkLayout.Diagonal },
            pageWidthDip: 816,
            pageHeightDip: 528);

        plan.Should().NotBeNull();
        plan!.WidthDip.Should().Be(624);
        plan.HeightDip.Should().Be(156);
        plan.XDip.Should().Be(96);
        plan.YDip.Should().Be(186);
        plan.RotationDegrees.Should().Be(-45);
        WatermarkVisualPlanner.TextPathGlyphScale.Should().Be(0.50);
    }

    [Fact]
    public void TextWatermarkLayoutPlanner_UsesImportedNativeVmlTextFootprint()
    {
        var plan = WatermarkVisualPlanner.BuildTextLayout(
            new WatermarkOptions("TABLE REVIEW")
            {
                NativeVmlTextWidthPt = 512.5,
                NativeVmlTextHeightPt = 240.25
            },
            pageWidthDip: 816,
            pageHeightDip: 1056);

        plan.Should().NotBeNull();
        plan!.WidthDip.Should().BeApproximately(683.333, 0.001);
        plan.HeightDip.Should().BeApproximately(320.333, 0.001);
        plan.XDip.Should().BeApproximately(66.333, 0.001);
        plan.YDip.Should().BeApproximately(367.833, 0.001);
    }

    [Fact]
    public void TextWatermarkLayoutPlanner_UsesExplicitNativeVmlRotation()
    {
        var plan = WatermarkVisualPlanner.BuildTextLayout(
            new WatermarkOptions("DRAFT") { NativeVmlTextRotationDegrees = 300.5 },
            pageWidthDip: 816,
            pageHeightDip: 1056);

        plan.Should().NotBeNull();
        plan!.RotationDegrees.Should().BeApproximately(-59.5, 0.001);
    }

    [Fact]
    public void TextWatermarkLayoutPlanner_SuppressesExplicitlyDisabledNativeTextPath()
    {
        var plan = WatermarkVisualPlanner.BuildTextLayout(
            new WatermarkOptions("DRAFT") { NativeVmlTextPathEnabled = false },
            pageWidthDip: 816,
            pageHeightDip: 1056);

        plan.Should().BeNull();
    }

    [Fact]
    public void TextWatermarkLayoutPlanner_PreservesSerializedNativeVmlTextPath()
    {
        var options =
            new WatermarkOptions("DRAFT")
            {
                FontFamily = "Calibri",
                FontColorHex = "#808080",
                Opacity = 0.4,
                NativeVmlTextPathEnabled = true,
                NativeVmlTextPathXml = "<v:textpath on=\"t\" fitshape=\"t\" string=\"DRAFT\" />",
                NativeVmlTextFitShape = true,
                NativeVmlTextWidthPt = 468,
                NativeVmlTextHeightPt = 117,
            };
        var plan = WatermarkVisualPlanner.BuildTextLayout(
            options,
            pageWidthDip: 816,
            pageHeightDip: 1056);

        plan.Should().NotBeNull();
        plan!.CenterXDip.Should().Be(396);
        plan.CenterYDip.Should().Be(531);
        WatermarkVisualPlanner.UsesImportedDraftVisual(options).Should().BeTrue();
        WatermarkVisualPlanner.ResolveTextFontFamily(options).Should().Be("Calibri Light");
        WatermarkVisualPlanner.ResolveTextColorHex(options).Should().Be("#B4D699");
        WatermarkVisualPlanner.ResolveTextOpacity(options).Should().Be(1);
        WatermarkVisualPlanner.ResolveTextPathFontSize(options, plan, 1).Should().Be(200);
        WatermarkVisualPlanner.ResolveTextPathScaleX(options).Should().Be(1.18);
        WatermarkVisualPlanner.ResolveTextPathScaleY(options).Should().Be(0.76);
    }

    [Fact]
    public void SharedNoteRegionPlanner_ExcludesDirectAndInheritedHiddenRuns()
    {
        var document = new TextDocument();
        document.Styles["HiddenNote"] = new DocumentStyle
        {
            Id = "HiddenNote",
            Name = "Hidden Note",
            Run = RunFormatting.Default with { Hidden = true },
        };
        var note = new Footnote(1);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Visible "));
        paragraph.Runs.Add(new Run("DIRECT_SECRET", RunFormatting.Default with { Hidden = true }));
        paragraph.Runs.Add(new Run("tail"));
        note.Content.Add(paragraph);
        note.Content.Add(new Paragraph("STYLE_SECRET") { StyleId = "HiddenNote" });
        document.Footnotes[1] = note;

        var region = DocumentNoteRegionPlanner.BuildFootnoteRegion(document, [1], 1, 480);
        var continuation = DocumentNoteRegionPlanner.BuildFootnoteContinuation(document, [1], 1, 480, 48, 48);

        region.Rows.Should().ContainSingle();
        region.Rows[0].Text.Should().Contain("Visible tail");
        region.Rows[0].Text.Should().NotContain("DIRECT_SECRET");
        region.Rows[0].Text.Should().NotContain("STYLE_SECRET");
        string.Join(" ", continuation.Pages.SelectMany(page => page.Fragments).Select(fragment => fragment.Text))
            .Should().NotContain("SECRET");
    }

    [Fact]
    public void TextWatermarkLayoutPlanner_SuppressesUnverifiedSerializedNativeVmlTextPath()
    {
        var plan = WatermarkVisualPlanner.BuildTextLayout(
            new WatermarkOptions("CONFIDENTIAL")
            {
                NativeVmlTextPathEnabled = true,
                NativeVmlTextPathXml = "<v:textpath on=\"t\" fitshape=\"t\" string=\"CONFIDENTIAL\" />"
            },
            pageWidthDip: 816,
            pageHeightDip: 1056);

        plan.Should().BeNull();
    }

    [Fact]
    public void TextWatermarkLayoutPlanner_UsesSerializedFitShapeToResolveTextPathFontSize()
    {
        var fitted = WatermarkVisualPlanner.BuildTextLayout(
            new WatermarkOptions("DRAFT"),
            pageWidthDip: 816,
            pageHeightDip: 1056);
        var unfitted = WatermarkVisualPlanner.BuildTextLayout(
            new WatermarkOptions("DRAFT") { NativeVmlTextFitShape = false },
            pageWidthDip: 816,
            pageHeightDip: 1056);

        fitted.Should().NotBeNull();
        unfitted.Should().NotBeNull();
        fitted!.FitsShape.Should().BeTrue();
        unfitted!.FitsShape.Should().BeFalse();
        WatermarkVisualPlanner.ResolveTextPathFontSize(fitted, unitTextWidthDip: 4)
            .Should().Be(65);
        WatermarkVisualPlanner.ResolveTextPathFontSize(unfitted, unitTextWidthDip: 4)
            .Should().BeApproximately(4d / 3d, 0.001);
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
        evidence.GetProperty("pageExpectation").GetProperty("proofingDiagnostics").GetProperty("diagnosticCount")
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
            markdown.Should().Contain("## Drawing/Object Visual Proof Readiness");
            markdown.Should().Contain("| drawing-objects-complex | 1 | paired-renderer-proof-ready |");
            markdown.Should().Contain("paired renderer evidence is present; run Word PNG baseline comparison for drawing-objects-complex");
            markdown.Should().Contain(
                "5 grouped child object(s): Group0Child0:Image/Group0Child1:Shape/Group0Child2:Chart/Group0Child3:WordArt/Group0Child4:SmartArt");
            markdown.Should().Contain("2 rendered grouped child effect object(s): GroupChild1:Shape:glow/GroupChild3:WordArt:glow");
            markdown.Should().NotContain("planned grouped child effect object(s)");

            summary.DrawingObjectProofReadiness.Should().Contain(row =>
                row.ScenarioId == "drawing-objects-complex" &&
                row.Status == "paired-renderer-proof-ready" &&
                row.WordBaselineStatus == "not-run" &&
                row.SemanticEvidence.Contains("WPF 6 object(s)", StringComparison.Ordinal) &&
                row.SemanticEvidence.Contains("Avalonia 6 object(s)", StringComparison.Ordinal) &&
                row.Trust.Passed);
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
                            RenderedGroupChildEffectSummaries = ["GroupChild1:Shape:shadow"]
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
                && f.Contains("WPF 'GroupChild1:Shape:glow/GroupChild3:WordArt:glow'", StringComparison.Ordinal)
                && f.Contains("Avalonia 'GroupChild1:Shape:shadow'", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresMatchingGroupedMixedChildVisualEvidence()
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
            var avaloniaWithoutSmartArtChildSignature = avaloniaRow with
            {
                PageExpectation = avaloniaRow.PageExpectation with
                {
                    DrawingObjects = avaloniaRow.PageExpectation.DrawingObjects with
                    {
                        GroupChildren = avaloniaRow.PageExpectation.DrawingObjects.GroupChildren with
                        {
                            ChildVisualSignatures = avaloniaRow.PageExpectation.DrawingObjects.GroupChildren.ChildVisualSignatures
                                .Where(signature => !signature.StartsWith("Group0Child4:SmartArt:", StringComparison.Ordinal))
                                .ToList()
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
                [avaloniaWithoutSmartArtChildSignature],
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
                && f.Contains("grouped child visual signatures differ", StringComparison.Ordinal)
                && f.Contains("Group0Child4:SmartArt", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_AllowsUniformFloatingObjectRendererOriginOffset()
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
            var avaloniaRow = OffsetFloatingObjectXDip(
                BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: 1),
                (_, _) => 48);

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaRow],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

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

            summary.Trust.Passed.Should().BeTrue(string.Join(Environment.NewLine, summary.Trust.Failures));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RejectsRelativeFloatingObjectHorizontalDrift()
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
            var avaloniaRow = OffsetFloatingObjectXDip(
                BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: 1),
                (_, index) => index == 0 ? 72 : 48);

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaRow],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

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
                f.Contains("origin-normalized floating object signatures differ", StringComparison.Ordinal) &&
                f.Contains("xRel=", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresGroupedChildVisualSemanticsBeyondCounts()
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
            var avaloniaWithChangedChildVisuals = ReplaceGroupedChildVisualSignatures(
                avaloniaRow,
                signature => signature.StartsWith("Group0Child2:Chart:", StringComparison.Ordinal)
                    ? "Group0Child2:Chart:kind=Line|geometry=Lines|gridlines=1|markers=0"
                    : signature.StartsWith("Group0Child3:WordArt:", StringComparison.Ordinal)
                        ? signature.Replace("effects=glow", "effects=shadow", StringComparison.Ordinal)
                        : signature);

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaWithChangedChildVisuals],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

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
                f.Contains("proof-comparable grouped child visual signatures differ", StringComparison.Ordinal) &&
                f.Contains("Group0Child2:Chart", StringComparison.Ordinal) &&
                f.Contains("Group0Child3:WordArt", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresRenderedGroupedChildWordArtEffectEvidence()
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
            var wpfWithoutWordArtChildEffect = RemoveRenderedGroupChildWordArtEffect(wpfRow);
            var avaloniaWithoutWordArtChildEffect = RemoveRenderedGroupChildWordArtEffect(avaloniaRow);

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfWithoutWordArtChildEffect],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaWithoutWordArtChildEffect],
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
                f.Contains("drawing-object evidence expects rendered grouped child WordArt effects but the object plan records none", StringComparison.Ordinal));
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
            var alteredSmartArt = avaloniaPlan.SmartArts.Single(plan => plan.LayoutId == "orgchart1") with
            {
                Nodes = avaloniaPlan.SmartArts.Single(plan => plan.LayoutId == "orgchart1").Nodes
                    .Select((node, index) => index == 1 ? node with { FillHex = "#101010" } : node)
                    .ToList()
            };
            var avaloniaWithDifferentSmartArtPlan = avaloniaRow with
            {
                PageExpectation = avaloniaRow.PageExpectation with
                {
                    ChartSmartArt = avaloniaPlan with
                    {
                        SmartArts = avaloniaPlan.SmartArts
                            .Select(plan => plan.LayoutId == "orgchart1" ? alteredSmartArt : plan)
                            .ToList(),
                        SmartArtVisualSignatures = ChartSmartArtVisualPlanner.BuildSmartArtVisualSignatures(
                            avaloniaPlan.SmartArts
                                .Select(plan => plan.LayoutId == "orgchart1" ? alteredSmartArt : plan)
                                .ToList())
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
                && f.Contains("SmartArt visual signatures differ", StringComparison.Ordinal)
                && f.Contains("#1F3864", StringComparison.Ordinal)
                && f.Contains("#101010", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresMatchingChartDataEvidence()
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
            var alteredCharts = avaloniaPlan.Charts
                .Select((chart, index) => index == 0
                    ? chart with
                    {
                        Series =
                        [
                            new ChartSeriesVisualPlan(
                                chart.Series[0].Name,
                                [9.9, 1.8, 1.6, 2.2])
                        ]
                    }
                    : chart)
                .ToList();
            var avaloniaWithDifferentChartData = avaloniaRow with
            {
                PageExpectation = avaloniaRow.PageExpectation with
                {
                    ChartSmartArt = avaloniaPlan with
                    {
                        Charts = alteredCharts,
                        ChartDataSignatures = ChartSmartArtVisualPlanner.BuildChartDataSignatures(alteredCharts)
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaWithDifferentChartData],
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

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
                && f.Contains("chart data signatures differ", StringComparison.Ordinal)
                && f.Contains("seriesData=0:Revenue=9.9,1.8,1.6,2.2", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_AllowsStyleDerivedHeaderShadingVarianceInTablePlanEvidence()
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
            var wpfTablePlan = wpfRow.PageExpectation.Tables.Tables.Single();
            var wpfCells = wpfTablePlan.Cells.ToList();
            wpfCells[0] = wpfCells[0] with { ShadingColorHex = null };
            var wpfWithMissingHeaderShading = wpfRow with
            {
                PageExpectation = wpfRow.PageExpectation with
                {
                    Tables = wpfRow.PageExpectation.Tables with
                    {
                        Tables = [wpfTablePlan with { Cells = wpfCells }]
                    }
                }
            };
            var avaloniaRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);
            var avaloniaTablePlan = avaloniaRow.PageExpectation.Tables.Tables.Single();
            var avaloniaCells = avaloniaTablePlan.Cells.ToList();
            avaloniaCells[0] = avaloniaCells[0] with { ShadingColorHex = "#2F5496" };
            var avaloniaWithStyleDerivedHeaderShading = avaloniaRow with
            {
                PageExpectation = avaloniaRow.PageExpectation with
                {
                    Tables = avaloniaRow.PageExpectation.Tables with
                    {
                        Tables = [avaloniaTablePlan with { Cells = avaloniaCells }]
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfWithMissingHeaderShading],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaWithStyleDerivedHeaderShading],
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

            summary.Trust.Passed.Should().BeTrue();
            summary.Evidence.Should().OnlyContain(row =>
                row.Tables.TableCellFillSignatures.Any(signature =>
                    signature.Contains("source=style-derived-header", StringComparison.Ordinal)
                    && signature.Contains("fill=#2F5496", StringComparison.Ordinal)));
            summary.Evidence.Should().OnlyContain(row =>
                row.Tables.Tables.Single().Cells.Single(cell => cell.RowIndex == 0 && cell.CellIndex == 0)
                    .ShadingColorHex == null);
            summary.Trust.Failures.Should().NotContain(f =>
                f.Contains("table renderer pair 'table-layout-complex' page 1", StringComparison.Ordinal)
                && f.Contains("table plan signatures differ", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresPlannedStyleDerivedHeaderFillEvidence()
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
            var wpfTablePlan = wpfRow.PageExpectation.Tables.Tables.Single();
            var wpfCells = wpfTablePlan.Cells.ToList();
            wpfCells[0] = wpfCells[0] with { ShadingColorHex = "#4472C4" };
            var wpfWithLegacyHeaderChrome = wpfRow with
            {
                PageExpectation = wpfRow.PageExpectation with
                {
                    Tables = wpfRow.PageExpectation.Tables with
                    {
                        Tables = [wpfTablePlan with { Cells = wpfCells }]
                    }
                }
            };
            var avaloniaRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);
            var avaloniaTablePlan = avaloniaRow.PageExpectation.Tables.Tables.Single();
            var avaloniaCells = avaloniaTablePlan.Cells.ToList();
            avaloniaCells[0] = avaloniaCells[0] with { ShadingColorHex = null };
            var avaloniaWithoutHeaderChrome = avaloniaRow with
            {
                PageExpectation = avaloniaRow.PageExpectation with
                {
                    Tables = avaloniaRow.PageExpectation.Tables with
                    {
                        Tables = [avaloniaTablePlan with { Cells = avaloniaCells }]
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfWithLegacyHeaderChrome],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaWithoutHeaderChrome],
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
                && f.Contains("table cell fill signatures differ", StringComparison.Ordinal)
                && f.Contains("#4472C4", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("table renderer pair 'table-layout-complex' page 1", StringComparison.Ordinal)
                && f.Contains("table plan signatures differ", StringComparison.Ordinal)
                && f.Contains("shading color differs", StringComparison.Ordinal)
                && f.Contains("#4472C4", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_AllowsStyleDerivedBandedRowVarianceInTablePlanEvidence()
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
            var wpfTablePlan = wpfRow.PageExpectation.Tables.Tables.Single();
            var wpfCells = wpfTablePlan.Cells.ToList();
            var styleDerivedBodyCellIndex = wpfCells.FindIndex(cell =>
                cell.RowIndex == 1
                && cell.ShadingColorHex is null
                && cell.GridColumnIndex == 1);
            styleDerivedBodyCellIndex.Should().BeGreaterThanOrEqualTo(0);
            wpfCells[styleDerivedBodyCellIndex] =
                wpfCells[styleDerivedBodyCellIndex] with { ShadingColorHex = "#BDD7EE" };
            var wpfWithMaterializedBandedRowFill = wpfRow with
            {
                PageExpectation = wpfRow.PageExpectation with
                {
                    Tables = wpfRow.PageExpectation.Tables with
                    {
                        Tables = [wpfTablePlan with { Cells = wpfCells }]
                    }
                }
            };
            var avaloniaRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfWithMaterializedBandedRowFill],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaRow],
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

            summary.Trust.Passed.Should().BeTrue();
            summary.Evidence.Should().OnlyContain(row =>
                row.Tables.TableCellFillSignatures.Any(signature =>
                    signature.Contains("source=style-derived-banded-row", StringComparison.Ordinal)
                    && signature.Contains("fill=#BDD7EE", StringComparison.Ordinal)));
            summary.Evidence.Should().OnlyContain(row =>
                row.Tables.Tables.Single().Cells
                    .Where(cell => cell.RowIndex == 1 && cell.GridColumnIndex == 1)
                    .All(cell => cell.ShadingColorHex == null));
            summary.Trust.Failures.Should().NotContain(f =>
                f.Contains("table renderer pair 'table-layout-complex' page 1", StringComparison.Ordinal)
                && f.Contains("table plan signatures differ", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresMatchingExplicitHeaderTableShadingEvidence()
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
            var avaloniaCells = avaloniaTablePlan.Cells.ToList();
            avaloniaCells[0] = avaloniaCells[0] with { ShadingColorHex = "#C00000" };
            var avaloniaWithExplicitHeaderShadingTable = avaloniaTablePlan with { Cells = avaloniaCells };
            var avaloniaWithExplicitHeaderShading = avaloniaRow with
            {
                PageExpectation = avaloniaRow.PageExpectation with
                {
                    Tables = avaloniaRow.PageExpectation.Tables with
                    {
                        TableCellFillSignatures = FreeWVisualEvidencePlanner.BuildTableCellFillSignatures(
                            [avaloniaWithExplicitHeaderShadingTable]),
                        Tables = [avaloniaWithExplicitHeaderShadingTable]
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaWithExplicitHeaderShading],
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
                && f.Contains("table cell fill signatures differ", StringComparison.Ordinal)
                && f.Contains("#C00000", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("table renderer pair 'table-layout-complex' page 1", StringComparison.Ordinal)
                && f.Contains("table plan signatures differ", StringComparison.Ordinal)
                && f.Contains("shading color differs", StringComparison.Ordinal)
                && f.Contains("#C00000", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresMatchingExplicitBodyTableShadingEvidence()
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
            var avaloniaCells = avaloniaTablePlan.Cells.ToList();
            var bodyShadedCellIndex = avaloniaCells.FindIndex(cell =>
                cell.RowIndex > 0 && !string.IsNullOrWhiteSpace(cell.ShadingColorHex));
            bodyShadedCellIndex.Should().BeGreaterThanOrEqualTo(0);
            var bodyShadedCell = avaloniaCells[bodyShadedCellIndex];
            var originalFill = bodyShadedCell.ShadingColorHex!;
            avaloniaCells[bodyShadedCellIndex] = bodyShadedCell with { ShadingColorHex = "#ABCDEF" };
            var avaloniaFillSignatures = avaloniaRow.PageExpectation.Tables.TableCellFillSignatures
                .Select(signature =>
                    signature.Contains("source=explicit-cell", StringComparison.Ordinal)
                    && signature.Contains("row=" + bodyShadedCell.RowIndex.ToString(), StringComparison.Ordinal)
                    && signature.Contains("cell=" + bodyShadedCell.CellIndex.ToString(), StringComparison.Ordinal)
                    && signature.Contains("fill=" + originalFill, StringComparison.Ordinal)
                        ? signature.Replace("fill=" + originalFill, "fill=#ABCDEF", StringComparison.Ordinal)
                        : signature)
                .ToList();
            var avaloniaWithDifferentTablePlan = avaloniaRow with
            {
                PageExpectation = avaloniaRow.PageExpectation with
                {
                    Tables = avaloniaRow.PageExpectation.Tables with
                    {
                        TableCellFillSignatures = avaloniaFillSignatures,
                        Tables = [avaloniaTablePlan with { Cells = avaloniaCells }]
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
                && f.Contains("table cell fill signatures differ", StringComparison.Ordinal)
                && f.Contains("#ABCDEF", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("table renderer pair 'table-layout-complex' page 1", StringComparison.Ordinal)
                && f.Contains("table plan signatures differ", StringComparison.Ordinal)
                && f.Contains("shading color differs", StringComparison.Ordinal)
                && f.Contains("#ABCDEF", StringComparison.Ordinal));
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
            var alteredResolvedFieldSignatures = avaloniaRows[1]
                .PageExpectation
                .Fields
                .HeaderFooterResolvedFieldSignatures
                .Select(signature => signature.Contains("field=PAGE", StringComparison.Ordinal)
                    ? signature.Replace("text=1-2", "text=1-99", StringComparison.Ordinal)
                    : signature)
                .ToList();
            avaloniaRows[1] = avaloniaRows[1] with
            {
                PageExpectation = avaloniaRows[1].PageExpectation with
                {
                    Fields = avaloniaRows[1].PageExpectation.Fields with
                    {
                        ComplexFieldKeywords = ["AUTHOR", "NUMPAGES", "PAGE"],
                        HeaderFooterResolvedFieldSignatures = alteredResolvedFieldSignatures
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
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("field renderer pair 'field-page-number-variants' page 2", StringComparison.Ordinal)
                && f.Contains("resolved header/footer field signatures differ", StringComparison.Ordinal)
                && f.Contains("1-99", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresChapterPrefixInResolvedPageText()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            const string scenarioId = "field-page-number-variants";
            var wpfRow = RemoveChapterPrefixFromResolvedPageText(BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                scenarioId,
                pageNumber: 2,
                pageCount: 3));
            var avaloniaRow = RemoveChapterPrefixFromResolvedPageText(BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                scenarioId,
                pageNumber: 2,
                pageCount: 3));

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaRow],
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
                f.Contains("field evidence expects chapter-prefixed PAGE display text but records none", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresMatchingEquationGeometryEvidence()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            const string scenarioId = "equation-structures";
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
            var avaloniaWithDrift = avaloniaRow with
            {
                PageExpectation = avaloniaRow.PageExpectation with
                {
                    Equations = avaloniaRow.PageExpectation.Equations with
                    {
                        ElementGeometrySignatures =
                        [
                            .. avaloniaRow.PageExpectation.Equations.ElementGeometrySignatures
                                .Where(signature => !signature.Contains("geometry=radical", StringComparison.Ordinal))
                        ],
                        SpacingGeometrySignatures =
                        [
                            .. avaloniaRow.PageExpectation.Equations.SpacingGeometrySignatures
                                .Where(signature => !signature.Contains("spacing=radical", StringComparison.Ordinal))
                        ]
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [avaloniaWithDrift],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

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
                f.Contains("equation renderer pair 'equation-structures' page 1", StringComparison.Ordinal)
                && f.Contains("element geometry signatures differ", StringComparison.Ordinal)
                && f.Contains("geometry=radical", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("equation renderer pair 'equation-structures' page 1", StringComparison.Ordinal)
                && f.Contains("spacing geometry signatures differ", StringComparison.Ordinal)
                && f.Contains("spacing=radical", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresMatchingHeaderFooterImageEvidence()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "f2-hf-images";
            var wpfRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    page,
                    pageCount: 2))
                .ToList();
            var avaloniaRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        page,
                        pageCount: 2))
                .ToList();
            avaloniaRows[0] = avaloniaRows[0] with
            {
                PageExpectation = avaloniaRows[0].PageExpectation with
                {
                    HeaderFooters = avaloniaRows[0].PageExpectation.HeaderFooters with
                    {
                        ImageSignatures = ["slot=header|section=1|page=1|image=missing"]
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
                        2),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        2)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("header/footer renderer pair 'f2-hf-images' page 1", StringComparison.Ordinal)
                && f.Contains("header/footer image signatures differ", StringComparison.Ordinal)
                && f.Contains("image=missing", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RejectsHeaderFooterImageScenarioWithoutImages()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "f2-hf-images";
            var wpfRows = Enumerable.Range(1, 2)
                .Select(page => RemoveHeaderFooterImages(BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    page,
                    pageCount: 2)))
                .ToList();
            var avaloniaRows = Enumerable.Range(1, 2)
                .Select(page => RemoveHeaderFooterImages(BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    page,
                    pageCount: 2)))
                .ToList();

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
                        2),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        2)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("scenario expects header/footer image evidence", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresReferencesHeavyToaPageReferenceSignatures()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "references-heavy-fields";
            var wpfRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    page,
                    pageCount: 2))
                .ToList();
            wpfRows[0] = wpfRows[0] with
            {
                PageExpectation = wpfRows[0].PageExpectation with
                {
                    TableOfAuthorities = wpfRows[0].PageExpectation.TableOfAuthorities with
                    {
                        EntryWithPageReferenceCount = 0,
                        HasPageReferences = false,
                        HasExplicitPageNumbers = false,
                        PageReferenceSignatures = [],
                        PageReferences = []
                    }
                }
            };
            var avaloniaRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    page,
                    pageCount: 2))
                .ToList();

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
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
                        2),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        2)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            var normalizedWpf = summary.Evidence.Single(row =>
                row.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId
                && row.ScenarioId == scenarioId
                && row.PageNumber == 1);
            normalizedWpf.Trust.Passed.Should().BeFalse();
            normalizedWpf.Trust.Failures.Should().Contain(f =>
                f.Contains("generated Table of Authorities page references", StringComparison.Ordinal));
            normalizedWpf.Trust.Failures.Should().Contain(f =>
                f.Contains("missing generated page-reference signature", StringComparison.Ordinal)
                && f.Contains("Example v. FreeW", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("references-heavy-fields", StringComparison.Ordinal)
                && f.Contains("field renderer pair", StringComparison.Ordinal)
                && f.Contains("missing WPF page", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresMatchingReferencesHeavyToaSignatures()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "references-heavy-fields";
            var wpfRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    page,
                    pageCount: 2))
                .ToList();
            var avaloniaRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    page,
                    pageCount: 2))
                .ToList();
            avaloniaRows[0] = avaloniaRows[0] with
            {
                PageExpectation = avaloniaRows[0].PageExpectation with
                {
                    TableOfAuthorities = avaloniaRows[0].PageExpectation.TableOfAuthorities with
                    {
                        PageReferenceSignatures =
                        [
                            .. avaloniaRows[0].PageExpectation.TableOfAuthorities.PageReferenceSignatures,
                            "category=Cases|entry=Unexpected v. Drift|kind=explicit-page-numbers|pages=2|text=2"
                        ]
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
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
                        2),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        2)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Evidence.Should().OnlyContain(row => row.Trust.Passed);
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("field renderer pair 'references-heavy-fields' page 1", StringComparison.Ordinal)
                && f.Contains("generated TOA page-reference signatures differ", StringComparison.Ordinal)
                && f.Contains("Unexpected v. Drift", StringComparison.Ordinal));
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
                    ["captureSource"] = "wpf-composite-renderer"
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
    public void BuildNormalizedSummaryFromFiles_RejectsBackstageSoftwareRendererRows()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "backstage-pdf-export-fidelity",
                pageNumber: 1,
                pageCount: 2);
            var softwareFallback = row with
            {
                HostMetadata = new Dictionary<string, string>
                {
                    ["renderer"] = "FreeW.FidelityRender",
                    ["captureSource"] = "software-renderer",
                    ["backstageWorkflow"] = "pdf-export",
                    ["backstageArtifactKind"] = "pdf-export-rasterized",
                    ["backstagePipeline"] = "pdf-export-rasterized-artifact"
                }
            };
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [softwareFallback],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "backstage-pdf-export-fidelity",
                        1)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Evidence.Single().Trust.Passed.Should().BeFalse();
            summary.BackstagePrintEvidenceReadiness.Should().ContainSingle(row =>
                row.ScenarioId == "backstage-pdf-export-fidelity" &&
                row.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                row.PageNumber == 1 &&
                row.Status == "failed" &&
                row.Notes.Contains("must use real capture source 'wpf-composite-renderer'", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("backstage renderer evidence for host 'wpf-fidelity-render' must use real capture source 'wpf-composite-renderer', found 'software-renderer'", StringComparison.Ordinal));
            summary.RemainingEvidenceBlockers.Should().ContainSingle(blocker =>
                blocker.BlockerId == "backstage-real-captures-backstage-pdf-export-fidelity" &&
                blocker.Status == "missing-real-captures" &&
                blocker.SemanticEvidence.Contains("wpf-fidelity-render/p1=failed"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_AllowsBackstageSoftwareRendererRowsForNoWordFallback()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "backstage-pdf-export-fidelity";
            var softwareFallbackRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    pageNumber: page,
                    pageCount: 2) with
                    {
                        HostMetadata = new Dictionary<string, string>
                        {
                            ["renderer"] = "FreeW.FidelityRender",
                            ["captureSource"] = "software-renderer",
                            ["backstageWorkflow"] = "pdf-export",
                            ["backstageArtifactKind"] = "pdf-export-rasterized",
                            ["backstagePipeline"] = "pdf-export-rasterized-artifact",
                            ["backstageCaptureRoute"] = "backstage-pdf-export-raster-capture",
                            ["wpfRenderTargetBitmap"] = "unavailable",
                            ["wpfRenderTargetBitmapReason"] = "Software evidence renderer requested by --software-fallback; WPF RenderTargetBitmap was not used."
                        }
                    })
                .ToList();
            var avaloniaRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    pageNumber: page,
                    pageCount: 2))
                .ToList();
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                softwareFallbackRows,
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

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
                ],
                allowNoWordFallbackEvidence: true);

            summary.Trust.Passed.Should().BeTrue();
            summary.Evidence.Should().OnlyContain(row => row.Trust.Passed);
            summary.BackstagePrintEvidenceReadiness.Should().Contain(row =>
                row.ScenarioId == scenarioId &&
                row.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                row.PageNumber == 1 &&
                row.Status == "fallback" &&
                row.Notes.Contains("real wpf-composite-renderer capture still required", StringComparison.Ordinal));
            summary.RemainingEvidenceBlockers.Should().ContainSingle(blocker =>
                blocker.BlockerId == "backstage-runner-evidence-hygiene-backstage-pdf-export-fidelity" &&
                blocker.Area == "Backstage print/export visual evidence runner" &&
                blocker.Status == "runner-evidence-hygiene" &&
                blocker.SemanticEvidence.Contains("wpf-fidelity-render/p1=fallback") &&
                blocker.RequiresWordBaseline == false &&
                blocker.Trust.Passed);
            summary.RemainingEvidenceBlockers.Should().NotContain(blocker =>
                blocker.Status == "missing-real-captures");

            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();
            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.BaselineComparisons.Should().OnlyContain(comparison =>
                comparison.Status == FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
            withBaseline.EvidenceAuthority.AuthoritativeWordPngParityClaimed.Should().BeFalse();
            withBaseline.RemainingEvidenceBlockers.Should().ContainSingle(blocker =>
                blocker.BlockerId == "backstage-runner-evidence-hygiene-backstage-pdf-export-fidelity" &&
                blocker.Status == "runner-evidence-hygiene" &&
                blocker.Trust.Passed);
            withBaseline.RemainingEvidenceBlockers.Should().NotContain(blocker =>
                blocker.Status == "missing-real-captures");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_MakesDefaultWpfFloatingWrapOptionalForNoWordFallback()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var unrelatedRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "f2-hf-basic",
                pageNumber: 1,
                pageCount: 1);
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [unrelatedRow],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

            var strictSummary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                includedScenarioIds: [FreeWVisualEvidenceManifestNormalizer.FloatingWrappingWpfScenarioId]);
            strictSummary.Trust.Passed.Should().BeFalse();
            strictSummary.Trust.Failures.Should().Contain(failure =>
                failure.Contains("wpf-fidelity-render/f2-01-float-wrap", StringComparison.Ordinal)
                && failure.Contains("expected at least 1 trusted output", StringComparison.Ordinal));

            var fallbackSummary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                includedScenarioIds: [FreeWVisualEvidenceManifestNormalizer.FloatingWrappingWpfScenarioId],
                allowNoWordFallbackEvidence: true);

            fallbackSummary.Trust.Passed.Should().BeTrue();
            fallbackSummary.ExpectedScenarios.Should().NotContain(scenario =>
                scenario.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId
                && scenario.ScenarioId == FreeWVisualEvidenceManifestNormalizer.FloatingWrappingWpfScenarioId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RequiresBackstageArtifactMetadata()
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
            var missingArtifactMetadata = row with
            {
                HostMetadata = new Dictionary<string, string>
                {
                    ["renderer"] = "FreeW.FidelityRender",
                    ["captureSource"] = "wpf-composite-renderer",
                    ["backstageWorkflow"] = "print-preview"
                }
            };
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [missingArtifactMetadata],
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
                f.Contains("must declare backstageArtifactKind 'print-preview-fixed-layout'", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("must declare backstagePipeline 'print-preview-fixed-layout-artifact'", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RejectsGenericBackstageArtifactMetadata()
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
            var genericArtifactMetadata = row with
            {
                HostMetadata = new Dictionary<string, string>
                {
                    ["renderer"] = "FreeW.PageLayoutShot",
                    ["captureSource"] = "avalonia-render-target",
                    ["backstageWorkflow"] = "pdf-export",
                    ["backstageArtifactKind"] = "pdf-export",
                    ["backstagePipeline"] = "generic-page-screenshot"
                }
            };
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [genericArtifactMetadata],
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
                f.Contains("cannot use generic or fallback backstageArtifactKind 'pdf-export'", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("cannot use generic or fallback backstagePipeline 'generic-page-screenshot'", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RejectsGenericBackstageCaptureRouteMetadata()
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
            var genericCaptureRoute = row with
            {
                HostMetadata = new Dictionary<string, string>
                {
                    ["renderer"] = "FreeW.PageLayoutShot",
                    ["captureSource"] = "avalonia-render-target",
                    ["backstageWorkflow"] = "pdf-export",
                    ["backstageArtifactKind"] = "pdf-export-rasterized",
                    ["backstagePipeline"] = "pdf-export-rasterized-artifact",
                    ["backstageCaptureRoute"] = "workflow-only"
                }
            };
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [genericCaptureRoute],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

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
                f.Contains("cannot use generic or fallback backstageCaptureRoute 'workflow-only'", StringComparison.Ordinal));
            summary.BackstagePrintEvidenceReadiness.Should().ContainSingle(row =>
                row.ScenarioId == "backstage-pdf-export-fidelity" &&
                row.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                row.PageNumber == 1 &&
                row.Status == "failed" &&
                row.Notes.Contains("backstageCaptureRoute", StringComparison.Ordinal));
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
    public void BuildNormalizedSummaryFromFiles_RejectsCrossWiredBackstageArtifactMetadata()
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
            var crossWired = row with
            {
                HostMetadata = new Dictionary<string, string>
                {
                    ["renderer"] = "FreeW.FidelityRender",
                    ["captureSource"] = "wpf-composite-renderer",
                    ["backstageWorkflow"] = "print-preview",
                    ["backstageArtifactKind"] = "pdf-export-rasterized",
                    ["backstagePipeline"] = "pdf-export-rasterized-artifact"
                }
            };
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [crossWired],
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
                f.Contains("must use backstageArtifactKind 'print-preview-fixed-layout', found 'pdf-export-rasterized'", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("must use backstagePipeline 'print-preview-fixed-layout-artifact', found 'pdf-export-rasterized-artifact'", StringComparison.Ordinal));
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
            markdown.Should().Contain("## Remaining Evidence Blockers");
            markdown.Should().Contain("| backstage-real-captures-backstage-print-preview-fidelity | backstage-print-preview-fidelity | Backstage print/export visual evidence | missing-real-captures |");
            markdown.Should().Contain("Backstage print preview has paired renderer contracts, but the visual-evidence summary is missing trusted real capture rows");
            summary.RemainingEvidenceBlockers.Should().ContainSingle(blocker =>
                blocker.BlockerId == "backstage-real-captures-backstage-print-preview-fidelity" &&
                blocker.Status == "missing-real-captures" &&
                blocker.Trust.Passed == false);

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
                row.GetProperty("hostMetadata").GetProperty("backstageWorkflow").GetString() == "print-preview" &&
                row.GetProperty("hostMetadata").GetProperty("backstageArtifactKind").GetString() == "print-preview-fixed-layout" &&
                row.GetProperty("hostMetadata").GetProperty("backstagePipeline").GetString() == "print-preview-fixed-layout-artifact");
            var blockers = doc.RootElement
                .GetProperty("remainingEvidenceBlockers")
                .EnumerateArray()
                .ToArray();
            blockers.Should().Contain(row =>
                row.GetProperty("blockerId").GetString() == "backstage-real-captures-backstage-print-preview-fidelity" &&
                row.GetProperty("requiresWordBaseline").GetBoolean() == false &&
                row.GetProperty("trust").GetProperty("passed").GetBoolean() == false);
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
    public void BuildNormalizedSummaryFromFiles_ValidatesReviewProofingDiagnosticSignatures()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            const string scenarioId = "review-proofing-visual-depth";
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
                new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero));
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
                new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero));

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
            summary.Evidence.Should().OnlyContain(row => row.ProofingDiagnostics.DiagnosticCount == 4);
            summary.Evidence.Should().OnlyContain(row => row.ProofingDiagnostics.SpellingCount == 3);
            summary.Evidence.Should().OnlyContain(row => row.ProofingDiagnostics.GrammarCount == 1);
            summary.Evidence.Should().OnlyContain(row => row.ProofingDiagnostics.HasSpelling);
            summary.Evidence.Should().OnlyContain(row => row.ProofingDiagnostics.HasGrammar);
            summary.Evidence.Should().OnlyContain(row => row.ProofingDiagnostics.AdornmentCount == 4);
            summary.Evidence.Should().OnlyContain(row => row.ProofingDiagnostics.SpellingAdornmentCount == 3);
            summary.Evidence.Should().OnlyContain(row => row.ProofingDiagnostics.GrammarAdornmentCount == 1);
            summary.Evidence.Should().OnlyContain(row => row.ProofingDiagnostics.HasSpellingUnderline);
            summary.Evidence.Should().OnlyContain(row => row.ProofingDiagnostics.HasGrammarUnderline);
            summary.Evidence.SelectMany(row => row.ProofingDiagnostics.StableSignatures)
                .Should().Contain(signature => signature.Contains("kind=Grammar", StringComparison.Ordinal)
                    && signature.Contains("normalized=the", StringComparison.Ordinal)
                    && signature.Contains("language=en-US", StringComparison.Ordinal));
            summary.Evidence.SelectMany(row => row.ProofingDiagnostics.AdornmentStableSignatures)
                .Should().Contain(signature => signature.Contains("adornment=grammar-squiggle", StringComparison.Ordinal)
                    && signature.Contains("style=wavy", StringComparison.Ordinal)
                    && signature.Contains("color=#2B579A", StringComparison.Ordinal)
                    && signature.Contains("paragraphStart=49", StringComparison.Ordinal));

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(summary);
            using var doc = JsonDocument.Parse(json);
            var firstProofing = doc.RootElement.GetProperty("evidence")[0].GetProperty("proofingDiagnostics");
            firstProofing.GetProperty("adornmentCount").GetInt32().Should().Be(4);
            firstProofing.GetProperty("adornments")[0].GetProperty("underlineStyle").GetString().Should().Be("wavy");

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(summary);
            markdown.Should().Contain("4 proofing visual adornment(s)");
            markdown.Should().Contain("grammar-squiggle wavy #2B579A");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReviewProofingNoWordSummary_ReportsBaselineReadinessAndUnavailableBlockers()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarios = FreeWVisualEvidenceManifestNormalizer.ReviewProofingVisualProofScenarioIds;
            var wpfRows = scenarios
                .Select(scenarioId => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: 1))
                .ToList();
            var avaloniaRows = scenarios
                .Select(scenarioId => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: 1))
                .ToList();

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

            var expected = scenarios
                .SelectMany(scenarioId => new[]
                {
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        1)
                })
                .ToList();
            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                expected);
            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();

            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.ReviewProofingProofReadiness.Should().HaveCount(scenarios.Count);
            withBaseline.ReviewProofingProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready" &&
                row.WordBaselineStatus == "word-baseline-unavailable=2" &&
                row.BaselineReadiness.Contains("without authoritative Word parity", StringComparison.Ordinal) &&
                row.SemanticEvidence.Contains("spelling-squiggle wavy #D13438", StringComparison.Ordinal) &&
                row.SemanticEvidence.Contains("grammar-squiggle wavy #2B579A", StringComparison.Ordinal) &&
                row.Trust.Passed);
            withBaseline.RemainingEvidenceBlockers
                .Where(blocker => blocker.Area == "Review proofing visual adornment fidelity")
                .Should().HaveCount(scenarios.Count)
                .And.OnlyContain(blocker =>
                    blocker.Status == FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus &&
                    blocker.RequiresWordBaseline &&
                    blocker.Reason.Contains("Word.Application", StringComparison.Ordinal) &&
                    blocker.SemanticEvidence.Any(evidence =>
                        evidence.Contains("adornment=spelling-squiggle", StringComparison.Ordinal)) &&
                    blocker.CandidateBaselinePaths.Any(path =>
                        path.EndsWith("_p1.png", StringComparison.Ordinal)));

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            var readiness = doc.RootElement.GetProperty("reviewProofingProofReadiness");
            readiness.GetArrayLength().Should().Be(scenarios.Count);
            readiness.EnumerateArray()
                .Should().OnlyContain(row =>
                    row.GetProperty("trust").GetProperty("passed").GetBoolean()
                    && row.GetProperty("wordBaselineStatus").GetString() == "word-baseline-unavailable=2");
            var blockers = doc.RootElement.GetProperty("remainingEvidenceBlockers").EnumerateArray().ToArray();
            blockers.Should().Contain(row =>
                row.GetProperty("blockerId").GetString() == "review-proofing-visual-depth-word-baseline-fidelity"
                && row.GetProperty("status").GetString() == "word-baseline-unavailable"
                && row.GetProperty("requiresWordBaseline").GetBoolean());

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Review Proofing Visual Proof Readiness");
            markdown.Should().Contain("| review-proofing-visual-depth | 1 | paired-renderer-proof-ready |");
            markdown.Should().Contain("review-proofing-visual-depth-word-baseline-fidelity");
            markdown.Should().Contain("Word COM or baseline generation unavailable; paired WPF/Avalonia proofing adornment evidence is retained without authoritative Word parity");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RejectsReviewProofingMissingVisualAdornments()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            const string scenarioId = "review-proofing-visual-depth";
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                scenarioId,
                pageNumber: 1,
                pageCount: 1);
            var missingAdornmentRow = row with
            {
                PageExpectation = row.PageExpectation with
                {
                    ProofingDiagnostics = row.PageExpectation.ProofingDiagnostics with
                    {
                        AdornmentCount = 0,
                        SpellingAdornmentCount = 0,
                        GrammarAdornmentCount = 0,
                        HasSpellingUnderline = false,
                        HasGrammarUnderline = false,
                        AdornmentStableSignatures = [],
                        Adornments = []
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [missingAdornmentRow],
                new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        1)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("scenario expects proofing visual adornment evidence", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("spelling visual adornment count must match", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("grammar visual adornment count must match", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_ValidatesReviewProtectionCommandMatrix()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            const string scenarioId = "review-protection-proofing-comments-only";
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
                new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero));
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
                new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero));

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
            summary.Evidence.Should().OnlyContain(row => row.ProofingDiagnostics.DiagnosticCount == 4);
            summary.Evidence.Should().OnlyContain(row => row.ReviewProtection.ProtectionMode == nameof(ProtectionMode.CommentsOnly));
            summary.Evidence.Should().OnlyContain(row => row.ReviewProtection.RestrictEditing.IsChecked);
            summary.Evidence.Should().OnlyContain(row => row.ReviewProtection.MarkAsFinal.IsChecked);
            summary.Evidence.Should().OnlyContain(row => row.ReviewProtection.IsMarkedAsFinal);
            summary.Evidence.Should().OnlyContain(row => !row.ReviewProtection.IsCommentWorkflowAllowed);
            summary.Evidence.SelectMany(row => row.ReviewProtection.StableSignatures)
                .Should().Contain(signature =>
                    signature.Contains("operation=ProofingReplacement", StringComparison.Ordinal)
                    && signature.Contains("allowed=0", StringComparison.Ordinal)
                    && signature.Contains("blockReason=MarkedAsFinal", StringComparison.Ordinal));
            summary.Evidence.SelectMany(row => row.ReviewProtection.StableSignatures)
                .Should().Contain(signature =>
                    signature.Contains("operation=HistoryUndo", StringComparison.Ordinal)
                    && signature.Contains("mutation=Comment", StringComparison.Ordinal)
                    && signature.Contains("allowed=0", StringComparison.Ordinal)
                    && signature.Contains("blockReason=MarkedAsFinal", StringComparison.Ordinal));

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(summary);
            using var doc = JsonDocument.Parse(json);
            var firstProtection = doc.RootElement.GetProperty("evidence")[0].GetProperty("reviewProtection");
            firstProtection.GetProperty("protectionMode").GetString().Should().Be(nameof(ProtectionMode.CommentsOnly));
            firstProtection.GetProperty("restrictEditing").GetProperty("isChecked").GetBoolean().Should().BeTrue();
            firstProtection.GetProperty("isMarkedAsFinal").GetBoolean().Should().BeTrue();
            firstProtection.GetProperty("markAsFinal").GetProperty("isChecked").GetBoolean().Should().BeTrue();

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(summary);
            markdown.Should().Contain("protection CommentsOnly");
            markdown.Should().Contain("Mark as Final checked");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RejectsCrossHostReviewProofingDiagnosticDrift()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            const string scenarioId = "review-proofing-visual-depth";
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
                    ProofingDiagnostics = avaloniaRow.PageExpectation.ProofingDiagnostics with
                    {
                        StableSignatures = avaloniaRow.PageExpectation.ProofingDiagnostics.StableSignatures
                            .Select(signature => signature.Contains("kind=Grammar", StringComparison.Ordinal)
                                ? signature.Replace("language=en-US", "language=de-DE", StringComparison.Ordinal)
                                : signature)
                            .ToList(),
                        AdornmentStableSignatures = avaloniaRow.PageExpectation.ProofingDiagnostics.AdornmentStableSignatures
                            .Select(signature => signature.Contains("adornment=grammar-squiggle", StringComparison.Ordinal)
                                ? signature.Replace("color=#2B579A", "color=#D13438", StringComparison.Ordinal)
                                : signature)
                            .ToList()
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [driftedAvaloniaRow],
                new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero));

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
                f.Contains("review renderer pair 'review-proofing-visual-depth' page 1", StringComparison.Ordinal)
                && f.Contains("proofing diagnostic signatures differ", StringComparison.Ordinal)
                && f.Contains("kind=Grammar", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("review renderer pair 'review-proofing-visual-depth' page 1", StringComparison.Ordinal)
                && f.Contains("proofing visual adornment signatures differ", StringComparison.Ordinal)
                && f.Contains("grammar-squiggle", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RejectsCrossHostReviewProtectionCommandDrift()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            const string scenarioId = "review-protection-proofing-comments-only";
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
                    ReviewProtection = avaloniaRow.PageExpectation.ReviewProtection with
                    {
                        StableSignatures = avaloniaRow.PageExpectation.ReviewProtection.StableSignatures
                            .Select(signature => signature.Contains("operation=ProofingReplacement", StringComparison.Ordinal)
                                ? signature.Replace("allowed=0", "allowed=1", StringComparison.Ordinal)
                                : signature)
                            .ToList()
                    }
                }
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfRow],
                new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [driftedAvaloniaRow],
                new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero));

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
                f.Contains("review renderer pair 'review-protection-proofing-comments-only' page 1", StringComparison.Ordinal)
                && f.Contains("protection command signatures differ", StringComparison.Ordinal)
                && f.Contains("operation=ProofingReplacement", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReviewMarkupNoWordSummary_ReportsBaselineReadinessAndUnavailableBlockers()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarios = FreeWVisualEvidenceManifestNormalizer.ReviewMarkupVisualProofScenarioIds;
            var wpfRows = scenarios
                .Select(scenarioId => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: 1))
                .ToList();
            var avaloniaRows = scenarios
                .Select(scenarioId => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: 1))
                .ToList();

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

            var expected = scenarios
                .SelectMany(scenarioId => new[]
                {
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        1)
                })
                .ToList();
            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                expected);
            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();

            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.ReviewMarkupProofReadiness.Should().HaveCount(scenarios.Count);
            withBaseline.ReviewMarkupProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready" &&
                row.WordBaselineStatus == "word-baseline-unavailable=2" &&
                row.BaselineReadiness.Contains("without authoritative Word parity", StringComparison.Ordinal) &&
                row.SemanticEvidence.Contains("WPF", StringComparison.Ordinal) &&
                row.SemanticEvidence.Contains("Avalonia", StringComparison.Ordinal) &&
                row.Trust.Passed);
            withBaseline.Evidence.Where(row => row.ScenarioId == "f2-tracked-changes")
                .Should().OnlyContain(row =>
                    row.ReviewMarkup.RevisionCount > 0 &&
                    row.ReviewMarkup.Authors.Contains("Alice") &&
                    row.ReviewMarkup.Authors.Contains("Bob") &&
                    row.ReviewMarkup.Authors.Contains("Carol"));
            withBaseline.Evidence.Where(row => row.ScenarioId == "f2-comments")
                .Should().OnlyContain(row =>
                    row.ReviewMarkup.CommentCount == 2 &&
                    row.ReviewMarkup.CommentAnchorCount > 0 &&
                    row.ReviewMarkup.CommentReferenceCount > 0);
            withBaseline.RemainingEvidenceBlockers
                .Where(blocker => blocker.Area == "Review markup visual fidelity")
                .Should().HaveCount(scenarios.Count)
                .And.OnlyContain(blocker =>
                    blocker.Status == FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus &&
                    blocker.RequiresWordBaseline &&
                    blocker.Reason.Contains("Word.Application", StringComparison.Ordinal) &&
                    blocker.SemanticEvidence.Any(evidence =>
                        evidence.Contains("revisions=", StringComparison.Ordinal) ||
                        evidence.Contains("comments=", StringComparison.Ordinal)));

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().BeGreaterThanOrEqualTo(42);
            doc.RootElement.GetProperty("reviewMarkupProofReadiness").GetArrayLength().Should().Be(scenarios.Count);
            doc.RootElement.GetProperty("remainingEvidenceBlockers").EnumerateArray()
                .Should().Contain(blocker =>
                    blocker.GetProperty("blockerId").GetString() == "f2-comments-word-baseline-fidelity" &&
                    blocker.GetProperty("status").GetString() == "word-baseline-unavailable" &&
                    blocker.GetProperty("requiresWordBaseline").GetBoolean());

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Review Markup Visual Proof Readiness");
            markdown.Should().Contain("| f2-tracked-changes | 1 | paired-renderer-proof-ready |");
            markdown.Should().Contain("f2-comments-word-baseline-fidelity");
            markdown.Should().Contain("Word COM or baseline generation unavailable; paired WPF/Avalonia review markup evidence is retained without authoritative Word parity");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_ValidatesReviewCompareCombineProofReadiness()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioIds = new[]
            {
                "review-compare-visual-proof",
                "review-combine-visual-proof"
            };

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                scenarioIds.Select(scenarioId => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: 1)).ToList(),
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                scenarioIds.Select(scenarioId => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: 1)).ToList(),
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                scenarioIds.SelectMany(scenarioId => new[]
                {
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        1)
                }).ToList());

            summary.Trust.Passed.Should().BeTrue();
            summary.ReviewCompareCombineProofReadiness.Should().HaveCount(2);
            summary.ReviewCompareCombineProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready"
                && row.Trust.Passed
                && row.WordBaselineStatus == "not-run"
                && row.SemanticEvidence.Contains("WPF", StringComparison.Ordinal)
                && row.SemanticEvidence.Contains("Avalonia", StringComparison.Ordinal));
            summary.Evidence.Where(row => row.ScenarioId == "review-compare-visual-proof")
                .Should().OnlyContain(row =>
                    row.ReviewCompareCombine.Operation == "compare"
                    && row.ReviewCompareCombine.HasCompareSemantics
                    && row.ReviewCompareCombine.Authors.Contains("Riley"));
            summary.Evidence.Where(row => row.ScenarioId == "review-combine-visual-proof")
                .Should().OnlyContain(row =>
                    row.ReviewCompareCombine.Operation == "combine"
                    && row.ReviewCompareCombine.HasCombineSemantics
                    && row.ReviewCompareCombine.Authors.Contains("Alice")
                    && row.ReviewCompareCombine.Authors.Contains("Bob"));

            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();
            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.ReviewCompareCombineProofReadiness.Should().HaveCount(2);
            withBaseline.ReviewCompareCombineProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready"
                && row.WordBaselineStatus == "word-baseline-unavailable=2"
                && row.BaselineReadiness.Contains("without authoritative Word parity", StringComparison.Ordinal)
                && row.Trust.Passed);
            withBaseline.RemainingEvidenceBlockers.Should().HaveCount(2);
            withBaseline.RemainingEvidenceBlockers.Should().OnlyContain(blocker =>
                scenarioIds.Contains(blocker.ScenarioId)
                && blocker.BlockerId == blocker.ScenarioId + "-word-baseline-fidelity"
                && blocker.Status == FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus
                && blocker.RequiresWordBaseline);
            withBaseline.RemainingEvidenceBlockers.Should().Contain(blocker =>
                blocker.ScenarioId == "review-combine-visual-proof"
                && blocker.SemanticEvidence.Any(evidence =>
                    evidence.Contains("operation=combine", StringComparison.Ordinal)
                    && evidence.Contains("Alice", StringComparison.Ordinal)
                    && evidence.Contains("Bob", StringComparison.Ordinal)));

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(summary);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("reviewCompareCombineProofReadiness").GetArrayLength().Should().Be(2);
            var firstCompareCombine = doc.RootElement.GetProperty("evidence")[0].GetProperty("reviewCompareCombine");
            firstCompareCombine.GetProperty("revisionCount").GetInt32().Should().BeGreaterThan(0);

            var withBaselineJson = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var withBaselineDoc = JsonDocument.Parse(withBaselineJson);
            withBaselineDoc.RootElement.GetProperty("schemaVersion").GetInt32().Should().BeGreaterThanOrEqualTo(41);
            withBaselineDoc.RootElement.GetProperty("remainingEvidenceBlockers").EnumerateArray()
                .Should().Contain(blocker =>
                    blocker.GetProperty("blockerId").GetString() == "review-combine-visual-proof-word-baseline-fidelity");

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(summary);
            markdown.Should().Contain("Review Compare/Combine Visual Proof Readiness");
            markdown.Should().Contain("review-compare-visual-proof");
            markdown.Should().Contain("review-combine-visual-proof");
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
    public void SectionGeometryNoWordSummary_ReportsBaselineReadinessAndUnavailableBlocker()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            const string scenarioId = "f2-section-landscape";
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
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        pageNumber: 1,
                        pageCount: 2),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        pageNumber: 2,
                        pageCount: 2)
                ],
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

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

            summary.Trust.Passed.Should().BeTrue();
            summary.SectionGeometryProofReadiness.Should().HaveCount(2);
            summary.SectionGeometryProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready"
                && row.Trust.Passed
                && row.SemanticEvidence.Contains("section=", StringComparison.Ordinal)
                && row.BaselineReadiness.Contains("run Word PNG baseline comparison", StringComparison.Ordinal));
            summary.SectionGeometryProofReadiness.Single(row => row.PageNumber == 2)
                .SemanticEvidence.Should().Contain("landscape");

            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();
            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(summary, comparisons);

            withBaseline.SectionGeometryProofReadiness.Should().OnlyContain(row =>
                row.WordBaselineStatus.Contains("word-baseline-unavailable=2", StringComparison.Ordinal)
                && row.BaselineReadiness.Contains("without authoritative Word parity", StringComparison.Ordinal)
                && row.Trust.Passed);
            withBaseline.RemainingEvidenceBlockers.Should().ContainSingle(blocker =>
                blocker.BlockerId == "f2-section-landscape-word-baseline-fidelity"
                && blocker.Status == FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus
                && blocker.RequiresWordBaseline
                && blocker.SemanticEvidence.Any(evidence => evidence.Contains("section=2", StringComparison.Ordinal)));

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().BeGreaterThanOrEqualTo(45);
            doc.RootElement.GetProperty("sectionGeometryProofReadiness").GetArrayLength().Should().Be(2);
            doc.RootElement.GetProperty("remainingEvidenceBlockers").EnumerateArray()
                .Should().Contain(blocker =>
                    blocker.GetProperty("blockerId").GetString() == "f2-section-landscape-word-baseline-fidelity");

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Section Geometry Visual Proof Readiness");
            markdown.Should().Contain("f2-section-landscape");
            markdown.Should().Contain("word-baseline-unavailable");
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
    public void BuildNormalizedSummaryFromFiles_FiltersIncludedScenariosBeforeValidation()
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
                        pageCount: 2),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "backstage-print-preview-fidelity",
                        pageNumber: 2,
                        pageCount: 2),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "drawing-objects-complex",
                        pageNumber: 1,
                        pageCount: 1)
                ],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "backstage-print-preview-fidelity",
                        pageNumber: 1,
                        pageCount: 2),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "backstage-print-preview-fidelity",
                        pageNumber: 2,
                        pageCount: 2)
                ],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                includedScenarioIds: ["backstage-print-preview-fidelity"]);

            summary.Trust.Passed.Should().BeTrue();
            summary.Evidence.Should().HaveCount(4);
            summary.Evidence.Select(e => e.ScenarioId)
                .Should().OnlyContain(id => id == "backstage-print-preview-fidelity");
            summary.ExpectedScenarios.Select(s => s.ScenarioId)
                .Should().OnlyContain(id => id == "backstage-print-preview-fidelity");
            summary.BackstagePrintEvidenceReadiness
                .Where(row => row.ScenarioId == "backstage-print-preview-fidelity")
                .Should().HaveCount(4)
                .And.OnlyContain(row => row.Status == "trusted");
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
            var authority = doc.RootElement.GetProperty("evidenceAuthority");
            authority.GetProperty("authorityLevel").GetString()
                .Should().Be("word-baseline-unavailable");
            authority.GetProperty("authoritativeWordPngParityClaimed").GetBoolean()
                .Should().BeFalse();
            authority.GetProperty("trustedEvidenceRows").GetInt32().Should().Be(1);
            authority.GetProperty("comparableWordBaselineRows").GetInt32().Should().Be(1);
            authority.GetProperty("realWordPngComparedRows").GetInt32().Should().Be(0);
            authority.GetProperty("wordBaselineUnavailableRows").GetInt32().Should().Be(1);
            authority.GetProperty("preparatoryEvidenceRows").GetInt32().Should().Be(1);

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Evidence Authority");
            markdown.Should().Contain("Authority level: `word-baseline-unavailable`");
            markdown.Should().Contain("Authoritative Word PNG parity claimed: no");
            markdown.Should().Contain("| 1 | 1 | 0 | 1 | 0 | 0 | 0 | 1 |");
            markdown.Should().Contain("## Word Baseline Triage");
            markdown.Should().Contain("Word baseline unavailable: 1 row(s). Trust remains passed for unavailable rows.");
            markdown.Should().Contain("Word COM or baseline generation was unavailable; no authoritative Word PNG parity is claimed for unavailable rows.");
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
    public void BuildNormalizedSummaryFromFiles_TrustsLegalReferenceSectionPageNumberSignatures()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "legal-reference-section-page-numbers";
            var wpfRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    page,
                    pageCount: 2))
                .ToList();
            var avaloniaRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    page,
                    pageCount: 2))
                .ToList();

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

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

            summary.Trust.Passed.Should().BeTrue();
            summary.Evidence.Should().OnlyContain(row => row.Trust.Passed);
            var wpfPage1 = summary.Evidence.Single(row =>
                row.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId
                && row.ScenarioId == scenarioId
                && row.PageNumber == 1);
            var caseReference = wpfPage1.TableOfAuthorities.PageReferences.Should().ContainSingle(reference =>
                reference.EntryText == "Matter of Sectioned Pages, 101 F. Supp. 3d 2026 (D. FreeW)").Subject;
            caseReference.PageNumbers.Should().Equal(1, 2);
            caseReference.DisplayedPageReferences.Should().Equal("1", "i");
            caseReference.PageReferenceKind.Should().Be("section-formatted-page-numbers");
            wpfPage1.TableOfAuthorities.PageReferenceSignatures.Should().Contain(
                "category=Cases|entry=Matter of Sectioned Pages, 101 F. Supp. 3d 2026 (D. FreeW)|kind=section-formatted-page-numbers|pages=1,2|text=i, 1");

            summary.LegalReferenceProofReadiness.Should().HaveCount(2);
            summary.LegalReferenceProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready"
                && row.WordBaselineStatus == "not-run"
                && row.Trust.Passed);
            summary.LegalReferenceProofReadiness.Should().Contain(row =>
                row.SemanticEvidence.Contains("section-formatted-page-numbers", StringComparison.Ordinal));

            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();
            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.LegalReferenceProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready"
                && row.WordBaselineStatus == "word-baseline-unavailable=2"
                && row.BaselineReadiness.Contains("without authoritative Word parity", StringComparison.Ordinal)
                && row.Trust.Passed);
            var blocker = withBaseline.RemainingEvidenceBlockers.Should().ContainSingle().Subject;
            blocker.BlockerId.Should().Be("legal-reference-section-page-number-fidelity");
            blocker.ScenarioId.Should().Be(scenarioId);
            blocker.Area.Should().Be("Section-formatted TOA page-number fidelity");
            blocker.Status.Should().Be(FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
            blocker.RequiredEvidence.Should().Contain("real MS Word PNG comparisons");
            blocker.Reason.Should().Contain("Word.Application");
            blocker.SemanticEvidence.Should().Contain(evidence =>
                evidence.Contains("wpf-fidelity-render/p1", StringComparison.Ordinal)
                && evidence.Contains("kind=section-formatted-page-numbers|pages=1,2|text=i, 1", StringComparison.Ordinal));
            blocker.RequiresWordBaseline.Should().BeTrue();
            blocker.CandidateBaselinePaths.Should().Contain("legal-reference-section-page-numbers/legal-reference-section-page-numbers_p1.png");

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("legalReferenceProofReadiness").GetArrayLength().Should().Be(2);
            doc.RootElement.GetProperty("remainingEvidenceBlockers")[0].GetProperty("blockerId").GetString()
                .Should().Be("legal-reference-section-page-number-fidelity");

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Legal Reference Section Page-Number Proof Readiness");
            markdown.Should().Contain("paired-renderer-proof-ready");
            markdown.Should().Contain("word-baseline-unavailable=2");
            markdown.Should().Contain("legal-reference-section-page-number-fidelity");
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
            var avaloniaDir = Path.Combine(root, "avalonia");
            var wpfRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    "references-heavy-fields",
                    page,
                    pageCount: 2))
                .ToList();
            var avaloniaRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    "references-heavy-fields",
                    page,
                    pageCount: 2))
                .ToList();
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
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
                        "references-heavy-fields",
                        2),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        "references-heavy-fields",
                        2)
                ]);

            summary.Trust.Passed.Should().BeTrue();
            summary.ReferencesHeavyProofReadiness.Should().HaveCount(2);
            summary.ReferencesHeavyProofReadiness.Should().OnlyContain(row =>
                row.ScenarioId == "references-heavy-fields"
                && row.Status == "paired-renderer-proof-ready"
                && row.WordBaselineStatus == "not-run"
                && row.SemanticEvidence.Contains("BIBLIOGRAPHY", StringComparison.Ordinal)
                && row.SemanticEvidence.Contains("TOA entries=", StringComparison.Ordinal)
                && row.Trust.Passed);
            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();
            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.ReferencesHeavyProofReadiness.Should().HaveCount(2);
            withBaseline.ReferencesHeavyProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready"
                && row.WordBaselineStatus == "word-baseline-unavailable=2"
                && row.BaselineReadiness.Contains("without authoritative Word parity", StringComparison.Ordinal)
                && row.SemanticEvidence.Contains("Example v. FreeW", StringComparison.Ordinal));
            var blocker = withBaseline.RemainingEvidenceBlockers.Should().ContainSingle().Subject;
            blocker.BlockerId.Should().Be("references-heavy-toa-page-number-fidelity");
            blocker.ScenarioId.Should().Be("references-heavy-fields");
            blocker.Area.Should().Be("TOA page-number fidelity");
            blocker.Status.Should().Be(FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
            blocker.RequiredEvidence.Should().Contain("real MS Word PNG comparisons");
            blocker.Reason.Should().Contain("Word.Application");
            blocker.SemanticEvidence.Should().Contain(evidence =>
                evidence.Contains("wpf-fidelity-render/p1", StringComparison.Ordinal)
                && evidence.Contains("category=Cases|entry=Example v. FreeW, 123 F.4th 456 (2026)|kind=explicit-page-numbers|pages=1,2|text=1, 2", StringComparison.Ordinal));
            blocker.SemanticEvidence.Should().Contain(evidence =>
                evidence.Contains("avalonia-page-layout-shot/p1", StringComparison.Ordinal)
                && evidence.Contains("category=Statutes|entry=Free Software Evidence Act, 42 U.S.C. 2026|kind=explicit-page-numbers|pages=1|text=1", StringComparison.Ordinal));
            blocker.RequiresWordBaseline.Should().BeTrue();
            blocker.RelatedBaselineStatuses.Should().Contain(
                FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
            blocker.CandidateBaselinePaths.Should().Contain("references-heavy-fields/references-heavy-fields_p1.png");
            blocker.Trust.Passed.Should().BeTrue();

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            var readiness = doc.RootElement.GetProperty("referencesHeavyProofReadiness");
            readiness.GetArrayLength().Should().Be(2);
            readiness.EnumerateArray().Should().Contain(row =>
                row.GetProperty("scenarioId").GetString() == "references-heavy-fields" &&
                row.GetProperty("wordBaselineStatus").GetString() == "word-baseline-unavailable=2" &&
                row.GetProperty("trust").GetProperty("passed").GetBoolean());
            var jsonBlocker = doc.RootElement.GetProperty("remainingEvidenceBlockers")[0];
            jsonBlocker.GetProperty("blockerId").GetString()
                .Should().Be("references-heavy-toa-page-number-fidelity");
            jsonBlocker.GetProperty("status").GetString()
                .Should().Be("word-baseline-unavailable");
            jsonBlocker.GetProperty("semanticEvidence").GetArrayLength().Should().Be(8);
            jsonBlocker.GetProperty("requiresWordBaseline").GetBoolean().Should().BeTrue();
            jsonBlocker.GetProperty("trust").GetProperty("passed").GetBoolean()
                .Should().BeTrue();

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## References-Heavy Field/TOA Proof Readiness");
            markdown.Should().Contain("| references-heavy-fields | 1 | paired-renderer-proof-ready |");
            markdown.Should().Contain("word-baseline-unavailable=2");
            markdown.Should().Contain("## Remaining Evidence Blockers");
            markdown.Should().Contain("references-heavy-toa-page-number-fidelity");
            markdown.Should().Contain("TOA page-number fidelity");
            markdown.Should().Contain("category=Cases\\|entry=Example v. FreeW, 123 F.4th 456 (2026)\\|kind=explicit-page-numbers\\|pages=1,2\\|text=1, 2");
            markdown.Should().Contain("yes");
            markdown.Should().Contain("COM ProgID 'Word.Application' is not registered");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EquationStructuresNoWordSummary_ReportsEquationWordBaselineEvidenceBlocker()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            const string scenarioId = "equation-structures";
            var wpfRows = new[]
            {
                BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: 1)
            };
            var avaloniaRows = new[]
            {
                BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: 1)
            };
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
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
            summary.Evidence.Should().OnlyContain(row => row.Trust.Passed);
            summary.Evidence.Should().OnlyContain(row =>
                row.Equations.EquationCount >= 8
                && row.Equations.ElementCount >= 8
                && row.Equations.SpacingGeometrySignatures.Count > 0
                && row.Equations.ElementKindCounts.Contains("EquationArray=1")
                && row.Equations.ElementKindCounts.Contains("Fraction=1")
                && row.Equations.ElementKindCounts.Contains("Radical=1")
                && row.Equations.ElementKindCounts.Contains("NAry=2")
                && row.Equations.ElementKindCounts.Contains("Accent=1")
                && row.Equations.ElementKindCounts.Contains("Bar=2")
                && row.Equations.ElementKindCounts.Contains("Delimiter=1")
                && row.Equations.ElementKindCounts.Contains("GroupChar=2")
                && row.Equations.ElementKindCounts.Contains("FunctionApply=2"));
            summary.Evidence.Should().OnlyContain(row =>
                row.Equations.ElementGeometrySignatures.Any(signature => signature.Contains("geometry=script", StringComparison.Ordinal))
                && row.Equations.ElementGeometrySignatures.Any(signature => signature.Contains("geometry=function-apply", StringComparison.Ordinal))
                && row.Equations.SpacingGeometrySignatures.Any(signature => signature.Contains("spacing=equationarray", StringComparison.Ordinal))
                && row.Equations.SegmentRoleCounts.Contains("FunctionArgument=2")
                && row.Equations.SegmentRoleCounts.Contains("GroupCharMark=2"));

            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();
            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            var blocker = withBaseline.RemainingEvidenceBlockers.Should().ContainSingle().Subject;
            blocker.BlockerId.Should().Be("equation-structures-word-baseline-fidelity");
            blocker.ScenarioId.Should().Be(scenarioId);
            blocker.Area.Should().Be("Equation structure visual fidelity");
            blocker.Status.Should().Be(FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
            blocker.RequiredEvidence.Should().Contain("real MS Word PNG comparisons");
            blocker.Reason.Should().Contain("Word.Application");
            blocker.SemanticEvidence.Should().Contain(evidence =>
                evidence.Contains("wpf-fidelity-render/p1", StringComparison.Ordinal)
                && evidence.Contains("EquationArray=1", StringComparison.Ordinal)
                && evidence.Contains("FunctionApply=2", StringComparison.Ordinal)
                && evidence.Contains("structureFamilies=", StringComparison.Ordinal)
                && evidence.Contains("roleFamilies=", StringComparison.Ordinal)
                && evidence.Contains("geometryFamilies=", StringComparison.Ordinal)
                && evidence.Contains("spacingFamilies=", StringComparison.Ordinal));
            blocker.SemanticEvidence.Should().Contain(evidence =>
                evidence.Contains("avalonia-page-layout-shot/p1", StringComparison.Ordinal)
                && evidence.Contains("elements=", StringComparison.Ordinal)
                && evidence.Contains("geometry=function-apply", StringComparison.Ordinal)
                && evidence.Contains("spacing=equationarray", StringComparison.Ordinal)
                && evidence.Contains("FunctionArgument=2", StringComparison.Ordinal));
            blocker.RelatedBaselineStatuses.Should().Contain(
                FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
            blocker.CandidateBaselinePaths.Should().Contain("equation-structures/equation-structures_p1.png");
            blocker.RequiresWordBaseline.Should().BeTrue();
            blocker.Trust.Passed.Should().BeTrue();

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().BeGreaterThanOrEqualTo(37);
            var jsonBlocker = doc.RootElement.GetProperty("remainingEvidenceBlockers")[0];
            jsonBlocker.GetProperty("blockerId").GetString()
                .Should().Be("equation-structures-word-baseline-fidelity");
            jsonBlocker.GetProperty("status").GetString()
                .Should().Be("word-baseline-unavailable");
            jsonBlocker.GetProperty("semanticEvidence").GetArrayLength().Should().Be(2);
            jsonBlocker.GetProperty("requiresWordBaseline").GetBoolean().Should().BeTrue();

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Equation Geometry Evidence");
            markdown.Should().Contain("## Remaining Evidence Blockers");
            markdown.Should().Contain("equation-structures-word-baseline-fidelity");
            markdown.Should().Contain("Equation structure visual fidelity");
            markdown.Should().Contain("EquationArray=1");
            markdown.Should().Contain("FunctionApply=2");
            markdown.Should().Contain("spacing=equationarray");
            markdown.Should().Contain("COM ProgID 'Word.Application' is not registered");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WithBaselineComparisons_SurfacesDrawingObjectVisualProofReadiness()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioIds = new[]
            {
                "drawing-objects-complex",
                "object-format-position-size-style",
                "chart-smartart-complex",
                "wordart-watermark-stress",
                "wordart-picture-watermark-layout"
            };
            var wpfRows = scenarioIds
                .Select(scenarioId => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: scenarioId == "chart-smartart-complex" ? 2 : 1))
                .ToList();
            wpfRows.Add(BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "chart-smartart-complex",
                pageNumber: 2,
                pageCount: 2));
            var avaloniaRows = scenarioIds
                .Select(scenarioId => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: scenarioId == "chart-smartart-complex" ? 2 : 1))
                .ToList();
            avaloniaRows.Add(BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                "chart-smartart-complex",
                pageNumber: 2,
                pageCount: 2));

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            var expected = scenarioIds
                .SelectMany(scenarioId => new[]
                {
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        scenarioId == "chart-smartart-complex" ? 2 : 1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        scenarioId == "chart-smartart-complex" ? 2 : 1)
                })
                .ToList();
            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                expected);

            summary.Trust.Passed.Should().BeTrue();
            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();
            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.DrawingObjectProofReadiness.Should().HaveCount(6);
            withBaseline.DrawingObjectProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready"
                && row.WordBaselineStatus == "word-baseline-unavailable=2"
                && row.BaselineReadiness.Contains("without authoritative Word parity", StringComparison.Ordinal)
                && row.Trust.Passed);
            withBaseline.DrawingObjectProofReadiness.Single(row => row.ScenarioId == "chart-smartart-complex" && row.PageNumber == 2)
                .SemanticEvidence.Should().Contain("chart signatures=2");
            withBaseline.DrawingObjectProofReadiness.Single(row => row.ScenarioId == "chart-smartart-complex" && row.PageNumber == 2)
                .SemanticEvidence.Should().Contain("chart data signatures=2");
            withBaseline.DrawingObjectProofReadiness.Single(row => row.ScenarioId == "chart-smartart-complex" && row.PageNumber == 2)
                .SemanticEvidence.Should().Contain("SmartArt layouts=orgchart1/pyramid1");
            withBaseline.DrawingObjectProofReadiness.Single(row => row.ScenarioId == "chart-smartart-complex" && row.PageNumber == 2)
                .SemanticEvidence.Should().Contain("SmartArt polygon nodes=4");
            withBaseline.DrawingObjectProofReadiness.Single(row => row.ScenarioId == "wordart-picture-watermark-layout")
                .SemanticEvidence.Should().Contain("picture watermark");
            withBaseline.WordArtWatermarkProofReadiness.Should().HaveCount(2);
            withBaseline.WordArtWatermarkProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready"
                && row.WordBaselineStatus == "word-baseline-unavailable=2"
                && row.BaselineReadiness.Contains("without authoritative Word parity", StringComparison.Ordinal)
                && row.SemanticEvidence.Contains("wordart", StringComparison.Ordinal)
                && row.Trust.Passed);
            withBaseline.WordArtWatermarkProofReadiness.Single(row => row.ScenarioId == "wordart-watermark-stress")
                .SemanticEvidence.Should().Contain("watermark");
            withBaseline.WordArtWatermarkProofReadiness.Single(row => row.ScenarioId == "wordart-picture-watermark-layout")
                .SemanticEvidence.Should().Contain("picture watermark");
            var smartArtBlocker = withBaseline.RemainingEvidenceBlockers.Single(blocker =>
                blocker.BlockerId == "chart-smartart-complex-word-baseline-fidelity");
            smartArtBlocker.Status.Should().Be(FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
            smartArtBlocker.Area.Should().Be("SmartArt polygon visual fidelity");
            smartArtBlocker.RequiresWordBaseline.Should().BeTrue();
            smartArtBlocker.SemanticEvidence.Should().HaveCount(4);
            smartArtBlocker.SemanticEvidence.Should().OnlyContain(evidence =>
                evidence.Contains("pyramid1", StringComparison.Ordinal) &&
                evidence.Contains("polygonNodes=4", StringComparison.Ordinal));
            var wordArtBlockers = withBaseline.RemainingEvidenceBlockers
                .Where(blocker => blocker.Area == "WordArt/watermark visual fidelity")
                .ToList();
            wordArtBlockers.Should().HaveCount(2);
            wordArtBlockers.Should().OnlyContain(blocker =>
                blocker.Status == FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus
                && blocker.RequiresWordBaseline
                && blocker.SemanticEvidence.Count == 2);

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().BeGreaterThanOrEqualTo(38);
            var readiness = doc.RootElement.GetProperty("drawingObjectProofReadiness");
            readiness.GetArrayLength().Should().Be(6);
            readiness.EnumerateArray()
                .Should().OnlyContain(row =>
                    row.GetProperty("trust").GetProperty("passed").GetBoolean()
                    && row.GetProperty("wordBaselineStatus").GetString() == "word-baseline-unavailable=2");
            var wordArtReadiness = doc.RootElement.GetProperty("wordArtWatermarkProofReadiness");
            wordArtReadiness.GetArrayLength().Should().Be(2);
            wordArtReadiness.EnumerateArray()
                .Should().OnlyContain(row =>
                    row.GetProperty("trust").GetProperty("passed").GetBoolean()
                    && row.GetProperty("wordBaselineStatus").GetString() == "word-baseline-unavailable=2");

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Drawing/Object Visual Proof Readiness");
            markdown.Should().Contain("## WordArt/Watermark Visual Proof Readiness");
            markdown.Should().Contain("word-baseline-unavailable=2");
            markdown.Should().Contain("chart data signatures=2");
            markdown.Should().Contain("SmartArt polygon nodes=4");
            markdown.Should().Contain("wordart-watermark-stress");
            markdown.Should().Contain("wordart-picture-watermark-layout");
            markdown.Should().Contain("WordArt/watermark visual fidelity");
            markdown.Should().Contain("chart-smartart-complex-word-baseline-fidelity");
            markdown.Should().Contain("paired WPF/Avalonia evidence is retained without authoritative Word parity");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SmartArtPolygonNoWordSummary_ReportsFocusedProofReadinessWithoutAuthoritativeWordParity()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            const string scenarioId = "chart-smartart-complex";
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
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        pageNumber: 1,
                        pageCount: 2),
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        pageNumber: 2,
                        pageCount: 2)
                ],
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
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
            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();
            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            var readiness = withBaseline.DrawingObjectProofReadiness.Single(row => row.PageNumber == 2);
            readiness.ScenarioId.Should().Be(scenarioId);
            readiness.Status.Should().Be("paired-renderer-proof-ready");
            readiness.WordBaselineStatus.Should().Be("word-baseline-unavailable=2");
            readiness.BaselineReadiness.Should().Contain("without authoritative Word parity");
            readiness.SemanticEvidence.Should().Contain("SmartArt layouts=orgchart1/pyramid1");
            readiness.SemanticEvidence.Should().Contain("SmartArt geometry=Pyramid");
            readiness.SemanticEvidence.Should().Contain("SmartArt polygon nodes=4");
            readiness.Trust.Passed.Should().BeTrue();

            var blocker = withBaseline.RemainingEvidenceBlockers.Single();
            blocker.BlockerId.Should().Be("chart-smartart-complex-word-baseline-fidelity");
            blocker.Status.Should().Be(FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
            blocker.Area.Should().Be("SmartArt polygon visual fidelity");
            blocker.Reason.Should().Contain("Word.Application");
            blocker.RequiresWordBaseline.Should().BeTrue();
            blocker.SemanticEvidence.Should().OnlyContain(evidence =>
                evidence.Contains("pyramid1", StringComparison.Ordinal) &&
                evidence.Contains("polygonNodes=4", StringComparison.Ordinal));

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Drawing/Object Visual Proof Readiness");
            markdown.Should().Contain("chart-smartart-complex-word-baseline-fidelity");
            markdown.Should().Contain("SmartArt polygon nodes=4");
            markdown.Should().Contain("COM ProgID 'Word.Application' is not registered");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DrawingObjectNoWordSummary_ReportsPairedProofReadinessWithoutAuthoritativeWordParity()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarios = FreeWVisualEvidenceManifestNormalizer.DrawingObjectVisualProofScenarioIds;
            var wpfRows = scenarios
                .Select(scenarioId => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: scenarioId == "chart-smartart-complex" ? 2 : 1))
                .ToList();
            wpfRows.Add(BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "chart-smartart-complex",
                pageNumber: 2,
                pageCount: 2));
            var avaloniaRows = scenarios
                .Select(scenarioId => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    pageNumber: 1,
                    pageCount: scenarioId == "chart-smartart-complex" ? 2 : 1))
                .ToList();
            avaloniaRows.Add(BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                "chart-smartart-complex",
                pageNumber: 2,
                pageCount: 2));
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

            var expected = scenarios
                .SelectMany(scenarioId => new[]
                {
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        scenarioId == "chart-smartart-complex" ? 2 : 1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        scenarioId == "chart-smartart-complex" ? 2 : 1)
                })
                .ToList();
            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                expected);
            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();

            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.DrawingObjectProofReadiness.Should().HaveCount(scenarios.Count + 1);
            withBaseline.DrawingObjectProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready" &&
                row.WordBaselineStatus == "word-baseline-unavailable=2" &&
                row.BaselineReadiness.Contains("without authoritative Word parity", StringComparison.Ordinal) &&
                row.SemanticEvidence.Contains("WPF", StringComparison.Ordinal) &&
                row.SemanticEvidence.Contains("Avalonia", StringComparison.Ordinal) &&
                row.Trust.Passed);

            var drawingRow = withBaseline.DrawingObjectProofReadiness.Single(row =>
                row.ScenarioId == "drawing-objects-complex");
            drawingRow.SemanticEvidence.Should().Contain("6 object(s)");
            drawingRow.SemanticEvidence.Should().Contain("5 grouped child object(s)");
            drawingRow.SemanticEvidence.Should().Contain("grouped child visual signatures=5");
            drawingRow.SemanticEvidence.Should().Contain("2 rendered grouped child effect object(s)");
            var smartArtRow = withBaseline.DrawingObjectProofReadiness.Single(row =>
                row.ScenarioId == "chart-smartart-complex" && row.PageNumber == 2);
            smartArtRow.SemanticEvidence.Should().Contain("SmartArt layouts=orgchart1/pyramid1");
            smartArtRow.SemanticEvidence.Should().Contain("SmartArt geometry=Pyramid");
            smartArtRow.SemanticEvidence.Should().Contain("SmartArt polygon nodes=4");

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            var readiness = doc.RootElement
                .GetProperty("drawingObjectProofReadiness")
                .EnumerateArray()
                .ToArray();
            readiness.Should().Contain(row =>
                row.GetProperty("scenarioId").GetString() == "drawing-objects-complex" &&
                row.GetProperty("wordBaselineStatus").GetString() == "word-baseline-unavailable=2" &&
                row.GetProperty("trust").GetProperty("passed").GetBoolean());

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Drawing/Object Visual Proof Readiness");
            markdown.Should().Contain("| drawing-objects-complex | 1 | paired-renderer-proof-ready |");
            markdown.Should().Contain("word-baseline-unavailable=2");
            markdown.Should().Contain("Word COM or baseline generation unavailable; paired WPF/Avalonia evidence is retained without authoritative Word parity");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GroupedDrawingObjectNoWordSummary_ReportsFocusedGroupedChildProofWithoutUnrelatedRows()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            const string scenarioId = "drawing-objects-complex";
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
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
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
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

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
            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();

            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.DrawingObjectProofReadiness.Should().ContainSingle();
            withBaseline.WordArtWatermarkProofReadiness.Should().BeEmpty();
            var readiness = withBaseline.DrawingObjectProofReadiness.Single();
            readiness.ScenarioId.Should().Be(scenarioId);
            readiness.Status.Should().Be("paired-renderer-proof-ready");
            readiness.WordBaselineStatus.Should().Be("word-baseline-unavailable=2");
            readiness.BaselineReadiness.Should().Contain("without authoritative Word parity");
            readiness.SemanticEvidence.Should().Contain("5 grouped child object(s)");
            readiness.SemanticEvidence.Should().Contain("grouped child kinds=Group0Child0:Image/Group0Child1:Shape/Group0Child2:Chart/Group0Child3:WordArt/Group0Child4:SmartArt");
            readiness.SemanticEvidence.Should().Contain("grouped child visual signatures=5");
            readiness.SemanticEvidence.Should().Contain("2 rendered grouped child effect object(s)");
            readiness.Trust.Passed.Should().BeTrue();
            var blocker = withBaseline.RemainingEvidenceBlockers.Single();
            blocker.BlockerId.Should().Be("drawing-objects-complex-word-baseline-fidelity");
            blocker.Status.Should().Be(FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
            blocker.Area.Should().Be("Grouped drawing/object visual fidelity");
            blocker.RequiresWordBaseline.Should().BeTrue();
            blocker.SemanticEvidence.Should().HaveCount(2);
            blocker.SemanticEvidence.Should().OnlyContain(evidence =>
                evidence.Contains("visualSignatures=5", StringComparison.Ordinal) &&
                evidence.Contains("renderedEffects=2", StringComparison.Ordinal));

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("drawingObjectProofReadiness").GetArrayLength().Should().Be(1);
            doc.RootElement.GetProperty("wordArtWatermarkProofReadiness").GetArrayLength().Should().Be(0);

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Drawing/Object Visual Proof Readiness");
            markdown.Should().Contain("| drawing-objects-complex | 1 | paired-renderer-proof-ready |");
            markdown.Should().Contain("grouped child visual signatures=5");
            markdown.Should().NotContain("chart-smartart-complex");
            markdown.Should().NotContain("wordart-watermark-stress");
            markdown.Should().NotContain("wordart-picture-watermark-layout");
            markdown.Should().NotContain("table-pagination-repeat-header");
            markdown.Should().Contain("drawing-objects-complex-word-baseline-fidelity");
            markdown.Should().Contain("Grouped drawing/object visual fidelity");
            markdown.Should().Contain("COM ProgID 'Word.Application' is not registered");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ObjectFormatNoWordSummary_ReportsFocusedPositionSizeStyleProofWithoutAuthoritativeWordParity()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            const string scenarioId = "object-format-position-size-style";
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
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
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
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

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
            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();

            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.DrawingObjectProofReadiness.Should().ContainSingle();
            withBaseline.WordArtWatermarkProofReadiness.Should().BeEmpty();
            var readiness = withBaseline.DrawingObjectProofReadiness.Single();
            readiness.ScenarioId.Should().Be(scenarioId);
            readiness.Status.Should().Be("paired-renderer-proof-ready");
            readiness.WordBaselineStatus.Should().Be("word-baseline-unavailable=2");
            readiness.BaselineReadiness.Should().Contain("without authoritative Word parity");
            readiness.SemanticEvidence.Should().Contain("3 object(s)");
            readiness.SemanticEvidence.Should().Contain("3 effect object(s)");
            readiness.SemanticEvidence.Should().Contain("3 alt-text object(s)");
            readiness.SemanticEvidence.Should().Contain("kinds=image/shape/wordart");
            readiness.SemanticEvidence.Should().Contain("object format signatures=");
            readiness.SemanticEvidence.Should().Contain("Image:Square:z5:176x112:front");
            readiness.SemanticEvidence.Should().Contain("Shape:Behind:z1:");
            readiness.SemanticEvidence.Should().Contain("WordArt:TopAndBottom:z9:");
            readiness.SemanticEvidence.Should().Contain("effects=Shape:shadow+bevel/Image:shadow+glow+reflection+soft-edge+bevel+artistic:GlowDiffused/WordArt:glow");
            readiness.Trust.Passed.Should().BeTrue();

            var blocker = withBaseline.RemainingEvidenceBlockers.Single();
            blocker.BlockerId.Should().Be("object-format-position-size-style-word-baseline-fidelity");
            blocker.Status.Should().Be(FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
            blocker.Area.Should().Be("Drawing/object visual fidelity");
            blocker.RequiresWordBaseline.Should().BeTrue();
            blocker.SemanticEvidence.Should().HaveCount(2);
            blocker.SemanticEvidence.Should().OnlyContain(evidence =>
                evidence.Contains("altText=3", StringComparison.Ordinal) &&
                evidence.Contains("effects=3", StringComparison.Ordinal));

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("drawingObjectProofReadiness").GetArrayLength().Should().Be(1);
            doc.RootElement.GetProperty("wordArtWatermarkProofReadiness").GetArrayLength().Should().Be(0);

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Drawing/Object Visual Proof Readiness");
            markdown.Should().Contain("| object-format-position-size-style | 1 | paired-renderer-proof-ready |");
            markdown.Should().Contain("object format signatures=");
            markdown.Should().Contain("object-format-position-size-style-word-baseline-fidelity");
            markdown.Should().Contain("COM ProgID 'Word.Application' is not registered");
            markdown.Should().NotContain("drawing-objects-complex");
            markdown.Should().NotContain("chart-smartart-complex");
            markdown.Should().NotContain("table-pagination-repeat-header");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HeaderFooterImageNoWordSummary_ReportsFocusedProofReadinessWithoutAuthoritativeWordParity()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioId = "f2-hf-images";
            var wpfRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    scenarioId,
                    page,
                    pageCount: 2))
                .ToList();
            var avaloniaRows = Enumerable.Range(1, 2)
                .Select(page => BuildFileBackedRow(
                    root,
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    scenarioId,
                    page,
                    pageCount: 2))
                .ToList();

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

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

            summary.Trust.Passed.Should().BeTrue();
            summary.HeaderFooterImageProofReadiness.Should().HaveCount(2);
            summary.HeaderFooterImageProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready"
                && row.WordBaselineStatus == "not-run"
                && row.SemanticEvidence.Contains("header/footer image(s)", StringComparison.Ordinal)
                && row.SemanticEvidence.Contains("image slots=", StringComparison.Ordinal)
                && row.Trust.Passed);

            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();
            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.HeaderFooterImageProofReadiness.Should().HaveCount(2);
            withBaseline.HeaderFooterImageProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready"
                && row.WordBaselineStatus == "word-baseline-unavailable=2"
                && row.BaselineReadiness.Contains("without authoritative Word parity", StringComparison.Ordinal)
                && row.SemanticEvidence.Contains("header/footer image(s)", StringComparison.Ordinal)
                && row.Trust.Passed);
            withBaseline.RemainingEvidenceBlockers.Should().ContainSingle(blocker =>
                blocker.BlockerId == "f2-hf-images-word-baseline-fidelity"
                && blocker.ScenarioId == scenarioId
                && blocker.Status == FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus
                && blocker.Area == "Header/footer image visual fidelity"
                && blocker.RequiresWordBaseline
                && blocker.SemanticEvidence.Count == 4);

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().BeGreaterThanOrEqualTo(43);
            doc.RootElement.GetProperty("headerFooterImageProofReadiness").GetArrayLength().Should().Be(2);
            doc.RootElement.GetProperty("remainingEvidenceBlockers").EnumerateArray()
                .Should().Contain(blocker =>
                    blocker.GetProperty("blockerId").GetString() == "f2-hf-images-word-baseline-fidelity");

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Header/Footer Image Visual Proof Readiness");
            markdown.Should().Contain("| f2-hf-images | 1 | paired-renderer-proof-ready |");
            markdown.Should().Contain("word-baseline-unavailable=2");
            markdown.Should().Contain("Header/footer image visual fidelity");
            markdown.Should().Contain("COM ProgID 'Word.Application' is not registered");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NotePlacementNoWordSummary_ReportsFocusedProofReadinessWithoutAuthoritativeWordParity()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarios = FreeWVisualEvidenceManifestNormalizer.NotePlacementVisualProofScenarioIds;
            var wpfRows = scenarios
                .SelectMany(scenarioId =>
                {
                    var pageCount = FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs;
                    return Enumerable.Range(1, pageCount)
                        .Select(page => BuildFileBackedRow(
                            root,
                            FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                            scenarioId,
                            page,
                            pageCount));
                })
                .ToList();
            var avaloniaRows = scenarios
                .SelectMany(scenarioId =>
                {
                    var pageCount = FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs;
                    return Enumerable.Range(1, pageCount)
                        .Select(page => BuildFileBackedRow(
                            root,
                            FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                            scenarioId,
                            page,
                            pageCount));
                })
                .ToList();

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

            var expected = scenarios
                .SelectMany(scenarioId =>
                {
                    var minimum = FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs;
                    return new[]
                    {
                        new FreeWVisualEvidenceExpectedScenario(
                            FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                            scenarioId,
                            minimum),
                        new FreeWVisualEvidenceExpectedScenario(
                            FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                            scenarioId,
                            minimum)
                    };
                })
                .ToList();
            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                expected);
            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();

            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            var expectedReadinessRows = scenarios
                .Sum(scenarioId => FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.NotePlacementProofReadiness.Should().HaveCount(expectedReadinessRows);
            withBaseline.NotePlacementProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready" &&
                row.WordBaselineStatus == "word-baseline-unavailable=2" &&
                row.BaselineReadiness.Contains("without authoritative Word parity", StringComparison.Ordinal) &&
                row.SemanticEvidence.Contains("WPF", StringComparison.Ordinal) &&
                row.SemanticEvidence.Contains("Avalonia", StringComparison.Ordinal) &&
                row.Trust.Passed);
            withBaseline.NotePlacementProofReadiness
                .Where(row => row.ScenarioId == "f2-footnotes")
                .Should().OnlyContain(row =>
                    row.SemanticEvidence.Contains("footnotes", StringComparison.Ordinal) &&
                    row.SemanticEvidence.Contains("body page", StringComparison.Ordinal));
            withBaseline.NotePlacementProofReadiness
                .Where(row => row.ScenarioId == "f2-endnotes")
                .Should().Contain(row =>
                    row.PageNumber == 2 &&
                    row.SemanticEvidence.Contains("endnotes", StringComparison.Ordinal) &&
                    row.SemanticEvidence.Contains("body page", StringComparison.Ordinal) &&
                    !row.SemanticEvidence.Contains("synthetic page", StringComparison.Ordinal));
            withBaseline.RemainingEvidenceBlockers
                .Where(blocker => blocker.Area == "Note placement visual fidelity")
                .Should().HaveCount(scenarios.Count)
                .And.OnlyContain(blocker =>
                    blocker.Status == FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus &&
                    blocker.RequiresWordBaseline &&
                    blocker.Reason.Contains("Word.Application", StringComparison.Ordinal) &&
                    blocker.CandidateBaselinePaths.Any(path =>
                        path.EndsWith("_p1.png", StringComparison.Ordinal)));

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().BeGreaterThanOrEqualTo(44);
            doc.RootElement.GetProperty("notePlacementProofReadiness").GetArrayLength().Should().Be(expectedReadinessRows);
            doc.RootElement.GetProperty("evidence").EnumerateArray()
                .Should().Contain(row =>
                    row.GetProperty("scenarioId").GetString() == "f2-footnotes" &&
                    row.GetProperty("hasFootnotes").GetBoolean());
            doc.RootElement.GetProperty("evidence").EnumerateArray()
                .Should().Contain(row =>
                    row.GetProperty("scenarioId").GetString() == "f2-endnotes" &&
                    row.GetProperty("hasEndnotes").GetBoolean() &&
                    !row.GetProperty("isSyntheticPage").GetBoolean());

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Note Placement Visual Proof Readiness");
            markdown.Should().Contain("| f2-footnotes | 1 | paired-renderer-proof-ready |");
            markdown.Should().Contain("f2-footnotes-word-baseline-fidelity");
            markdown.Should().Contain("f2-endnotes-word-baseline-fidelity");
            markdown.Should().Contain("Word COM or baseline generation unavailable; paired WPF/Avalonia note placement evidence is retained without authoritative Word parity");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TablePaginationNoWordSummary_ReportsFocusedProofReadinessWithoutAuthoritativeWordParity()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var scenarioIds = new[]
            {
                "table-pagination-repeat-header",
                "table-page-composition-stress"
            };
            var wpfRows = scenarioIds
                .SelectMany(scenarioId => Enumerable.Range(1, scenarioId == "table-page-composition-stress" ? 3 : 2)
                    .Select(page => BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        scenarioId,
                        page,
                        pageCount: scenarioId == "table-page-composition-stress" ? 3 : 2)))
                .ToList();
            var avaloniaRows = scenarioIds
                .SelectMany(scenarioId => Enumerable.Range(1, scenarioId == "table-page-composition-stress" ? 3 : 2)
                    .Select(page => BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        scenarioId,
                        page,
                        pageCount: scenarioId == "table-page-composition-stress" ? 3 : 2)))
                .ToList();

            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                wpfRows,
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                avaloniaRows,
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                scenarioIds
                    .SelectMany(scenarioId => new[]
                    {
                        new FreeWVisualEvidenceExpectedScenario(
                            FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                            scenarioId,
                            scenarioId == "table-page-composition-stress" ? 3 : 2),
                        new FreeWVisualEvidenceExpectedScenario(
                            FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                            scenarioId,
                            scenarioId == "table-page-composition-stress" ? 3 : 2)
                    })
                    .ToList());

            summary.Trust.Passed.Should().BeTrue();
            summary.TablePaginationProofReadiness.Should().HaveCount(5);
            summary.TablePaginationProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready"
                && row.WordBaselineStatus == "not-run"
                && row.Trust.Passed);
            summary.TablePaginationProofReadiness.Should().Contain(row =>
                row.ScenarioId == "table-page-composition-stress"
                && row.SemanticEvidence.Contains("page-border", StringComparison.Ordinal)
                && row.SemanticEvidence.Contains("watermark", StringComparison.Ordinal)
                && row.SemanticEvidence.Contains("NUMPAGES", StringComparison.Ordinal));

            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();
            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            withBaseline.TablePaginationProofReadiness.Should().HaveCount(5);
            withBaseline.TablePaginationProofReadiness.Should().OnlyContain(row =>
                row.Status == "paired-renderer-proof-ready"
                && row.WordBaselineStatus == "word-baseline-unavailable=2"
                && row.BaselineReadiness.Contains("without authoritative Word table parity", StringComparison.Ordinal)
                && row.SemanticEvidence.Contains("rowCells=", StringComparison.Ordinal)
                && row.SemanticEvidence.Contains("repeatedHeaderPages=", StringComparison.Ordinal)
                && row.SemanticEvidence.Contains("keepRows=1", StringComparison.Ordinal)
                && row.SemanticEvidence.Contains("tableSig=", StringComparison.Ordinal)
                && row.SemanticEvidence.Contains("paginationSig=", StringComparison.Ordinal)
                && row.Trust.Passed);
            withBaseline.RemainingEvidenceBlockers.Should().HaveCount(2);
            withBaseline.RemainingEvidenceBlockers.Should().OnlyContain(blocker =>
                scenarioIds.Contains(blocker.ScenarioId)
                && blocker.BlockerId == blocker.ScenarioId + "-word-baseline-fidelity"
                && blocker.Status == FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus
                && blocker.RequiresWordBaseline);
            withBaseline.RemainingEvidenceBlockers.Should().Contain(blocker =>
                blocker.ScenarioId == "table-page-composition-stress"
                && blocker.SemanticEvidence.Any(evidence =>
                    evidence.Contains("page-border", StringComparison.Ordinal)
                    && evidence.Contains("watermark", StringComparison.Ordinal)
                    && evidence.Contains("tableSig=", StringComparison.Ordinal)
                    && evidence.Contains("paginationSig=", StringComparison.Ordinal)));

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().BeGreaterThanOrEqualTo(40);
            doc.RootElement.GetProperty("tablePaginationProofReadiness").GetArrayLength().Should().Be(5);
            doc.RootElement.GetProperty("remainingEvidenceBlockers").EnumerateArray()
                .Should().Contain(blocker =>
                    blocker.GetProperty("blockerId").GetString() == "table-page-composition-stress-word-baseline-fidelity");

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Table Pagination/Page Composition Proof Readiness");
            markdown.Should().Contain("table-page-composition-stress");
            markdown.Should().Contain("word-baseline-unavailable=2");
            markdown.Should().Contain("tableSig=");
            markdown.Should().Contain("paginationSig=");
            markdown.Should().Contain("without authoritative Word table parity");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FloatingWrappingNoWordSummary_ReportsPairedProofReadinessWithoutAuthoritativeWordParity()
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
                        FreeWVisualEvidenceManifestNormalizer.FloatingWrappingWpfScenarioId,
                        pageNumber: 1,
                        pageCount: 1)
                ],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        FreeWVisualEvidenceManifestNormalizer.FloatingWrappingAvaloniaScenarioId,
                        pageNumber: 1,
                        pageCount: 1)
                ],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        FreeWVisualEvidenceManifestNormalizer.FloatingWrappingWpfScenarioId,
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        FreeWVisualEvidenceManifestNormalizer.FloatingWrappingAvaloniaScenarioId,
                        1)
                ]);
            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();

            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            withBaseline.Trust.Passed.Should().BeTrue();
            var readiness = withBaseline.FloatingWrappingProofReadiness.Single();
            readiness.ScenarioId.Should().Be(FreeWVisualEvidenceManifestNormalizer.FloatingWrappingProofScenarioId);
            readiness.Status.Should().Be("paired-renderer-proof-ready");
            readiness.WpfScenarioId.Should().Be("f2-01-float-wrap");
            readiness.AvaloniaScenarioId.Should().Be("page-composition-floating-image");
            readiness.WordBaselineStatus.Should().Be("word-baseline-unavailable=2");
            readiness.BaselineReadiness.Should().Contain("without authoritative Word wrap parity");
            readiness.SemanticEvidence.Should().Contain("WPF 2 floating object(s)");
            readiness.SemanticEvidence.Should().Contain("wraps=Square/Tight");
            readiness.SemanticEvidence.Should().Contain("Avalonia 3 floating object(s)");
            readiness.SemanticEvidence.Should().Contain("wraps=Behind/InFront/TopAndBottom");
            readiness.Trust.Passed.Should().BeTrue();

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(withBaseline);
            using var doc = JsonDocument.Parse(json);
            var jsonReadiness = doc.RootElement.GetProperty("floatingWrappingProofReadiness")[0];
            jsonReadiness.GetProperty("scenarioId").GetString()
                .Should().Be("floating-wrapping-visual-proof");
            jsonReadiness.GetProperty("wordBaselineStatus").GetString()
                .Should().Be("word-baseline-unavailable=2");
            jsonReadiness.GetProperty("trust").GetProperty("passed").GetBoolean().Should().BeTrue();

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Floating/Wrapping Visual Proof Readiness");
            markdown.Should().Contain("| floating-wrapping-visual-proof | 1 | paired-renderer-proof-ready |");
            markdown.Should().Contain("Word COM or baseline generation unavailable; paired WPF/Avalonia floating evidence is retained without authoritative Word wrap parity");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FloatingWrappingNoWordSummary_FailsReadinessWhenTightWrapEvidenceIsMissing()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            var wpfRow = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                FreeWVisualEvidenceManifestNormalizer.FloatingWrappingWpfScenarioId,
                pageNumber: 1,
                pageCount: 1);
            var wpfWithoutTightWrap = wpfRow with
            {
                PageExpectation = wpfRow.PageExpectation with
                {
                    DrawingObjects = wpfRow.PageExpectation.DrawingObjects with
                    {
                        Objects = wpfRow.PageExpectation.DrawingObjects.Objects
                            .Select(snapshot => snapshot.Wrapping == ImageWrapping.Tight
                                ? snapshot with { Wrapping = ImageWrapping.Square }
                                : snapshot)
                            .ToList()
                    }
                }
            };
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [wpfWithoutTightWrap],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        FreeWVisualEvidenceManifestNormalizer.FloatingWrappingAvaloniaScenarioId,
                        pageNumber: 1,
                        pageCount: 1)
                ],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        FreeWVisualEvidenceManifestNormalizer.FloatingWrappingWpfScenarioId,
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        FreeWVisualEvidenceManifestNormalizer.FloatingWrappingAvaloniaScenarioId,
                        1)
                ]);
            var comparisons = summary.Evidence
                .Select(row => FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                    row,
                    FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                    "COM ProgID 'Word.Application' is not registered"))
                .ToList();

            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                comparisons);

            var readiness = withBaseline.FloatingWrappingProofReadiness.Single();
            readiness.Status.Should().Be("floating-wrapping-proof-failed");
            readiness.SemanticEvidence.Should().Contain("wraps=Square");
            readiness.SemanticEvidence.Should().NotContain("wraps=Square/Tight");
            readiness.Trust.Passed.Should().BeFalse();
            readiness.Trust.Failures.Should().Contain("floating/wrapping proof is missing WPF tight-wrap evidence");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FloatingWrappingReadiness_FailsWhenRealWordPngComparisonDrifts()
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
                        FreeWVisualEvidenceManifestNormalizer.FloatingWrappingWpfScenarioId,
                        pageNumber: 1,
                        pageCount: 1)
                ],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                [
                    BuildFileBackedRow(
                        root,
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        FreeWVisualEvidenceManifestNormalizer.FloatingWrappingAvaloniaScenarioId,
                        pageNumber: 1,
                        pageCount: 1)
                ],
                new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        FreeWVisualEvidenceManifestNormalizer.FloatingWrappingWpfScenarioId,
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                        FreeWVisualEvidenceManifestNormalizer.FloatingWrappingAvaloniaScenarioId,
                        1)
                ]);
            var wpfEvidence = summary.Evidence.Single(row =>
                row.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId);
            var avaloniaEvidence = summary.Evidence.Single(row =>
                row.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId);
            var baseline = BuildBgraPixels(2, 2, (10, 10, 10));
            var actual = BuildBgraPixels(2, 2, (240, 240, 240));
            var failedComparison = FreeWVisualBaselineComparisonPlanner.BuildBaselineComparison(
                wpfEvidence,
                "f2-01-float-wrap/f2-01-float-wrap_p1.png",
                FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(wpfEvidence),
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
            failedComparison.Status.Should().Be(FreeWVisualBaselineComparisonPlanner.FailedStatus);

            var withBaseline = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(
                summary,
                [
                    failedComparison,
                    FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
                        avaloniaEvidence,
                        FreeWVisualBaselineComparisonTolerance.WordPngDefault,
                        "COM ProgID 'Word.Application' is not registered")
                ]);

            withBaseline.Trust.Passed.Should().BeFalse();
            var readiness = withBaseline.FloatingWrappingProofReadiness.Single();
            readiness.Status.Should().Be("floating-wrapping-proof-failed");
            readiness.Trust.Passed.Should().BeFalse();
            readiness.Trust.Failures.Should().Contain(f =>
                f.Contains("wpf-fidelity-render/f2-01-float-wrap_p1.png", StringComparison.Ordinal) &&
                f.Contains("changed pixel ratio", StringComparison.Ordinal));
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
            var authority = doc.RootElement.GetProperty("evidenceAuthority");
            authority.GetProperty("authorityLevel").GetString()
                .Should().Be("real-word-png-comparison");
            authority.GetProperty("authoritativeWordPngParityClaimed").GetBoolean()
                .Should().BeTrue();
            authority.GetProperty("trustedEvidenceRows").GetInt32().Should().Be(1);
            authority.GetProperty("comparableWordBaselineRows").GetInt32().Should().Be(1);
            authority.GetProperty("realWordPngComparedRows").GetInt32().Should().Be(1);
            authority.GetProperty("preparatoryEvidenceRows").GetInt32().Should().Be(0);

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("## Evidence Authority");
            markdown.Should().Contain("Authority level: `real-word-png-comparison`");
            markdown.Should().Contain("Authoritative Word PNG parity claimed: yes");
            markdown.Should().Contain("| 1 | 1 | 1 | 0 | 0 | 0 | 0 | 0 |");
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

    private static FreeWVisualEvidenceRow RemoveRenderedGroupChildWordArtEffect(FreeWVisualEvidenceRow row) =>
        row with
        {
            PageExpectation = row.PageExpectation with
            {
                DrawingObjects = row.PageExpectation.DrawingObjects with
                {
                    Effects = row.PageExpectation.DrawingObjects.Effects with
                    {
                        RenderedGroupChildEffectObjectCount = 1,
                        RenderedGroupChildShapeEffectObjectCount = 1,
                        RenderedGroupChildWordArtEffectObjectCount = 0,
                        RenderedGroupChildEffectSummaries = ["GroupChild1:Shape:glow"]
                    }
                }
            }
        };

    private static FreeWVisualEvidenceRow RemoveHeaderFooterImages(FreeWVisualEvidenceRow row) =>
        row with
        {
            PageExpectation = row.PageExpectation with
            {
                HeaderFooters = HeaderFooterVisualPlanner.EmptyExpectation
            }
        };

    private static FreeWVisualEvidenceRow RemoveChapterPrefixFromResolvedPageText(FreeWVisualEvidenceRow row) =>
        row with
        {
            PageExpectation = row.PageExpectation with
            {
                Fields = row.PageExpectation.Fields with
                {
                    HeaderFooterResolvedFieldSignatures = row.PageExpectation.Fields.HeaderFooterResolvedFieldSignatures
                        .Select(signature => signature.Contains("field=PAGE", StringComparison.Ordinal)
                            ? signature.Replace("text=1-2", "text=2", StringComparison.Ordinal)
                            : signature)
                        .ToList()
                }
            }
        };

    private static FreeWVisualEvidenceRow BuildFileBackedRow(
        string root,
        string hostId,
        string scenarioId,
        int pageNumber,
        int pageCount,
        int pixelWidth = 20,
        int pixelHeight = 20,
        TextDocument? documentOverride = null)
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
        var document = documentOverride ?? DocumentForScenario(scenarioId);
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
            hasFootnotes: string.Equals(scenarioId, "f2-footnotes", StringComparison.OrdinalIgnoreCase),
            hasEndnotes: string.Equals(scenarioId, "f2-endnotes", StringComparison.OrdinalIgnoreCase)
                && pageNumber == pageCount,
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

    private static FreeWVisualEvidenceRow OffsetFloatingObjectXDip(
        FreeWVisualEvidenceRow row,
        Func<DocumentFloatingObjectSnapshot, int, double> offsetForObject) =>
        row with
        {
            PageExpectation = row.PageExpectation with
            {
                DrawingObjects = row.PageExpectation.DrawingObjects with
                {
                    Objects = row.PageExpectation.DrawingObjects.Objects
                        .Select((snapshot, index) => snapshot with
                        {
                            Rect = snapshot.Rect with
                            {
                                XDip = snapshot.Rect.XDip + offsetForObject(snapshot, index)
                            }
                        })
                        .ToList()
                }
            }
        };

    private static FreeWVisualEvidenceRow ReplaceGroupedChildVisualSignatures(
        FreeWVisualEvidenceRow row,
        Func<string, string> replace) =>
        row with
        {
            PageExpectation = row.PageExpectation with
            {
                DrawingObjects = row.PageExpectation.DrawingObjects with
                {
                    GroupChildren = row.PageExpectation.DrawingObjects.GroupChildren with
                    {
                        ChildVisualSignatures = row.PageExpectation.DrawingObjects.GroupChildren.ChildVisualSignatures
                            .Select(replace)
                            .ToList()
                    }
                }
            }
        };

    private static Dictionary<string, string> BuildFileBackedHostMetadata(string hostId, string scenarioId)
    {
        var metadata = new Dictionary<string, string> { ["renderer"] = hostId };
        if (!FreeWVisualEvidenceManifestNormalizer.BackstageRendererScenarioIds
            .Contains(scenarioId, StringComparer.OrdinalIgnoreCase))
        {
            return metadata;
        }

        metadata["captureSource"] = hostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId
            ? "wpf-composite-renderer"
            : "avalonia-render-target";
        metadata["backstageWorkflow"] = scenarioId switch
        {
            "backstage-print-preview-fidelity" => "print-preview",
            "backstage-pdf-export-fidelity" => "pdf-export",
            _ => throw new InvalidOperationException($"Unsupported backstage visual evidence scenario: {scenarioId}")
        };
        metadata["backstageArtifactKind"] = scenarioId switch
        {
            "backstage-print-preview-fidelity" => "print-preview-fixed-layout",
            "backstage-pdf-export-fidelity" => "pdf-export-rasterized",
            _ => throw new InvalidOperationException($"Unsupported backstage visual evidence scenario: {scenarioId}")
        };
        metadata["backstagePipeline"] = scenarioId switch
        {
            "backstage-print-preview-fidelity" => "print-preview-fixed-layout-artifact",
            "backstage-pdf-export-fidelity" => "pdf-export-rasterized-artifact",
            _ => throw new InvalidOperationException($"Unsupported backstage visual evidence scenario: {scenarioId}")
        };
        metadata["backstageCaptureRoute"] = scenarioId switch
        {
            "backstage-print-preview-fidelity" => "backstage-print-preview-fixed-layout-capture",
            "backstage-pdf-export-fidelity" => "backstage-pdf-export-raster-capture",
            _ => throw new InvalidOperationException($"Unsupported backstage visual evidence scenario: {scenarioId}")
        };
        return metadata;
    }

    private static TextDocument? DocumentForScenario(string scenarioId) =>
        FreeWVisualEvidencePlanner.NormalizeScenarioId(scenarioId) switch
        {
            "f2-footnotes" => FreeWVisualEvidenceDocumentFactory.BuildFootnotePlacementDocument(),
            "f2-endnotes" => FreeWVisualEvidenceDocumentFactory.BuildEndnotePlacementDocument(),
            "f2-hf-images" => FreeWVisualEvidenceDocumentFactory.BuildMultiSectionHeaderFooterImageDocument(),
            "field-page-number-variants" => FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument(),
            "references-heavy-fields" => FreeWVisualEvidenceDocumentFactory.BuildReferencesHeavyFieldDocument(),
            "legal-reference-section-page-numbers" => FreeWVisualEvidenceDocumentFactory.BuildLegalReferenceSectionPageNumbersDocument(),
            "equation-structures" => FreeWVisualEvidenceDocumentFactory.BuildEquationStructuresDocument(),
            "f2-tracked-changes" => FreeWVisualEvidenceDocumentFactory.BuildTrackedChangesReviewDocument(),
            "f2-comments" => FreeWVisualEvidenceDocumentFactory.BuildCommentsReviewDocument(),
            "review-proofing-visual-depth" => FreeWVisualEvidenceDocumentFactory.BuildReviewProofingVisualDepthDocument(),
            "review-protection-proofing-comments-only" => FreeWVisualEvidenceDocumentFactory.BuildReviewProtectionProofingEvidenceDocument(),
            "review-compare-visual-proof" => FreeWVisualEvidenceDocumentFactory.BuildReviewCompareVisualProofDocument(),
            "review-combine-visual-proof" => FreeWVisualEvidenceDocumentFactory.BuildReviewCombineVisualProofDocument(),
            "table-layout-complex" => FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument(),
            "table-pagination-repeat-header" => FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument(),
            "table-page-composition-stress" => FreeWVisualEvidenceDocumentFactory.BuildTablePageCompositionStressDocument(),
            "drawing-objects-complex" => FreeWVisualEvidenceDocumentFactory.BuildDrawingObjectsCompositionDocument(),
            "object-format-position-size-style" => FreeWVisualEvidenceDocumentFactory.BuildObjectFormatPositionSizeStyleDocument(),
            "chart-smartart-complex" => FreeWVisualEvidenceDocumentFactory.BuildChartSmartArtCompositionDocument(),
            "wordart-watermark-stress" => FreeWVisualEvidenceDocumentFactory.BuildWordArtWatermarkStressDocument(),
            "wordart-picture-watermark-layout" => FreeWVisualEvidenceDocumentFactory.BuildWordArtPictureWatermarkLayoutDocument(),
            "f2-section-landscape" => FreeWVisualEvidenceDocumentFactory.BuildSectionGeometryDocument(),
            "f2-01-float-wrap" => FreeWVisualEvidenceDocumentFactory.BuildFloatingWrapDocument(),
            "backstage-print-preview-fidelity" => FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument(
                "Backstage Print Preview Fidelity",
                "Synthetic print preview renderer capture"),
            "backstage-pdf-export-fidelity" => FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument(
                "Backstage PDF Export Fidelity",
                "Synthetic PDF export renderer capture"),
            "page-composition-floating-image" => FreeWVisualEvidenceDocumentFactory.BuildFloatingImageEvidenceDocument(),
            _ => null
        };

    private static IEnumerable<Paragraph> ParagraphsInDocument(TextDocument document)
    {
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                yield return paragraph;
            }
            else if (block is Table table)
            {
                foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var cellParagraph in cell.Paragraphs)
                            yield return cellParagraph;
            }
        }
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
