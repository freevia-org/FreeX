using System.IO;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R48-io-page-setup-headerfooter-3-1: real Excel preserves
/// evenHeader/evenFooter/firstHeader/firstFooter text on disk even while the
/// "Different odd and even pages"/"Different first page" checkboxes are unchecked -- the flags
/// only deactivate rendering, they do not purge the stored text, so re-checking either box
/// later restores it. <see cref="XlsxWorksheetPageSetupMapper.SetHeaderFooter"/> previously only
/// wrote the first-page/even-page header-footer text when the corresponding "Different..." flag
/// was true, so every save with the flag off silently discarded that text even when the user
/// made no header/footer edit at all.
/// </summary>
public sealed class XlsxHeaderFooterOddEvenFirstPagePreservationTests
{
    [Fact]
    public void Save_EvenAndFirstPageHeaderText_SurvivesWhenDifferentFlagsAreOff()
    {
        var workbook = new Workbook("HeaderFooterPreserve");
        var sheet = workbook.AddSheet("Sheet1");

        // Both "Different first page" and "Different odd/even" are OFF, as if the user
        // unchecked them in Page Setup (or the workbook carries leftover configured text that
        // simply isn't active right now). The text itself was never cleared.
        sheet.PageHeader = new WorksheetHeaderFooter("Odd L", "Odd C", "Odd R");
        sheet.FirstPageHeader = new WorksheetHeaderFooter("First L", "First C", "First R");
        sheet.EvenPageHeader = new WorksheetHeaderFooter("Even L", "Even C", "Even R");
        sheet.DifferentFirstPageHeaderFooter = false;
        sheet.DifferentOddEvenHeaderFooter = false;

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(ms);
        var rs = reloaded.GetSheetAt(0);

        rs.DifferentFirstPageHeaderFooter.Should().BeFalse();
        rs.DifferentOddEvenHeaderFooter.Should().BeFalse();
        rs.PageHeader.Should().Be(new WorksheetHeaderFooter("Odd L", "Odd C", "Odd R"));

        // The bug: pre-fix, SetHeaderFooter only wrote firstHeader/evenHeader when the
        // corresponding "Different..." flag was true, so this text round-tripped to empty
        // strings even though nothing about it was ever cleared by the user.
        rs.FirstPageHeader.Should().Be(
            new WorksheetHeaderFooter("First L", "First C", "First R"),
            "Excel preserves first-page header text even while 'Different First Page' is unchecked");
        rs.EvenPageHeader.Should().Be(
            new WorksheetHeaderFooter("Even L", "Even C", "Even R"),
            "Excel preserves even-page header text even while 'Different Odd and Even Pages' is unchecked");
    }

    /// <summary>
    /// Sibling/no-regression case: with BOTH "Different odd/even" and "Different first page" ON,
    /// the primary occurrence is OddPages (not the fallback-y AllPages), so ClosedXML's
    /// AllPages-bleed quirk this fix works around never applies -- this path already worked
    /// correctly before the fix and must remain unaffected by it.
    /// </summary>
    [Fact]
    public void Save_DistinctOddEvenAndFirstPageHeaders_AllFlagsOn_RoundTripIndependently()
    {
        var workbook = new Workbook("HeaderFooterNoRegression");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.PageHeader = new WorksheetHeaderFooter("Odd L", "Odd C", "Odd R");
        sheet.EvenPageHeader = new WorksheetHeaderFooter("Even L", "Even C", "Even R");
        sheet.FirstPageHeader = new WorksheetHeaderFooter("First L", "First C", "First R");
        sheet.DifferentFirstPageHeaderFooter = true;
        sheet.DifferentOddEvenHeaderFooter = true;

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(ms);
        var rs = reloaded.GetSheetAt(0);

        rs.DifferentFirstPageHeaderFooter.Should().BeTrue();
        rs.DifferentOddEvenHeaderFooter.Should().BeTrue();
        rs.PageHeader.Should().Be(new WorksheetHeaderFooter("Odd L", "Odd C", "Odd R"));
        rs.EvenPageHeader.Should().Be(new WorksheetHeaderFooter("Even L", "Even C", "Even R"));
        rs.FirstPageHeader.Should().Be(new WorksheetHeaderFooter("First L", "First C", "First R"));
    }
}
