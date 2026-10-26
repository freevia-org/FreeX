using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r394: a worksheet loss the user already agreed to must not be reported back to them afterwards.
///
/// <para>Two planners describe the same event. <see cref="LossyFormatFeatureLossPlanner"/> asks
/// BEFORE the save -- "only the current worksheet's data will be saved ... Keep this format?" --
/// and <see cref="SingleSheetSaveWarningPlanner"/> describes the same discarded sheets AFTER it.
/// Both fire on <c>Sheets.Count &gt; 1</c>, so saving a multi-sheet workbook as CSV told the user
/// their sheets were dropped immediately after they clicked Yes to dropping them. Excel asks once
/// and then saves silently, so <c>WorkbookSaveService</c> now suppresses the second message for the
/// formats the pre-save gate covers.</para>
///
/// <para>That suppression is only correct while the gate's coverage and the warning's coverage line
/// up the way they do here, which is what this pins. The planner list is deliberately partial --
/// .xml, .html/.mht and .pdf are documented as NOT gated -- and for those the post-save warning is
/// the only notice the user gets, so it must survive. Without that half, a suppression that
/// swallowed the warning everywhere would look identical from the CSV case alone.</para>
/// </summary>
public sealed class R394_SheetLossIsReportedOnceTests
{
    private static Workbook MultiSheetWorkbook()
    {
        var workbook = new Workbook("Book1");
        foreach (var name in new[] { "Alpha", "Beta" })
        {
            var sheet = workbook.AddSheet(name);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(name));
        }

        return workbook;
    }

    [Theory]
    [InlineData(".csv")]
    [InlineData(".txt")]
    [InlineData(".prn")]
    [InlineData(".slk")]
    [InlineData(".dif")]
    public void AFormatGatedByThePreSaveConfirmationIsTheCaseWhereTheSecondMessageIsRedundant(string extension)
    {
        var workbook = MultiSheetWorkbook();

        LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, extension)
            .Should().BeTrue(
                "{0} asks the user before discarding the other sheets, which is what makes repeating " +
                "it afterwards redundant", extension);
    }

    [Fact]
    public void NoSingleSheetFormatCanDiscardASheetWithoutTellingTheUserSomehow()
    {
        // r409 UPDATED this case. It used to assert that .html is NOT gated, and therefore that the
        // post-save warning had to survive for it -- true when written, because the gate documented
        // .html as uncovered. r409 gated the web-page formats after measuring that they discard
        // comments and hyperlinks, so that premise is now false BY DESIGN and the old assertion
        // would have pinned the gap it closed.
        //
        // What r394 actually cared about is unchanged and is what this now pins: a sheet must never
        // be dropped silently. Either the pre-save gate asks, or the post-save planner describes it
        // -- never neither. WorkbookSaveService is what stops it being both.
        var workbook = MultiSheetWorkbook();

        var singleSheetFormats = new (IFileAdapter Adapter, string Extension)[]
        {
            (new CsvFileAdapter(), ".csv"),
            (new CsvUtf8FileAdapter(), ".csv"),
            (new DifFileAdapter(), ".dif"),
            (new SlkFileAdapter(), ".slk"),
            (new PrnFileAdapter(), ".prn"),
            (new HtmlFileAdapter(), ".html"),
        };

        foreach (var (adapter, extension) in singleSheetFormats)
        {
            var gated = LossyFormatFeatureLossPlanner.RequiresFeatureLossConfirmation(workbook, extension);
            var described = SingleSheetSaveWarningPlanner.DescribeDiscardedSheets(adapter, workbook) is not null;

            (gated || described).Should().BeTrue(
                "{0} keeps one worksheet, so dropping the others must reach the user either as the " +
                "pre-save confirmation or as the post-save warning", extension);
        }
    }

    [Fact]
    public void TheWarningItselfStillDescribesTheLossForGatedFormats()
    {
        // The service suppresses the MESSAGE; the planner must still be able to describe the loss,
        // so callers that show no pre-save prompt (and any future non-interactive reporting) keep
        // working. Suppression belongs at the point of display, not by blinding the planner.
        SingleSheetSaveWarningPlanner.DescribeDiscardedSheets(new CsvFileAdapter(), MultiSheetWorkbook())
            .Should().NotBeNull("the planner's own contract is unchanged by where it is consumed");
    }
}
