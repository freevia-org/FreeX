using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r310: the sheet-loss warning must name the sheet that actually survived.
///
/// <para>r292 added the warning and took the surviving sheet to be <c>Sheets[0]</c>. The writers do
/// not agree: they export the ACTIVE sheet. So a user who had switched tabs was told the wrong sheet
/// had been saved and the surviving one listed among the casualties -- precisely inverted, and this
/// is the one message they act on before closing the file.</para>
///
/// <para>r292's own tests all passed, because none of them ever made a sheet other than the first
/// one active. That is what let the defect through, so these tests vary exactly that.</para>
/// </summary>
public sealed class R310_SheetLossWarningNamesTheSurvivingSheetTests
{
    private static Workbook ThreeSheets(int activeIndex)
    {
        var workbook = new Workbook("Book1");
        foreach (var name in new[] { "Alpha", "Beta", "Gamma" })
        {
            var sheet = workbook.AddSheet(name);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(name));
        }

        workbook.ActiveSheetIndex = activeIndex;
        return workbook;
    }

    [Fact]
    public void TheWarningNamesTheActiveSheetAsTheOneThatWasSaved()
    {
        var warning = SingleSheetSaveWarningPlanner.DescribeDiscardedSheets(
            new CsvFileAdapter(), ThreeSheets(activeIndex: 2));

        warning.Should().NotBeNull();
        warning!.Should().Contain("Only \"Gamma\" was saved",
            "the writers export the active sheet, so that is the one that survived");
        warning.Should().Contain("Alpha").And.Contain("Beta");
    }

    /// <summary>
    /// The other direction, so the test above cannot pass by always naming the last sheet.
    /// </summary>
    [Fact]
    public void TheWarningNamesTheFirstSheetWhenItIsTheActiveOne()
    {
        var warning = SingleSheetSaveWarningPlanner.DescribeDiscardedSheets(
            new CsvFileAdapter(), ThreeSheets(activeIndex: 0));

        warning.Should().NotBeNull();
        warning!.Should().Contain("Only \"Alpha\" was saved");
        warning.Should().Contain("Beta").And.Contain("Gamma");
    }

    /// <summary>
    /// A workbook with no recorded active sheet still has to produce a correct warning rather than
    /// throw or name nothing -- the fallback is the first sheet, which is also what gets written.
    /// </summary>
    [Fact]
    public void AnAbsentOrOutOfRangeActiveSheetFallsBackToTheFirst()
    {
        var absent = ThreeSheets(activeIndex: 0);
        absent.ActiveSheetIndex = null;
        SingleSheetSaveWarningPlanner.DescribeDiscardedSheets(new CsvFileAdapter(), absent)
            .Should().Contain("Only \"Alpha\" was saved");

        var outOfRange = ThreeSheets(activeIndex: 0);
        outOfRange.ActiveSheetIndex = 97;
        SingleSheetSaveWarningPlanner.DescribeDiscardedSheets(new CsvFileAdapter(), outOfRange)
            .Should().Contain("Only \"Alpha\" was saved");
    }

    /// <summary>
    /// The warning and the file must agree. Naming a surviving sheet the writer did not write would
    /// be its own defect, so this checks the two against each other rather than against a constant.
    /// </summary>
    [Fact]
    public void TheNamedSurvivorIsTheSheetTheAdapterActuallyWrites()
    {
        var workbook = ThreeSheets(activeIndex: 1);
        var adapter = new CsvFileAdapter();

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        var written = System.Text.Encoding.UTF8.GetString(stream.ToArray());

        var warning = SingleSheetSaveWarningPlanner.DescribeDiscardedSheets(adapter, workbook);

        written.Should().Contain("Beta");
        warning.Should().Contain("Only \"Beta\" was saved");
    }
}
