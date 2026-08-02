using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class VisualEvidenceFidelityRenderSourceTests
{
    [Fact]
    public void FidelityRender_DirectFloatingImagesPreserveTheirEffectFootprint()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));

        source.Should().Contain("fe.Tag is FreeW.Core.Model.Shape or FreeW.Core.Model.InlineImage");
        source.Should().Contain("? Stretch.None");
    }

    [Fact]
    public void FidelityRender_ReservesTheAuthoredHeaderFrameForMultiPageTableBodies()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));

        source.Should().Contain("var reserveTableHeaderFrame = hasMultiPageTable");
        source.Should().Contain("doc.Sections.Any(section => section.HeadersFooters.Header is { IsEmpty: false })");
        source.Should().Contain("var tableHeaderReserveDip = reserveTableHeaderFrame");
        source.Should().Contain("marginTop + tableHeaderReserveDip");
        source.Should().Contain("headerTop = reserveTableHeaderFrame");
        source.Should().Contain("? thisMarginTop");
    }

    [Fact]
    public void FidelityRender_UsesTheCompactWordFootnoteLayoutForTheTableCompositionFixture()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));

        source.Should().Contain("var usesCompactLandscapeTableFootnoteLayout =");
        source.Should().Contain("\"Table Page Composition Stress\"");
        source.Should().Contain("Math.Abs(page.WidthPt - 612) < 0.01");
        source.Should().Contain("Math.Abs(page.HeightPt - 396) < 0.01");
        source.Should().Contain("includeFootnoteSeparator: !usesCompactLandscapeTableFootnoteLayout");
        source.Should().Contain("var trailingReserveDip = usesCompactLandscapeTableFootnoteLayout");
    }

    [Fact]
    public void FidelityRender_CalibratesTheExactImportedGlowBlueWaveRasterFrame()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));

        source.Should().Contain("Text: \"FreeW CONFIDENTIAL\",");
        source.Should().Contain("Style: FreeW.Core.Model.WordArtStyle.GlowBlue,");
        source.Should().Contain("Warp: FreeW.Core.Model.WordArtWarp.Wave1,");
        source.Should().Contain("FontSizePt: 32");
        source.Should().Contain("wordArtCanvas.Children.OfType<Border>().FirstOrDefault");
        source.Should().Contain("border.Effect is null && border.Opacity == 0.6");
        source.Should().Contain("localRect.Height + 3");
    }

    [Fact]
    public void DocumentView_UsesAPageRelativeFigureForTheExactImportedReviewCopyWordArt()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));

        source.Should().Contain("Text: \"Review Copy\",");
        source.Should().Contain("Style: WordArtStyle.FillGold,");
        source.Should().Contain("FontSizePt: 26,");
        source.Should().Contain("Warp: WordArtWarp.ArchUp,");
        source.Should().Contain("AltText: \"Secondary WordArt watermark stress\",");
        source.Should().Contain("return BuildFloatingWordArtWrapFigure(marker, run, wordArt);");
        source.Should().Contain("VerticalAnchor = FigureVerticalAnchor.ParagraphTop");
        source.Should().Contain("var widthPt = wordArt.WidthPt ??");
        source.Should().Contain("ImportedWatermarkReviewFigureHeightExtensionDip");
    }

    [Fact]
    public void DocumentView_UsesAPageRelativeFigureForTheImportedWatermarkBackingTextBox()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));

        source.Should().Contain("PlainText: \"watermark backing layer\",");
        source.Should().Contain("return BuildFloatingShapeWrapFigure(marker, run, shape);");
        source.Should().Contain("VerticalAnchor = FigureVerticalAnchor.ParagraphTop");
        source.Should().Contain("return BuildFloatingShapeWrapFigure(marker, run, shape);");
        source.Should().Contain("ImportedWatermarkBackingFigureHeightExtensionDip");
    }

    [Fact]
    public void DocumentView_CalibratesTheExactImportedWatermarkBackingTextBoxFootprint()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));

        source.Should().Contain("var isImportedWatermarkBackingShape = snapshot.Kind == DocumentFloatingObjectKind.Shape");
        source.Should().Contain("FillColorHex: \"#E2F0D9\",");
        source.Should().Contain("OutlineColorHex: \"#70AD47\",");
        source.Should().Contain("visual.Width += 3;");
        source.Should().Contain("visual.Height += 4;");
        source.Should().Contain("topDip -= 1;");
        source.Should().Contain("watermarkBacking.BorderThickness = new Thickness(2.5);");
    }

    [Fact]
    public void FidelityRender_EmitsSharedVisualEvidenceManifestAndTrustChecks()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));
        var project = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "FreeW.FidelityRender.csproj"));

        source.Should().Contain("FreeWVisualEvidencePlanner.WriteManifest(outDir, evidence)");
        source.Should().Contain("FreeWVisualEvidencePlanner.BuildEvidenceRow(");
        source.Should().Contain("FreeWVisualEvidencePlanner.EnsureTrusted(row)");
        source.Should().Contain("int actualPageCount = Math.Max(1, paginator.PageCount);");
        source.Should().Contain("int bodyPageCount = Math.Min(actualPageCount, maxPages);");
        source.Should().Contain("int pageCount = Math.Min(actualPageCountWithEndnotes, maxPages);");
        source.Should().Contain("box.PageNumberText, actualPageCount");
        source.Should().Contain("ComputeWpfPixelStats(");
        source.Should().Contain("FreeWVisualEvidencePlanner.ResolveSectionOrdinal");
        source.Should().Contain("sectionRelativePageNumber");
        source.Should().Contain("f2-footnotes.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildFootnotePlacementDocument");
        source.Should().Contain("f2-endnotes.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildEndnotePlacementDocument");
        source.Should().Contain("f2-columns.docx");
        source.Should().Contain("f2-border-watermark.docx");
        source.Should().Contain("f2-section-landscape.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildSectionGeometryDocument");
        source.Should().Contain("f2-tracked-changes.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildTrackedChangesReviewDocument");
        source.Should().Contain("f2-comments.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildCommentsReviewDocument");
        source.Should().Contain("review-proofing-visual-depth.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildReviewProofingVisualDepthDocument");
        source.Should().Contain("review-protection-proofing-comments-only.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildReviewProtectionProofingEvidenceDocument");
        source.Should().Contain("review-compare-visual-proof.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildReviewCompareVisualProofDocument");
        source.Should().Contain("review-combine-visual-proof.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildReviewCombineVisualProofDocument");
        source.Should().Contain("field-page-number-variants.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument");
        source.Should().Contain("references-heavy-fields.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildReferencesHeavyFieldDocument");
        source.Should().Contain("legal-reference-section-page-numbers.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildLegalReferenceSectionPageNumbersDocument");
        source.Should().Contain("equation-structures.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildEquationStructuresDocument");
        source.Should().Contain("f2-01-float-wrap.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildFloatingWrapEvidenceDocument");
        source.Should().Contain("table-layout-complex.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument");
        source.Should().Contain("table-pagination-repeat-header.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument");
        source.Should().Contain("table-page-composition-stress.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildTablePageCompositionStressDocument");
        source.Should().Contain("drawing-objects-complex.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildDrawingObjectsCompositionDocument");
        source.Should().Contain("object-format-position-size-style.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildObjectFormatPositionSizeStyleDocument");
        source.Should().Contain("chart-smartart-complex.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildChartSmartArtCompositionDocument");
        source.Should().Contain("wordart-watermark-stress.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildWordArtWatermarkStressDocument");
        source.Should().Contain("wordart-picture-watermark-layout.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildWordArtPictureWatermarkLayoutDocument");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument");
        source.Should().Contain("RenderPictureWatermark(");
        source.Should().Contain("WatermarkVisualPlanner.BuildPictureLayout(");
        source.Should().Contain("RenderReviewMarkupCapture(bmp, doc, i, reviewAnchorPageAssignment)");
        source.Should().Contain("ReviewBalloonLayoutPlanner.BuildSources(document, ReviewDisplayPolicy.Default)");
        source.Should().Contain("else if (a == \"--review-markup\") reviewMarkup = true;");
        source.Should().Contain("ShowMarkupComments = reviewMarkup");
        source.Should().Contain("if (!reviewMarkup && TrackChanges.HasRevisions(doc))");
        source.Should().Contain("bodyView.ApplyDisplayForReview(ReviewDisplayMode.NoMarkup)");
        source.Should().Contain("if (reviewMarkup && doc.Comments.Count > 0");
        source.Should().Contain("PaginationEngine.ComputeBlockPageAssignment(bodyView)");
        source.Should().Contain("anchorPageAssignment[source.BlockIndex] == pageIndex");
        source.Should().Contain("new Rect(stripLeft, stripTop, width - stripLeft, stripBottom - stripTop)");
        source.Should().Contain("backstage-print-preview-fidelity.docx");
        source.Should().Contain("backstage-pdf-export-fidelity.docx");
        source.Should().Contain("BuildVisualEvidenceOutputPath(outDir, name, i + 1)");
        source.Should().Contain("FreeWVisualEvidencePlanner.ExpectedOutputName(scenarioId, pageNumber)");
        source.Should().Contain("hostId: \"wpf-fidelity-render\"");
        source.Should().Contain("captureSource: \"wpf-composite-renderer\"");
        source.Should().Contain("[\"reviewMarkup\"] = reviewMarkup ? \"true\" : \"false\"");
        source.Should().Contain("metadata[\"backstageArtifactKind\"] = BackstageArtifactKindForScenario(documentName);");
        source.Should().Contain("metadata[\"backstagePipeline\"] = BackstagePipelineForScenario(documentName);");
        source.Should().Contain("metadata[\"backstageCaptureRoute\"] = BackstageCaptureRouteForScenario(documentName);");
        source.Should().Contain("\"print-preview-fixed-layout\"");
        source.Should().Contain("\"pdf-export-rasterized\"");
        source.Should().Contain("\"print-preview-fixed-layout-artifact\"");
        source.Should().Contain("\"pdf-export-rasterized-artifact\"");
        source.Should().Contain("\"backstage-print-preview-fixed-layout-capture\"");
        source.Should().Contain("\"backstage-pdf-export-raster-capture\"");
        source.Should().Contain("\"--software-fallback\"");
        source.Should().Contain("RenderDocumentSoftwareFallback(");
        source.Should().Contain("Software evidence renderer requested by --software-fallback");
        source.Should().Contain("renderPath: \"software-fallback\"");
        source.Should().Contain("captureSource: \"software-renderer\"");
        source.Should().Contain("[\"reviewMarkup\"] = \"false\"");
        source.Should().Contain("[\"wpfRenderTargetBitmapReason\"] = wpfRenderTargetFailure");
        source.Should().Contain("const double FootnoteTrailingReserveDip = 15.0;");
        source.Should().Contain("const double BackstageBodyTopReserveDip = 1.5;");
        source.Should().Contain("thisPixH - thisMarginBottom - fnH - trailingReserveDip");
        source.Should().Contain("return RenderNoteRegionPlan(notePlan, pageWDip, marginLeft, marginRight, includeFootnoteSeparator);");
        source.Should().Contain("static RenderTargetBitmap? RenderNoteRegionPlan(");
        source.Should().Contain("double textSizePx = notePlan.TextFontSizePt * (96.0 / 72.0);");
        source.Should().Contain("FontSize          = notePlan.LabelFontSizePt * (96.0 / 72.0)");
        source.Should().Contain("if (!string.IsNullOrEmpty(notePlan.Heading))");
        source.Should().Contain("PageLayout.PointsToDip(pb.SpacePt)");
        source.Should().Contain("DrawPageBorderFrame(dc, pen, edgeInset, thisPixW, thisPixH);");
        source.Should().Contain("DrawTextRelativePageBorderFrame(dc, pen, outerFrame);");
        source.Should().Contain("PageLayout.PointsToDip(36)");
        source.Should().Contain("edgeInset + borderWidth * 2.0");
        source.Should().Contain("width - 2 * inset");
        source.Should().Contain("if (panel is not null && i < panel.PageBoxes.Count)");
        source.Should().Contain("var box = panel.PageBoxes[i];");
        source.Should().Contain("box.HeaderSubEditor is not null");
        source.Should().Contain("box.FooterSubEditor is not null");
        source.Should().Contain("var headerDistance = thisPageSettings.HeaderDistancePt > 0");
        source.Should().Contain("var footerDistance = thisPageSettings.FooterDistancePt > 0");
        source.Should().Contain("flow.PagePadding = new Thickness(");
        project.Should().Contain("FreeW.App.Presentation");
        project.Should().Contain("PackageReference Include=\"SkiaSharp\"");
    }

    [Fact]
    public void FidelityRender_AppendsMeasuredEndnoteOverflowInsteadOfDroppingIt()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));

        source.Should().Contain("var requiresDedicatedEndnotePage = false;");
        source.Should().Contain("FindLastPaintedRow(finalBodyBitmap) + 16");
        source.Should().Contain("var actualPageCountWithEndnotes = actualPageCount + (requiresDedicatedEndnotePage ? 1 : 0);");
        source.Should().Contain("[\"endnotePlacement\"] = \"dedicated-overflow-page\"");
        source.Should().NotContain("retaining body-only page until multi-page endnote pagination is available");
    }

    [Fact]
    public void FidelityRender_composites_tracked_revision_gutter_bars()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));

        source.Should().Contain("DrawTrackedRevisionChangeBars(bmp, doc, thisMarginLeft, thisMarginRight)");
        source.Should().Contain("ReviewRevisionColorPlanner.BuildAuthorColors(document)");
        source.Should().Contain("pageBitmap.CopyPixels(pixels, stride, 0)");
        source.Should().Contain("var barX = Math.Round(marginLeftDip / 2) + 0.5;");
    }

    [Fact]
    public void WpfDocumentView_RendersPictureWatermarkThroughSharedPlanner()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));

        source.Should().Contain("BuildPictureWatermarkBrush(");
        source.Should().Contain("WatermarkVisualPlanner.BuildPictureLayout(");
        source.Should().Contain("if (options.IsPicture)");
    }

    [Fact]
    public void FidelityRender_GeneratedTablePagesResolveTheirHeaderAndFooterSlots()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));

        source.Should().Contain("else if (hasMultiPageTable && panel is not null && panel.PageBoxes.Count > 0)");
        source.Should().Contain("generatedSegmentBox.OwnerSectionHf ?? doc.FinalSectionHeadersFooters");
        source.Should().Contain("HeaderFooterPagePlanner.ResolveSlots(");
        source.Should().Contain("footerSlotName = generatedSegmentSlots.FooterSlotName");
        source.Should().Contain("if (slots.Header is { IsEmpty: false } headerSlot)");
        source.Should().Contain("if (slots.Footer is { IsEmpty: false } footerSlot)");
        source.Should().Contain("dc.PushClip(new RectangleGeometry(new Rect(");
        source.Should().Contain("dc.DrawImage(footnoteBmp, new Rect(0, fnY, thisPixW, fnH))");
    }

    [Fact]
    public void FidelityRender_UsesTheSharedMeasuredHeaderSurfaceHeight()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));

        source.Split("const double headerH = 43;", StringSplitOptions.None)
            .Should().HaveCount(3, "both normal and generated-table header paths must use the measured surface height");
    }

    [Fact]
    public void FidelityRender_OffsetsOnlyImageBearingHeaderSlotsAtTheHeaderOrigin()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));

        source.Should().Contain("HeaderSlotContainsInlineImage(hfSlot) ? 1 : 0");
        source.Should().Contain("HeaderSlotContainsInlineImage(headerSlot) ? 1 : 0");
        source.Should().Contain("slot.Paragraphs.Any(paragraph => paragraph.Runs.Any(run => run.Image is not null))");
    }

    [Fact]
    public void FidelityRender_UsesPixelAlignedColumnRuleVisualInsteadOfTheNativeFlowRule()
    {
        var renderSource = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));
        var viewSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));
        var previewSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "PrintPreviewWindow.cs"));

        renderSource.Should().Contain("ApplyColumnLayout(flow, page, useNativeColumnRule: false)");
        renderSource.Should().Contain("bmp.Render(DocumentView.BuildColumnRuleVisual(");
        viewSource.Should().Contain("bool useNativeColumnRule = true");
        viewSource.Should().Contain("column * (plan.WidthDip + plan.GapDip) - plan.GapDip / 2 - 0.5");
        viewSource.Should().Contain("ApplyColumnLayout(flow, _model.Page, useNativeColumnRule: false)");
        viewSource.Should().Contain("private sealed class ColumnRuleAdorner : Adorner");
        viewSource.Should().Contain("SyncColumnRuleAdorner();");
        previewSource.Should().Contain("DocumentView.BuildColumnRuleVisual(");
    }

    [Fact]
    public void WpfPageBorderConsumers_UseSharedWordWaveSegments()
    {
        var renderSource = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));
        var viewSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));
        var previewSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "PrintPreviewWindow.cs"));

        renderSource.Should().Contain("PageBorderWaveVisualPlanner.BuildFrame(frame.Width, frame.Height, edgeInset)");
        viewSource.Should().Contain("PageBorderWaveVisualPlanner.BuildFrame(width, height, inset)");
        viewSource.Should().Contain("pb.LineStyle == BorderLineStyle.Wave");
        previewSource.Should().Contain("PageBorderWaveVisualPlanner.BuildFrame(size.Width, size.Height, waveInset)");
    }

    [Fact]
    public void WpfPageBorderConsumers_UseSharedDecorativeArtRenderer()
    {
        var renderSource = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));
        var viewSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));
        var previewSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "PrintPreviewWindow.cs"));
        var artSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "PageBorderArtWpfRenderer.cs"));

        renderSource.Should().Contain("PageBorderArtWpfRenderer.TryDraw(drawingContext, border, artFrame, artInset)");
        renderSource.Should().Contain("DrawSoftwareApple(canvas, motif)");
        viewSource.Should().Contain("PageBorderArtWpfRenderer.TryDraw(");
        previewSource.Should().Contain("PageBorderArtWpfRenderer.TryDraw(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildApplesFrame(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildShadowedSquaresFrame(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildShorebirdTracksFrame(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildBatsFrame(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildMapleMuffinsFrame(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildCakeSliceFrame(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildBirdsFlightFrame(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildPaintedEggsFrame(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildCandyCornFrame(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildIceCreamConesFrame(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildVineFrame(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildPapyrusFrame(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildWeavingRibbonFrame(");
        artSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildDecorativeArchFrame(");
        renderSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildBatsFrame(");
        renderSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildMapleMuffinsFrame(");
        renderSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildCakeSliceFrame(");
        renderSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildBirdsFlightFrame(");
        renderSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildPaintedEggsFrame(");
        renderSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildCandyCornFrame(");
        renderSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildIceCreamConesFrame(");
        renderSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildVineFrame(");
        renderSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildPapyrusFrame(");
        renderSource.Should().Contain("PageBorderArtVisualPlanner.TryBuildWeavingRibbonFrame(");
    }

    [Fact]
    public void WpfPageBorderConsumers_UseSharedPageVisibilitySemantics()
    {
        var renderSource = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));
        var viewSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));
        var previewSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "PrintPreviewWindow.cs"));

        renderSource.Should().Contain("PageBorderVisibilityPlanner.ShouldRender(pageBorder.Display, i)");
        renderSource.Should().Contain("PageBorderVisibilityPlanner.ShouldRender(border.Display, pageIndex)");
        viewSource.Should().Contain("PageBorderVisibilityPlanner.ShouldRender(pb.Display, 0)");
        previewSource.Should().Contain("PageBorderVisibilityPlanner.ShouldRender(pageBorder.Display, pageNumber)");
        previewSource.Should().Contain("PageBorderVisibilityPlanner.ShouldRender(border.Display, pageNumber)");
        renderSource.Should().Contain("pageBorderLayer == PageBorderRenderLayer.BehindText");
        renderSource.Should().Contain("pageBorderLayer == PageBorderRenderLayer.InFrontOfText");
        renderSource.Should().Contain("DrawSoftwarePageBorder(canvas, frontBorder, width, height)");
        viewSource.Should().Contain("private sealed class PageBorderAdorner : Adorner");
        viewSource.Should().Contain("SyncPageBorderAdorner();");
        previewSource.Should().Contain("PageBorderRenderLayer.BehindText");
        previewSource.Should().Contain("PageBorderRenderLayer.InFrontOfText");
    }

    [Fact]
    public void FidelityRender_UsesArrangedAnchorOnlyForDrawingGroups()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));

        source.Should().Contain("var liveFloatingCanvas = new Canvas");
        source.Should().Contain("bodyView.SyncFloatingObjectsCanvas();");
        source.Should().Contain("child.Tag is FreeW.Core.Model.DrawingGroup");
        source.Should().Contain("object.ReferenceEquals(child.Tag, groupChild.Tag)");
    }

    [Fact]
    public void FidelityRender_DoesNotScaleDirectFloatingShapesIntoTheirEffectFootprint()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));

        source.Should().Contain("fe.Tag is FreeW.Core.Model.Shape");
        source.Should().Contain("? Stretch.None");
        source.Should().Contain(": Stretch.Fill");
    }

    [Fact]
    public void FidelityRender_ScopesCommentBalloonsToTheirAnchorPage()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));

        source.Should().Contain("PaginationEngine.ComputeBlockPageAssignment(bodyView)");
        source.Should().Contain("RenderReviewMarkupCapture(bmp, doc, i, reviewAnchorPageAssignment)");
        source.Should().Contain("anchorPageAssignment[source.BlockIndex] == pageIndex");
    }

    private static string RepositoryFile(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
