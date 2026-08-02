using System.IO;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Verifies the unified Page Setup dialog and its apply path. The dialog itself is a WPF
/// <see cref="System.Windows.Window"/> (STA); these tests construct it (so its control wiring is exercised
/// without a modal loop) and verify the apply path through <see cref="DocumentView.ApplyPageSettings"/> —
/// the same single commit + re-render path the Page Setup ribbon command uses — mutates the model's
/// <see cref="PageSettings"/> exactly as the dialog's <see cref="PageSetupDialog.Result"/> describes.
/// </summary>
public sealed class PageSetupDialogTests
{
    private static DocumentView ViewWith(PageSettings? seed = null)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Page setup body text."));
        if (seed is not null)
        {
            doc.Page.WidthPt = seed.WidthPt;
            doc.Page.HeightPt = seed.HeightPt;
            doc.Page.Landscape = seed.Landscape;
        }

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void Dialog_SeedsControls_AndRoundTripsSeededValuesThroughAccept()
    {
        // A landscape page stores swapped width/height; the dialog must seed from it and recombine on accept so
        // the produced Result faithfully reflects the seeded page (the controls' default round-trip).
        var page = new PageSettings
        {
            Landscape = true,
            WidthPt = 792,   // Tabloid stored landscape (portrait 792 x 1224)
            HeightPt = 1224,
            MarginTopPt = 50,
            MarginLeftPt = 40,
            GutterPt = 18,
            GutterAtTop = true,
            MirrorMargins = true,
            HeaderDistancePt = 30,
            FooterDistancePt = 40,
            DifferentFirstPage = true,
            VerticalAlignment = PageVerticalAlignment.Center
        };

        var dialog = PageSetupDialog.CreateForTest(page);
        var result = dialog.AcceptForTest();

        Assert.NotNull(result);
        Assert.Equal(50, result!.MarginTopPt);
        Assert.Equal(40, result.MarginLeftPt);
        Assert.Equal(18, result.GutterPt);
        Assert.True(result.GutterAtTop);
        Assert.True(result.Landscape);
        Assert.True(result.MirrorMargins);
        Assert.True(result.DifferentFirstPage);
        Assert.Equal(30, result.HeaderDistancePt);
        Assert.Equal(40, result.FooterDistancePt);
        Assert.Equal(PageVerticalAlignment.Center, result.VerticalAlignment);
        // Landscape width/height are recombined from the portrait-shown values, matching the seeded geometry.
        Assert.Equal(792, result.WidthPt);
        Assert.Equal(1224, result.HeightPt);
    }

    [StaFact]
    public void ApplyPageSettings_AppliesAllDialogFields_ToModel()
    {
        var view = ViewWith();

        // Mirror the exact mutation block the PageSetupCommand applies from a dialog Result.
        var result = new PageSetupDialog.Result(
            MarginTopPt: 54,
            MarginBottomPt: 60,
            MarginLeftPt: 66,
            MarginRightPt: 70,
            GutterPt: 12,
            GutterAtTop: true,
            Landscape: true,
            MirrorMargins: true,
            WidthPt: 1008,   // legal landscape width (legal portrait height)
            HeightPt: 612,
            SectionStart: SectionBreakKind.NextPage,
            DifferentFirstPage: true,
            DifferentOddEvenPages: true,
            HeaderDistancePt: 24,
            FooterDistancePt: 30,
            VerticalAlignment: PageVerticalAlignment.Justified);

        view.ApplyPageSettings(page =>
        {
            page.MarginTopPt = result.MarginTopPt;
            page.MarginBottomPt = result.MarginBottomPt;
            page.MarginLeftPt = result.MarginLeftPt;
            page.MarginRightPt = result.MarginRightPt;
            page.GutterPt = result.GutterPt;
            page.GutterAtTop = result.GutterAtTop;
            page.Landscape = result.Landscape;
            page.MirrorMargins = result.MirrorMargins;
            page.WidthPt = result.WidthPt;
            page.HeightPt = result.HeightPt;
            page.DifferentFirstPage = result.DifferentFirstPage;
            page.DifferentOddEvenPages = result.DifferentOddEvenPages;
            page.HeaderDistancePt = result.HeaderDistancePt;
            page.FooterDistancePt = result.FooterDistancePt;
            page.VerticalAlignment = result.VerticalAlignment;
        });

        var p = view.Model.Page;
        Assert.Equal(54, p.MarginTopPt);
        Assert.Equal(60, p.MarginBottomPt);
        Assert.Equal(66, p.MarginLeftPt);
        Assert.Equal(70, p.MarginRightPt);
        Assert.Equal(12, p.GutterPt);
        Assert.True(p.GutterAtTop);
        Assert.True(p.Landscape);
        Assert.True(p.MirrorMargins);
        Assert.Equal(1008, p.WidthPt);
        Assert.Equal(612, p.HeightPt);
        Assert.True(p.DifferentFirstPage);
        Assert.True(p.DifferentOddEvenPages);
        Assert.Equal(24, p.HeaderDistancePt);
        Assert.Equal(30, p.FooterDistancePt);
        Assert.Equal(PageVerticalAlignment.Justified, p.VerticalAlignment);
    }

    [Fact]
    public void DialogPolicy_IsDelegatedToPresentationPlanner()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
            "freew",
            "FreeW.App.Host",
            "PageSetupDialog.cs"));

        Assert.Contains("PageSetupDialogPlanner.BuildInitialState(", source);
        Assert.Contains("PageSetupDialogPlanner.TryBuildResult(", source);
        Assert.DoesNotContain("PaperSizes =", source);
        Assert.DoesNotContain("SectionStartValues =", source);
        Assert.DoesNotContain("TryParse(_top.Text", source);
    }

}
