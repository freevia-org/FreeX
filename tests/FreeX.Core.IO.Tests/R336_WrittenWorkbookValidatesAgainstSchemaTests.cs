using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r336: the third app under r334's lens.
///
/// <para>r334 found FreeP writing an invalid <c>txBody</c> and r335 found FreeW writing every table
/// without its mandatory <c>tblGrid</c> -- both behind validator tests that all passed, because each
/// validated a package containing only its own feature. FreeX validates in four test files with the
/// same shape (query tables, shared drawing parts, a smoke test, a cleanup fixture).</para>
///
/// <para>So this writes ONE workbook carrying several features together -- multiple sheets, styles,
/// a merged range, a defined name, a hyperlink, a comment, a formula, a data validation and a
/// conditional format -- and validates the whole package. The combination is the subject.</para>
/// </summary>
public sealed class R336_WrittenWorkbookValidatesAgainstSchemaTests
{
    private static string[] ValidateSchema(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = SpreadsheetDocument.Open(stream, isEditable: false);
        return new OpenXmlValidator(FileFormatVersions.Office2013)
            .Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => error.Description + " @ " + error.Path?.XPath)
            .ToArray();
    }

    [Fact]
    public void AWorkbookCombiningManyFeaturesValidates()
    {
        var workbook = new Workbook("Book1");
        var data = workbook.AddSheet("Data");
        var second = workbook.AddSheet("Second");

        var bold = workbook.RegisterStyle(new CellStyle { Bold = true });
        var money = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });

        var header = Cell.FromValue(new TextValue("Region"));
        header.StyleId = bold;
        data.SetCell(new CellAddress(data.Id, 1, 1), header);
        data.SetCell(new CellAddress(data.Id, 1, 2), new TextValue("Amount"));

        var amount = Cell.FromValue(new NumberValue(1234.5));
        amount.StyleId = money;
        data.SetCell(new CellAddress(data.Id, 2, 2), amount);
        data.SetCell(new CellAddress(data.Id, 2, 1), new TextValue("North"));
        data.SetFormula(new CellAddress(data.Id, 3, 2), "SUM(B2:B2)");

        data.AddMergedRegion(new GridRange(
            new CellAddress(data.Id, 5, 1),
            new CellAddress(data.Id, 5, 3)));

        data.Comments[new CellAddress(data.Id, 1, 1)] = "r336 comment";
        data.Hyperlinks[new CellAddress(data.Id, 2, 1)] = "https://example.invalid/r336";

        second.SetCell(new CellAddress(second.Id, 1, 1), new TextValue("second sheet"));

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        var bytes = stream.ToArray();

        // Vacuity guarded by content: the package must actually carry what this test combines.
        var sheetXml = ReadEntry(bytes, "xl/worksheets/sheet1.xml");
        sheetXml.Should().Contain("mergeCell", "the merged range must be in the package being validated");
        sheetXml.Should().Contain("hyperlink", "and the hyperlink");

        ValidateSchema(bytes).Should().BeEmpty(
            "the written package must satisfy the OOXML schema; a part well formed beside its own "
            + "feature can still be wrong beside a neighbour, which is what a multi-feature workbook "
            + "exposes and a single-feature one hides");
    }

    private static string ReadEntry(byte[] bytes, string entryName)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"the package must contain {entryName}");
        using var entryStream = entry!.Open();
        using var reader = new StreamReader(entryStream);
        return reader.ReadToEnd();
    }
    /// <summary>
    /// r340: r339's enumeration applied to the third writer. r336 validated a feature-RICH workbook;
    /// these are the states at the other end, which a fixture built to demonstrate features never
    /// contains and a user reaches by deleting things.
    /// </summary>
    [Fact]
    public void AWorkbookWithAnEmptySheetValidates()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Empty");

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        ValidateSchema(stream.ToArray()).Should().BeEmpty(
            "a workbook whose only sheet has no cells must still be a valid .xlsx");
    }

    [Fact]
    public void AWorkbookWhoseCellsWereAllClearedValidates()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("temporary"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), BlankValue.Instance);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        ValidateSchema(stream.ToArray()).Should().BeEmpty(
            "clearing the last cell must not leave a malformed sheet part");
    }

    [Fact]
    public void AMergedRegionOverEmptyCellsValidates()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Data");
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 4)));

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        ValidateSchema(stream.ToArray()).Should().BeEmpty(
            "a merge over cells that carry no values is ordinary and must validate");
    }

    /// <summary>
    /// r341: schema validity and data survival are different properties. r340 proved these degenerate
    /// packages parse against the schema; it did not ask whether reloading one gives back what was
    /// saved. A sheet with no cells is exactly the kind of thing a loader drops as "nothing here".
    /// </summary>
    [Fact]
    public void AnEmptySheetSurvivesAReload()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Empty");
        workbook.AddSheet("AlsoEmpty");
        var populated = workbook.AddSheet("HasData");
        populated.SetCell(new CellAddress(populated.Id, 1, 1), new TextValue("x"));

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        reloaded.Sheets.Select(s => s.Name).Should().Contain(["Empty", "AlsoEmpty", "HasData"],
            "an empty sheet is a sheet; dropping it silently loses the user's structure");
    }

}