using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r408: the lossy-save confirmation must cover what the plain-text writers actually discard.
///
/// <para><see cref="LossyFormatFeatureLossPlanner"/> exists to ask before a save "would silently drop
/// content the format can't hold", but it enumerated only worksheet count and drawing objects. A
/// single-sheet workbook carrying a comment, a hyperlink or a merged region therefore saved to .csv
/// with no confirmation at all -- and those are discarded just as completely as a chart, because the
/// delimited writers enumerate cell values and nothing else.</para>
///
/// <para>Each case pairs the warning with a measured round trip, so the test asserts the loss is real
/// rather than assuming it. A gate that warns about something the format actually preserves would be
/// noise, and this suite has spent several rounds removing assertions that were never checked against
/// behaviour.</para>
/// </summary>
public sealed class R408_LossySaveWarnsAboutAnnotationLossTests
{
    private static Workbook SingleSheetWorkbook(Action<Sheet> seed)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));
        seed(sheet);
        return workbook;
    }

    private static Sheet RoundTripThroughCsv(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return new CsvFileAdapter().Load(stream).Sheets[0];
    }

    [Fact]
    public void AComment_IsLostAndIsWarnedAbout()
    {
        var workbook = SingleSheetWorkbook(sheet =>
            sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "note");

        RoundTripThroughCsv(workbook).Comments.Should().BeEmpty("csv cannot carry a comment");
        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".csv")
            .Should().BeTrue("the user must be asked before their comments are discarded");
    }

    [Fact]
    public void AHyperlink_IsLostAndIsWarnedAbout()
    {
        var workbook = SingleSheetWorkbook(sheet =>
            sheet.Hyperlinks[new CellAddress(sheet.Id, 1, 1)] = "https://example.invalid");

        RoundTripThroughCsv(workbook).Hyperlinks.Should().BeEmpty("csv cannot carry a hyperlink");
        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".csv")
            .Should().BeTrue("the user must be asked before their hyperlinks are discarded");
    }

    [Fact]
    public void AMergedRegion_IsLostAndIsWarnedAbout()
    {
        var workbook = SingleSheetWorkbook(sheet =>
            sheet.AddMergedRegion(GridRange.Parse("A1:B1", sheet.Id)));

        RoundTripThroughCsv(workbook).MergedRegions.Should().BeEmpty("csv cannot carry a merge");
        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".csv")
            .Should().BeTrue("the user must be asked before their merges are discarded");
    }

    [Fact]
    public void APlainWorkbookIsStillNotWarnedAbout()
    {
        // The control that keeps the gate honest: broadening it must not make every save prompt.
        // Without this, "warn about everything" would satisfy the three tests above.
        var workbook = SingleSheetWorkbook(_ => { });

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".csv")
            .Should().BeFalse("a plain single-sheet workbook loses nothing in csv, so asking would be noise");
    }

    [Fact]
    public void TheGateStillIgnoresFormatsThatCanHoldTheContent()
    {
        // .xlsx keeps all of it, and has its own dedicated gate; broadening the plain-text rule must
        // not leak into formats that lose nothing.
        var workbook = SingleSheetWorkbook(sheet =>
        {
            sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "note";
            sheet.AddMergedRegion(GridRange.Parse("A1:B1", sheet.Id));
        });

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, ".xlsx")
            .Should().BeFalse("xlsx carries comments and merges, so this gate must stay out of its way");
    }
}
