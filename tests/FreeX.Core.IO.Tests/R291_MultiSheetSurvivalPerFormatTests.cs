using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r291: saving a multi-sheet workbook to a single-sheet format keeps sheet ONE and discards the
/// rest. Nothing in FreeX warns before that happens.
///
/// <para>Measured, not assumed: `json`, `ods` and `xml` round-trip all three sheets with their names;
/// `csv`, `prn`, `slk`, `dif` and `html` come back with one sheet carrying the FIRST sheet's data
/// under a default name. That division is a property of the formats, and keeping the first sheet is
/// a reasonable convention -- Excel keeps the ACTIVE one, which is a difference worth knowing but
/// not a defect on its own.</para>
///
/// <para>What IS a gap, recorded rather than fixed here: Excel warns that only one sheet will be
/// saved, and FreeX does not. The save pipeline already carries a warnings channel
/// (<c>WorkbookSaveExecutionResult.Warnings</c>, surfaced by the WPF host), but only the XLSX path
/// populates it, and the display method and its resource strings are XLSX-specific. Making this
/// warn means new strings in both shells and wiring this environment cannot exercise -- so it is
/// named here instead of half-built, the same call as r282.</para>
///
/// <para>These tests pin the boundary so a regression is visible: a multi-sheet format quietly
/// dropping to one sheet is silent data loss, and a single-sheet format losing the first sheet's
/// content is worse.</para>
/// </summary>
public sealed class R291_MultiSheetSurvivalPerFormatTests
{
    public static TheoryData<string> MultiSheetFormats() => new() { "json", "ods", "xml" };

    public static TheoryData<string> SingleSheetFormats() => new() { "csv", "prn", "slk", "dif", "html" };

    private static IFileAdapter Make(string key) => key switch
    {
        "csv" => new CsvFileAdapter(),
        "prn" => new PrnFileAdapter(),
        "slk" => new SlkFileAdapter(),
        "dif" => new DifFileAdapter(),
        "ods" => new OdsFileAdapter(),
        "xml" => new SpreadsheetXmlFileAdapter(),
        "json" => new NativeJsonAdapter(),
        "html" => new HtmlFileAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private static readonly string[] SheetNames = ["Alpha", "Beta", "Gamma"];

    private static Workbook ThreeSheets()
    {
        var workbook = new Workbook("Book1");
        foreach (var name in SheetNames)
        {
            var sheet = workbook.AddSheet(name);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(name + "-data"));
        }

        return workbook;
    }

    private static Workbook RoundTrip(string key, Workbook workbook)
    {
        var adapter = Make(key);
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }

    [Theory]
    [MemberData(nameof(MultiSheetFormats))]
    public void AMultiSheetFormatKeepsEverySheetAndItsName(string key)
    {
        var loaded = RoundTrip(key, ThreeSheets());

        loaded.Sheets.Select(sheet => sheet.Name).Should().Equal(SheetNames,
            $"{key} can represent multiple sheets, so dropping or renaming one is silent data loss");

        foreach (var sheet in loaded.Sheets)
        {
            sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
                .Should().Be(new TextValue(sheet.Name + "-data"),
                    "each surviving sheet must keep its OWN content, not another sheet's");
        }
    }

    /// <summary>
    /// The first sheet is the one that survives, and its content must arrive intact. Losing the
    /// others is inherent to the format; losing the one that is kept would be a defect.
    /// </summary>
    [Theory]
    [MemberData(nameof(SingleSheetFormats))]
    public void ASingleSheetFormatKeepsTheFirstSheetsContent(string key)
    {
        var loaded = RoundTrip(key, ThreeSheets());

        loaded.Sheets.Should().HaveCount(1,
            $"{key} has no representation for a second sheet");

        var sheet = loaded.Sheets.Single();
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(new TextValue("Alpha-data"),
                "the surviving sheet must be the FIRST one. Keeping a different sheet's data would "
                + "silently substitute content the user was not looking at");
    }
}
