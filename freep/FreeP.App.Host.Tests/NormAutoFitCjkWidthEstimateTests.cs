using FreeP.Core.IO;
using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r148 (freep-autofit F2): <see cref="PptxPackageWriter.RecomputeNormalAutoFitScale"/> estimated
/// wrapped-line width using a single Latin-proportional average advance width (~0.52em) for every
/// character, so full-width CJK text -- which renders close to a full em wide -- was judged as
/// wrapping to far fewer lines than it actually does. That let clearly-overflowing CJK text be
/// judged as "fits", so no fontScale/lnSpcReduction ever got cached for it, and PowerPoint (which
/// trusts an absent cached fontScale as 100%, unlike FreeP's own live view) renders the text
/// overflowing the placeholder on a plain reopen.
/// </summary>
public sealed class NormAutoFitCjkWidthEstimateTests
{
    // 4in x 3in placeholder, matching the finding's reproduction box.
    private static readonly long ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(4 * 72.0);
    private static readonly long ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(3 * 72.0);

    private static TextBody BuildJapaneseOverflowBody()
    {
        var body = new TextBody { AutoFitKind = TextAutoFitKind.Normal, Wrap = true };
        for (int i = 0; i < 3; i++)
        {
            var para = new Paragraph();
            para.Runs.Add(new Run { Text = new string('あ', 40), FontSizePt = 24.0 }); // 'あ' x40
            body.Paragraphs.Add(para);
        }
        return body;
    }

    [Fact]
    public void CjkTextThatOverflowsGetsAFontScaleCached()
    {
        var body = BuildJapaneseOverflowBody();

        var (fontScalePpt, _) = PptxPackageWriter.RecomputeNormalAutoFitScale(body, ExtentCxEmu, ExtentCyEmu);

        // Before the fix this returned null: the 0.52em-per-character Latin estimate under-counted
        // full-width glyph width by roughly half, so the estimator concluded the (heavily
        // overflowing) CJK text fit and left FontScalePPT unset entirely.
        Assert.NotNull(fontScalePpt);
        Assert.True(fontScalePpt < 100000, $"expected a shrink below 100% but got {fontScalePpt}");
    }

    /// <summary>
    /// Sibling/no-regression case: Latin proportional text that genuinely fits the box must still
    /// be left alone (no fontScale forced in) -- the CJK-specific width factor must not make the
    /// estimator more aggressive for scripts it doesn't apply to.
    /// </summary>
    [Fact]
    public void LatinTextThatFitsIsNotTouched()
    {
        var body = new TextBody { AutoFitKind = TextAutoFitKind.Normal, Wrap = true };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Short fitting text.", FontSizePt = 18.0 });
        body.Paragraphs.Add(para);

        var (fontScalePpt, lnSpcReductionPpt) =
            PptxPackageWriter.RecomputeNormalAutoFitScale(body, ExtentCxEmu, ExtentCyEmu);

        Assert.Null(fontScalePpt);
        Assert.Null(lnSpcReductionPpt);
    }
}
