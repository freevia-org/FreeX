using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r409: the web-page save targets must warn about what they actually lose -- and only that.
///
/// <para><see cref="LossyFormatFeatureLossPlanner"/> documented .html/.mht as "not (yet) checked at
/// all", and measurement showed the gap was real: a workbook round-tripped through the HTML and MHT
/// adapters comes back with its comments and hyperlinks gone. Saving a commented workbook as a web
/// page discarded them with no confirmation.</para>
///
/// <para>These formats deliberately do NOT join the plain-text rule r408 widened, because they are
/// not equivalent: merged regions SURVIVE a web-page round trip. Warning about a merge here would
/// describe a loss that does not happen, and a prompt that cries wolf is one the user learns to
/// click through -- which costs more than the warning gains.</para>
/// </summary>
public sealed class R409_WebPageSaveWarnsAboutWhatItDiscardsTests
{
    private static Workbook SingleSheetWorkbook(Action<Sheet> seed)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));
        seed(sheet);
        return workbook;
    }

    private static Sheet RoundTrip(IFileAdapter adapter, Workbook workbook)
    {
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream).Sheets[0];
    }

    [Theory]
    [InlineData(".html")]
    [InlineData(".htm")]
    [InlineData(".mht")]
    [InlineData(".mhtml")]
    public void CommentsAreLostAndWarnedAbout(string extension)
    {
        var workbook = SingleSheetWorkbook(sheet =>
            sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "note");

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, extension)
            .Should().BeTrue("a web-page save discards comments, so the user must be asked first");
    }

    [Fact]
    public void TheCommentLossIsReal_NotAssumed()
    {
        // The measurement the warning rests on. If a future writer learns to carry comments, this
        // fails first and the warning above becomes the thing to revisit -- rather than the product
        // quietly warning about a loss that no longer happens.
        var workbook = SingleSheetWorkbook(sheet =>
        {
            sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "note";
            sheet.Hyperlinks[new CellAddress(sheet.Id, 1, 1)] = "https://example.invalid";
        });

        var html = RoundTrip(new HtmlFileAdapter(), workbook);
        html.Comments.Should().BeEmpty("measured: the html writer does not carry comments");
        html.Hyperlinks.Should().BeEmpty("measured: the html writer does not carry hyperlinks");

        var mht = RoundTrip(new MhtFileAdapter(), workbook);
        mht.Comments.Should().BeEmpty("measured: the mht writer does not carry comments");
        mht.Hyperlinks.Should().BeEmpty("measured: the mht writer does not carry hyperlinks");
    }

    [Fact]
    public void AMergeSurvivesAWebPageSaveAndIsNotWarnedAbout()
    {
        // The control that keeps this rule distinct from the plain-text one. A merge round-trips
        // intact, so prompting for it would be a false alarm.
        var workbook = SingleSheetWorkbook(sheet =>
            sheet.AddMergedRegion(GridRange.Parse("A1:B1", sheet.Id)));

        RoundTrip(new HtmlFileAdapter(), workbook).MergedRegions
            .Should().NotBeEmpty("measured: merges survive a web-page round trip");

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".html")
            .Should().BeFalse("warning about a merge that survives would be a false alarm");
    }

    [Fact]
    public void APlainWorkbookIsStillNotWarnedAbout()
    {
        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(SingleSheetWorkbook(_ => { }), ".html")
            .Should().BeFalse("a plain single-sheet workbook loses nothing worth a prompt");
    }
}
