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
            "f2-footnotes",
            "f2-endnotes",
            "f2-columns",
            "f2-border-watermark",
            "f2-section-landscape",
            "f2-tracked-changes",
            "f2-comments",
            "table-layout-complex",
            "drawing-objects-complex",
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

        var floatingScenario = FreeWVisualEvidencePlanner.ResolveScenario("page-composition-floating-image");
        floatingScenario.Composition.ExpectsFloatingObjects.Should().BeTrue();

        var previewScenario = FreeWVisualEvidencePlanner.ResolveScenario("backstage-print-preview-fidelity");
        previewScenario.ExpectedFeatureTags.Should().Contain(["backstage", "print-preview", "fixed-layout"]);
        previewScenario.ExpectedOutputNamePattern.Should().Be("backstage-print-preview_p{page}.png");
        previewScenario.MinimumExpectedOutputs.Should().Be(2);

        var pdfScenario = FreeWVisualEvidencePlanner.ResolveScenario("backstage-pdf-export-fidelity");
        pdfScenario.ExpectedFeatureTags.Should().Contain(["backstage", "pdf-export", "pdf-rasterized"]);
        pdfScenario.ExpectedOutputNamePattern.Should().Be("backstage-pdf-export_p{page}.png");
        pdfScenario.MinimumExpectedOutputs.Should().Be(2);

        var columnsScenario = FreeWVisualEvidencePlanner.ResolveScenario("f2-columns");
        columnsScenario.Composition.ExpectsColumns.Should().BeTrue();

        var borderScenario = FreeWVisualEvidencePlanner.ResolveScenario("f2-border-watermark");
        borderScenario.Composition.ExpectsPageBorder.Should().BeTrue();
        borderScenario.Composition.ExpectsWatermark.Should().BeTrue();

        var tableScenario = FreeWVisualEvidencePlanner.ResolveScenario("table-layout-complex");
        tableScenario.ExpectedFeatureTags.Should().Contain(["table-layout", "merged-cells", "repeat-header-row"]);
        tableScenario.ExpectedOutputNamePattern.Should().Be("table-layout-complex_p{page}.png");
        tableScenario.Composition.ExpectsTables.Should().BeTrue();

        var drawingScenario = FreeWVisualEvidencePlanner.ResolveScenario("drawing-objects-complex");
        drawingScenario.ExpectedFeatureTags.Should().Contain(["drawing-objects", "charts", "smartart", "wordart"]);
        drawingScenario.ExpectedOutputNamePattern.Should().Be("drawing-objects-complex_p{page}.png");
        drawingScenario.Composition.ExpectsFloatingObjects.Should().BeTrue();
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
    public void DefaultExpectedScenarios_RequiresPairedTableRendererEvidence()
    {
        var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;

        foreach (var scenarioId in FreeWVisualEvidenceManifestNormalizer.TableRendererScenarioIds)
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
        expectation.DrawingObjects.FloatingObjectCount.Should().Be(5);
        expectation.DrawingObjects.BehindTextCount.Should().Be(1);
        expectation.DrawingObjects.InFrontCount.Should().Be(4);
        expectation.DrawingObjects.HasShapes.Should().BeTrue();
        expectation.DrawingObjects.HasCharts.Should().BeTrue();
        expectation.DrawingObjects.HasSmartArt.Should().BeTrue();
        expectation.DrawingObjects.HasWordArt.Should().BeTrue();
        expectation.DrawingObjects.HasGroups.Should().BeTrue();
        expectation.DrawingObjects.HasSquareWrap.Should().BeTrue();
        expectation.DrawingObjects.HasTopAndBottomWrap.Should().BeTrue();
        expectation.DrawingObjects.HasZOrder.Should().BeTrue();
        expectation.DrawingObjects.Objects.Select(o => o.TypeTag).Should().Contain([
            "Shape",
            "Chart",
            "SmartArt",
            "WordArt",
            "Group"]);
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
        root.GetProperty("schemaVersion").GetInt32().Should().Be(4);
        root.GetProperty("product").GetString().Should().Be("FreeW");
        root.GetProperty("scenarios").GetArrayLength().Should().Be(1);
        var evidence = root.GetProperty("evidence")[0];
        evidence.GetProperty("scenarioId").GetString().Should().Be("f2-hf-basic");
        evidence.GetProperty("trust").GetProperty("passed").GetBoolean().Should().BeTrue();
        evidence.GetProperty("pageExpectation").GetProperty("features").GetProperty("section").GetProperty("ownerId")
            .GetString().Should().Be("section-1");
        evidence.GetProperty("pageExpectation").GetProperty("drawingObjects").GetProperty("floatingObjectCount")
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
                && f.Contains("expected at least 1", StringComparison.Ordinal));
            Action act = () => FreeWVisualEvidenceManifestNormalizer.EnsureSummaryTrusted(summary);
            act.Should().Throw<InvalidOperationException>().WithMessage("*f2-comments*");
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
                && f.Contains("expected at least 2", StringComparison.Ordinal)
                && f.Contains("found 1", StringComparison.Ordinal));
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("avalonia-page-layout-shot/backstage-print-preview-fidelity", StringComparison.Ordinal)
                && f.Contains("expected at least 2", StringComparison.Ordinal)
                && f.Contains("found 0", StringComparison.Ordinal));
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
            withBaseline.BaselineComparisons.Single().CandidateBaselinePaths.Should().Contain([
                "f2-hf-basic/f2-hf-basic_p1.png",
                "f2-hf-basic_p1.png"]);
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
            doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(5);
            var baselineComparison = doc.RootElement.GetProperty("baselineComparisons")[0];
            baselineComparison.GetProperty("status").GetString().Should().Be("passed");
            baselineComparison.GetProperty("tolerance").GetProperty("name").GetString()
                .Should().Be("word-png-default");
            baselineComparison.GetProperty("metrics").GetProperty("meanAbsoluteChannelDelta")
                .GetDouble().Should().Be(0);

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(withBaseline);
            markdown.Should().Contain("Word Baseline Comparison");
            markdown.Should().Contain("word-png-default");
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
        var page = document?.Page ?? PageForScenario(scenarioId);
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            scenarioId,
            page,
            pageNumber,
            pageCount,
            outputName,
            scenario.LayoutKind,
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
            HostMetadata: new Dictionary<string, string> { ["renderer"] = hostId });

        return FreeWVisualEvidencePlanner.BuildEvidenceRow(capture);
    }

    private static TextDocument? DocumentForScenario(string scenarioId) =>
        FreeWVisualEvidencePlanner.NormalizeScenarioId(scenarioId) switch
        {
            "table-layout-complex" => FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument(),
            "drawing-objects-complex" => FreeWVisualEvidenceDocumentFactory.BuildDrawingObjectsCompositionDocument(),
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

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "FreeWVisualEvidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
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
