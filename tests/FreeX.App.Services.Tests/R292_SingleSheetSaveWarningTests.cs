using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r292: closes the gap r291 recorded -- FreeX discarded worksheets on a save to a single-sheet
/// format without telling anyone.
///
/// <para>r291 named it rather than half-building it, because the fix looked like it needed new
/// resource strings and UI in two shells. It did not: <c>WorkbookSaveService</c> already has a
/// portable chokepoint that asks the adapter what it can do (<c>IWarningCollectingFileAdapter</c>)
/// and returns warnings through a channel the host already displays. The sheet-loss warning goes
/// through the same place, so no call site changes.</para>
///
/// <para>The last test is the one that keeps this honest: the <c>ISingleSheetFileAdapter</c> marker
/// is a DECLARATION, and a declaration can drift from what the code does. It is checked against the
/// measured behaviour of every adapter, so an adapter that gains multi-sheet support without
/// dropping the marker -- or loses it without gaining the marker -- fails here.</para>
/// </summary>
public sealed class R292_SingleSheetSaveWarningTests
{
    private static readonly string[] SheetNames = ["Alpha", "Beta", "Gamma"];

    private static IFileAdapter[] AllAdapters() =>
    [
        new CsvFileAdapter(), new CsvUtf8FileAdapter(), new PrnFileAdapter(),
        new SlkFileAdapter(), new DifFileAdapter(), new HtmlFileAdapter(),
        new OdsFileAdapter(), new SpreadsheetXmlFileAdapter(), new NativeJsonAdapter(),
    ];

    private static Workbook Sheets(params string[] names)
    {
        var workbook = new Workbook("Book1");
        foreach (var name in names)
        {
            var sheet = workbook.AddSheet(name);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(name + "-data"));
        }

        return workbook;
    }

    [Fact]
    public void ASingleSheetFormatWarnsAndNamesTheSheetsItDiscards()
    {
        var warning = SingleSheetSaveWarningPlanner.DescribeDiscardedSheets(
            new CsvFileAdapter(), Sheets(SheetNames));

        warning.Should().NotBeNull("the sheets are gone from the file and nothing else says so");
        warning.Should().Contain("Alpha", "the user needs to know which sheet WAS saved");
        warning.Should().Contain("Beta").And.Contain("Gamma",
            "naming the lost sheets is the point -- a bare count leaves the user to work out which");
    }

    [Fact]
    public void ASingleSheetWorkbookLosesNothingAndIsNotWarnedAbout()
    {
        SingleSheetSaveWarningPlanner.DescribeDiscardedSheets(new CsvFileAdapter(), Sheets("Only"))
            .Should().BeNull("a warning with nothing behind it trains the user to dismiss warnings");
    }

    [Fact]
    public void AMultiSheetFormatIsNotWarnedAbout()
    {
        SingleSheetSaveWarningPlanner.DescribeDiscardedSheets(new OdsFileAdapter(), Sheets(SheetNames))
            .Should().BeNull("ODS keeps every sheet, so there is no loss to report");
    }

    [Theory]
    [InlineData(2, "was")]
    [InlineData(3, "were")]
    public void TheWarningReadsAsASentenceForOneOrManyLostSheets(int sheetCount, string verb) =>
        SingleSheetSaveWarningPlanner.DescribeDiscardedSheets(
                new CsvFileAdapter(), Sheets(SheetNames.Take(sheetCount).ToArray()))
            .Should().Contain(verb + " not.");

    /// <summary>
    /// The marker must agree with reality. Each adapter is given three sheets and round-tripped;
    /// one sheet back means it is single-sheet and must be marked, three means it is not and must
    /// not be.
    /// </summary>
    [Fact]
    public void TheMarkerMatchesWhatEveryAdapterActuallyDoes()
    {
        var mismatches = new List<string>();

        foreach (var adapter in AllAdapters())
        {
            using var stream = new MemoryStream();
            adapter.Save(Sheets(SheetNames), stream);
            stream.Position = 0;
            var survived = adapter.Load(stream).Sheets.Count;

            var declared = adapter is ISingleSheetFileAdapter;
            var actual = survived == 1;
            if (declared != actual)
            {
                mismatches.Add(
                    $"{adapter.GetType().Name}: declares ISingleSheetFileAdapter={declared} but "
                    + $"kept {survived} of {SheetNames.Length} sheets");
            }
        }

        mismatches.Should().BeEmpty(
            "the marker drives a user-facing warning: declaring it on a format that keeps every "
            + "sheet cries wolf, and omitting it from one that does not silently discards the "
            + "user's data again.\n" + string.Join("\n", mismatches));
    }
}
