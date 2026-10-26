using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r410: finishes the loss map the planner's own comment left open, and pins that each format's
/// prompt describes THAT format's loss.
///
/// <para>Three formats, three different answers, all measured rather than assumed:</para>
/// <list type="bullet">
/// <item>plain text (.csv and friends) loses everything but cell values -- r408.</item>
/// <item>web pages (.html/.mht) lose comments, hyperlinks, validations and conditional formats, but
/// KEEP merged regions -- r409 plus the validations/formats measured here.</item>
/// <item>SpreadsheetML (.xml) keeps worksheets, comments, hyperlinks and merges, and loses ONLY
/// validations and conditional formats.</item>
/// </list>
///
/// <para>They therefore cannot share a rule. Collapsing them would either under-warn (silently
/// dropping a validation) or over-warn (prompting about a merge that survives), and an inaccurate
/// prompt is one the user stops reading.</para>
/// </summary>
public sealed class R410_SpreadsheetXmlAndWebPageLossTests
{
    private static Workbook WorkbookWith(Action<Sheet> seed)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        seed(sheet);
        return workbook;
    }

    private static void AddValidation(Sheet sheet) =>
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = GridRange.Parse("A1:A5", sheet.Id),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
        });

    private static Sheet RoundTrip(IFileAdapter adapter, Workbook workbook)
    {
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream).Sheets[0];
    }

    [Fact]
    public void SpreadsheetXmlLosesValidations_AndNowWarns()
    {
        var workbook = WorkbookWith(AddValidation);

        RoundTrip(new SpreadsheetXmlFileAdapter(), workbook).DataValidations
            .Should().BeEmpty("measured: SpreadsheetML does not carry data validations");

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".xml")
            .Should().BeTrue("the user must be asked before their validation rules are discarded");
    }

    [Fact]
    public void SpreadsheetXmlKeepsCommentsAndMerges_SoThoseAloneMustNotPrompt()
    {
        // The control that makes .xml's rule the narrowest of the three. If this prompted, the
        // format's warning would be describing losses it does not have.
        var workbook = WorkbookWith(sheet =>
        {
            sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "note";
            sheet.Hyperlinks[new CellAddress(sheet.Id, 1, 1)] = "https://example.invalid";
            sheet.AddMergedRegion(GridRange.Parse("A1:B1", sheet.Id));
        });

        var reloaded = RoundTrip(new SpreadsheetXmlFileAdapter(), workbook);
        reloaded.Comments.Should().NotBeEmpty("measured: SpreadsheetML carries comments");
        reloaded.MergedRegions.Should().NotBeEmpty("measured: SpreadsheetML carries merges");

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".xml")
            .Should().BeFalse("nothing here is lost, so prompting would be a false alarm");
    }

    [Fact]
    public void AWebPageAlsoLosesValidations_AndWarns()
    {
        var workbook = WorkbookWith(AddValidation);

        RoundTrip(new HtmlFileAdapter(), workbook).DataValidations
            .Should().BeEmpty("measured: the html writer does not carry validations");

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".html")
            .Should().BeTrue("web-page saves discard validations too");
    }

    [Fact]
    public void PdfStaysUngatedBecauseItIsExportOnly()
    {
        // Not an oversight: PdfFileAdapter refuses to import, so there is no round trip to lose
        // anything through, and choosing "PDF" already says the user wants a rendering. Excel does
        // not prompt here either. Pinned so the omission reads as a decision, not a gap.
        var workbook = WorkbookWith(AddValidation);

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".pdf")
            .Should().BeFalse("pdf export is a rendering the user explicitly asked for");
    }
}
