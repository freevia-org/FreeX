using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r171 (freep-text-autofit F2): a cached <c>a:normAutofit</c> <c>fontScale</c>/<c>lnSpcReduction</c>
/// was never recomputed back toward 100% once the box/text stopped overflowing --
/// <see cref="PptxPackageWriter.RecomputeNormalAutoFitScale"/> only ever moved the value FURTHER
/// from what was cached, so a placeholder shrunk on import (or by an earlier, more-overflowing
/// edit) stayed shrunk in every subsequent Save/PDF export forever, even after the user deleted
/// enough text -- or enlarged the box enough -- that the live editor grows the text back to full
/// size. PowerPoint recomputes the shrink (including back up to 100%) on every edit; the writer
/// and the PDF exporter must agree with each other and with what the live editor is showing.
/// </summary>
public sealed class NormAutoFitStaleScaleRegrowTests
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static SlideShape MakeAutoFitShape(long extentCyEmu, TextBody body) => new()
    {
        Id = 2,
        Kind = SlideShapeKind.AutoShape,
        AutoShapeKind = DrawingShapeKind.Rectangle,
        OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
        OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
        ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(4 * 72), // 4in wide
        ExtentCyEmu = extentCyEmu,
        TextBody = body,
    };

    private static TextBody MakeOverflowingBody()
    {
        var body = new TextBody { AutoFitKind = TextAutoFitKind.Normal, Wrap = true };
        for (var i = 0; i < 20; i++)
        {
            var para = new Paragraph();
            para.Runs.Add(new Run { Text = $"Overflowing line {i:D2} of 20 that needs shrinking", FontSizePt = 24 });
            body.Paragraphs.Add(para);
        }
        return body;
    }

    private static XElement ReadNormAutofitElement(string pptxPath)
    {
        using var archive = ZipFile.OpenRead(pptxPath);
        var entry = archive.GetEntry("ppt/slides/slide1.xml")
            ?? throw new InvalidOperationException("expected ppt/slides/slide1.xml in the package");
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        var bodyPr = doc.Descendants(A + "bodyPr")
            .FirstOrDefault(el => el.Parent?.Name == P + "txBody");
        return bodyPr?.Element(A + "normAutofit")
            ?? throw new InvalidOperationException("expected a:normAutofit in the saved slide");
    }

    /// <summary>
    /// Full round-trip through the real Read/Write pair (not just a direct call into the
    /// estimator): author overflowing text small box, save so the writer computes and bakes in a
    /// real shrink, reopen (simulating a real PowerPoint/FreeP re-open that now carries a cached
    /// FontScalePPT the way an imported file would), delete enough text in the reloaded model that
    /// it comfortably fits the same box, then SAVE AGAIN and assert the file that comes out of
    /// that second save -- not merely the in-memory TextBody -- is back at full size.
    /// </summary>
    [Fact]
    public void SavedFile_RegrowsStaleShrunkScale_WhenTextNoLongerOverflows()
    {
        using var tempDir = new TestScratchDirectory();

        // 1) Author overflowing text in a small box and save -- the writer must compute a real
        //    shrink here (proves the box genuinely overflows before we ever touch the cache).
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        var shrinkHeightEmu = DrawingMlCoordinateUnits.PointsToEmu(150); // too small for 20 lines @ 24pt
        pres.Slides[0].Shapes.Add(MakeAutoFitShape(shrinkHeightEmu, MakeOverflowingBody()));

        var shrunkPath = Path.Combine(tempDir.Path, "shrunk.pptx");
        PptxPackageWriter.Write(pres, shrunkPath);

        var shrunkNaf = ReadNormAutofitElement(shrunkPath);
        var cachedScale = int.Parse(shrunkNaf.Attribute("fontScale")!.Value);
        cachedScale.Should().BeLessThan(100000, "the overflowing text must have been shrunk on first save");

        // 2) Reopen it -- TextBody.FontScalePPT is now populated from the file, exactly like a
        //    real PowerPoint-authored or previously-shrunk FreeP file would carry.
        var reloaded = PptxPackageReader.Read(shrunkPath);
        var reloadedShape = reloaded.Slides[0].Shapes.Single(s => s.Id == 2);
        reloadedShape.TextBody!.FontScalePPT.Should().Be(cachedScale);

        // 3) Delete all but one short paragraph -- the remaining text comfortably fits the SAME
        //    box at full (100%) size. This is the live-editor self-heal case the finding
        //    describes (TextLayoutPlanner grows the on-screen text back); nothing here touches
        //    FontScalePPT directly -- it stays at the stale cached value, matching how no editing
        //    command in FreeP clears it today.
        var survivingRun = reloadedShape.TextBody.Paragraphs[0].Runs[0];
        survivingRun.Text = "Short text that fits easily.";
        survivingRun.FontSizePt = 18;
        while (reloadedShape.TextBody.Paragraphs.Count > 1)
            reloadedShape.TextBody.Paragraphs.RemoveAt(reloadedShape.TextBody.Paragraphs.Count - 1);
        reloadedShape.TextBody.FontScalePPT.Should().Be(cachedScale,
            "no editing command clears the cache directly -- the writer/exporter must correct it");

        // 4) Save again with the SAME box size. The file this write produces -- not the in-memory
        //    property -- is the thing PowerPoint and the PDF exporter will actually read.
        var regrownPath = Path.Combine(tempDir.Path, "regrown.pptx");
        PptxPackageWriter.Write(reloaded, regrownPath);

        var regrownNaf = ReadNormAutofitElement(regrownPath);
        regrownNaf.Attribute("fontScale").Should().BeNull(
            "text that no longer overflows must be written at full size (no fontScale override), " +
            "not left frozen at the earlier shrink");
        regrownNaf.Attribute("lnSpcReduction").Should().BeNull(
            "line-spacing reduction must also be dropped once the text no longer needs it");

        // The exporter that PDF export shares this exact function with must agree.
        var (exportedFontScale, exportedLnSpcReduction) = PptxPackageWriter.RecomputeNormalAutoFitScale(
            reloadedShape.TextBody, reloadedShape.ExtentCxEmu, reloadedShape.ExtentCyEmu);
        exportedFontScale.Should().BeNull("PDF export reuses this exact function, so it must agree with what Save wrote");
        exportedLnSpcReduction.Should().BeNull();
    }

    /// <summary>
    /// Sibling/no-regression: text that STILL overflows the box after the edit must keep being
    /// shrunk on save -- growing a stale scale back must not turn into "always clear the cache".
    /// </summary>
    [Fact]
    public void SavedFile_KeepsShrinkingText_WhenItStillOverflowsAfterEdit()
    {
        using var tempDir = new TestScratchDirectory();

        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        var shrinkHeightEmu = DrawingMlCoordinateUnits.PointsToEmu(150);
        pres.Slides[0].Shapes.Add(MakeAutoFitShape(shrinkHeightEmu, MakeOverflowingBody()));

        var shrunkPath = Path.Combine(tempDir.Path, "shrunk2.pptx");
        PptxPackageWriter.Write(pres, shrunkPath);
        var reloaded = PptxPackageReader.Read(shrunkPath);
        var reloadedShape = reloaded.Slides[0].Shapes.Single(s => s.Id == 2);

        // Trim from 20 paragraphs to 12 -- still clearly overflows a 150pt-tall box at 24pt.
        while (reloadedShape.TextBody!.Paragraphs.Count > 12)
            reloadedShape.TextBody.Paragraphs.RemoveAt(reloadedShape.TextBody.Paragraphs.Count - 1);

        var stillOverflowingPath = Path.Combine(tempDir.Path, "still-overflowing.pptx");
        PptxPackageWriter.Write(reloaded, stillOverflowingPath);

        var naf = ReadNormAutofitElement(stillOverflowingPath);
        naf.Attribute("fontScale").Should().NotBeNull("text that still overflows must still be shrunk on save");
        int.Parse(naf.Attribute("fontScale")!.Value).Should().BeLessThan(100000);
    }

    private sealed class TestScratchDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "FreeP.NormAutoFitStaleScaleRegrowTests-" + Guid.NewGuid().ToString("N"));

        public TestScratchDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
