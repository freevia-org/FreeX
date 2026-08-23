using System.Text.Json;

namespace FreeP.App.Compositor.Tests;

public sealed class Wave194Deck17Slide02TopologyTests
{
    private const string RetainedEvidenceRoot =
        "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823";

    [Fact]
    public void Deck17Slide02_OfficeTopology_MatchesTheRetainedEvidenceSlice()
    {
        var topologyPath = TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "docs", "parity", "evidence", "freep-wave194-deck17-slide02-topology-20260823",
            "topology.json");
        using var topology = JsonDocument.Parse(File.ReadAllText(topologyPath));
        var root = topology.RootElement;

        root.GetProperty("schema").GetString()
            .Should().Be("freep.parity.wave194.deck17-slide02.topology.v2");
        root.GetProperty("sourceRevision").GetString().Should()
            .Be("bb454fbc7d4d8b4588a4d2ed0adec678527e3936");
        root.GetProperty("deck").GetString().Should().Be("17-bullets-autofit");
        root.GetProperty("slide").GetString().Should().Be("slide-02");

        root.GetProperty("officeReference").GetProperty("sourceRevision").GetString().Should()
            .Be("62fa14b152e3318c09b8696e9edd778c5eb1ab18");
        root.GetProperty("officeReference").GetProperty("slide02PngSha256").GetString().Should()
            .Be("2828a7a3ced739e6b5b36f53aa7309df35ceb8c7f898b7bf8fb29480ab012ee5");

        var themeEvidence = root.GetProperty("theme");
        var majorLatin = themeEvidence.GetProperty("majorLatin").GetString();
        var minorLatin = themeEvidence.GetProperty("minorLatin").GetString();
        majorLatin.Should().Be("Aptos Display");
        minorLatin.Should().Be("Aptos");

        var modelEvidence = root.GetProperty("model");
        var titleEvidence = modelEvidence.GetProperty("title");
        var bodyEvidence = modelEvidence.GetProperty("body");
        titleEvidence.GetProperty("rawRunFontFamily").ValueKind.Should().Be(JsonValueKind.Null);
        titleEvidence.GetProperty("effectiveFontFamily").GetString().Should().Be(majorLatin);
        titleEvidence.GetProperty("fontFamilySource").GetString().Should().Be("theme.majorLatin");
        titleEvidence.GetProperty("effectiveFontSizePt").GetDouble().Should().BeApproximately(28.0, 0.01);
        bodyEvidence.GetProperty("rawRunFontFamily").ValueKind.Should().Be(JsonValueKind.Null);
        bodyEvidence.GetProperty("effectiveFontFamily").GetString().Should().Be(minorLatin);
        bodyEvidence.GetProperty("fontFamilySource").GetString().Should().Be("theme.minorLatin");
        bodyEvidence.GetProperty("effectiveFontSizePt").GetDouble().Should().BeApproximately(18.0, 0.01);

        var corpusPptx = TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "tools", "FreeP.RenderCompare", "corpus", "17-bullets-autofit.pptx");
        var presentation = FreeP.Core.IO.PptxPackageReader.Read(corpusPptx);
        presentation.Theme.FontScheme.MajorLatinFont.Should().Be(majorLatin);
        presentation.Theme.FontScheme.MinorLatinFont.Should().Be(minorLatin);
        var slide = presentation.Slides[1];
        var textShapes = slide.Shapes.Where(shape => shape.TextBody is not null).ToList();
        textShapes.Should().HaveCount(2);

        var titleShape = textShapes.Single(shape =>
            string.Equals(ShapeText(shape), "Autofit Shrink Demo", StringComparison.Ordinal));
        var bodyShape = textShapes.Single(shape =>
            ShapeText(shape).StartsWith("Line 1:", StringComparison.Ordinal));

        var title = titleShape.TextBody!;
        title.AutoFitKind.Should().Be(TextAutoFitKind.Shape);
        title.ColumnCount.Should().Be(1);
        title.FontScalePPT.Should().BeNull();
        title.LnSpcReductionPPT.Should().BeNull();
        title.Paragraphs.Should().ContainSingle();
        var titleParagraph = title.Paragraphs[0];
        titleParagraph.BulletKind.Should().Be(BulletKind.None);
        titleParagraph.Runs.Should().ContainSingle();
        var titleRun = titleParagraph.Runs[0];
        titleRun.Text.Should().Be("Autofit Shrink Demo");
        titleRun.FontFamily.Should().BeNull("the effective title face is inherited from theme.majorLatin");
        titleRun.FontSizePt.Should().BeApproximately(28.0, 0.01);
        titleRun.Bold.Should().BeTrue();
        titleRun.Italic.Should().BeFalse();

        var body = bodyShape.TextBody!;
        body.AutoFitKind.Should().Be(TextAutoFitKind.None);
        body.ColumnCount.Should().Be(1);
        body.FontScalePPT.Should().BeNull();
        body.LnSpcReductionPPT.Should().BeNull();
        body.Paragraphs.Should().HaveCount(8);
        body.Paragraphs.Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text)))
            .Should().Equal(
                "Line 1: This is a long line of text that forces PowerPoint to shrink font.",
                "Line 2: This is a long line of text that forces PowerPoint to shrink font.",
                "Line 3: This is a long line of text that forces PowerPoint to shrink font.",
                "Line 4: This is a long line of text that forces PowerPoint to shrink font.",
                "Line 5: This is a long line of text that forces PowerPoint to shrink font.",
                "Line 6: This is a long line of text that forces PowerPoint to shrink font.",
                "Line 7: This is a long line of text that forces PowerPoint to shrink font.",
                "Line 8: This is a long line of text that forces PowerPoint to shrink font.");
        body.Paragraphs.Should().OnlyContain(paragraph =>
            paragraph.BulletKind == BulletKind.None
            && paragraph.Runs.Count == 1
            && paragraph.Runs[0].FontFamily == null
            && paragraph.Runs[0].FontSizePt == null
            && !paragraph.Runs[0].Bold
            && !paragraph.Runs[0].Italic);
        body.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => run.FontFamily == null);
    }

    [Fact]
    public void RetainedWave193ReferenceBundle_StillPinsTheCommittedSlide02Hash()
    {
        var refsPath = TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            RetainedEvidenceRoot.Split('/').Append("references.json").ToArray());
        using var refs = JsonDocument.Parse(File.ReadAllText(refsPath));
        var refsRoot = refs.RootElement;

        refsRoot.GetProperty("sourceRevision").GetString().Should()
            .Be("62fa14b152e3318c09b8696e9edd778c5eb1ab18");
        refsRoot.GetProperty("root").GetString().Should().Be("tools/FreeP.RenderCompare/corpus/pptx-ref");

        var slide02 = refsRoot.GetProperty("rows").EnumerateArray().Single(row =>
            row.GetProperty("deck").GetString() == "17-bullets-autofit"
            && row.GetProperty("slide").GetString() == "slide-02");
        slide02.GetProperty("sha256").GetString().Should()
            .Be("2828a7a3ced739e6b5b36f53aa7309df35ceb8c7f898b7bf8fb29480ab012ee5");
        slide02.GetProperty("width").GetInt32().Should().Be(1280);
        slide02.GetProperty("height").GetInt32().Should().Be(720);

        var imagesPath = TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            RetainedEvidenceRoot.Split('/').Append("images.json").ToArray());
        using var images = JsonDocument.Parse(File.ReadAllText(imagesPath));
        var imagesRoot = images.RootElement;

        imagesRoot.GetProperty("office-slide-02.png").GetString().Should()
            .Be("2828a7a3ced739e6b5b36f53aa7309df35ceb8c7f898b7bf8fb29480ab012ee5");
        imagesRoot.GetProperty("wpf-slide-02.png").GetString().Should()
            .Be("7a16e22e966907f2d5c9551cfae70a925585a774931fb6ddbd08573eeaf0d751");
        imagesRoot.GetProperty("avalonia-slide-02.png").GetString().Should()
            .Be("8f1d878abbf93e4761e11578332d35ff943a8dc4013b1374eaef9e90fa679bb7");
    }

    private static string ShapeText(FreeP.Core.Model.SlideShape shape) =>
        shape.TextBody is null
            ? string.Empty
            : string.Join("\n", shape.TextBody.Paragraphs.Select(
                paragraph => string.Concat(paragraph.Runs.Select(run => run.Text))));
}
