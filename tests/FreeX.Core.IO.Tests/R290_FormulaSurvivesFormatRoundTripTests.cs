using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r290: r275 established that a NUMBER survives every text-based format adapter. A formula is the
/// higher-risk payload -- it is the thing a user would be most upset to lose silently -- and nothing
/// checked it across the adapter set.
///
/// <para>Losing a formula is not necessarily a bug: CSV and PRN have nowhere to put one, so writing
/// the computed value is correct and is what Excel does. What would be a bug is a format that CLAIMS
/// to carry formulas losing them, or any adapter losing the VALUE as well. These tests separate the
/// two: every adapter must preserve the result, and the formula-carrying formats must preserve the
/// formula.</para>
/// </summary>
public sealed class R290_FormulaSurvivesFormatRoundTripTests
{
    // Formats whose on-disk representation has a formula slot.
    //
    // DIF is deliberately NOT here. The first draft of this test listed it, and it failed -- but the
    // adapter is right and the classification was wrong: DIF is a value-only interchange format
    // ("Single sheet, values only ... No formulas, formats, or structure"), which is what the real
    // format supports and what Excel writes. Its deliberate flattening is pinned separately below
    // rather than being quietly dropped from the list.
    public static TheoryData<string> FormulaCarryingFormats() => new()
    {
        "slk", "ods", "xml", "json",
    };

    // Every round-trippable adapter, formula-carrying or not.
    public static TheoryData<string> AllRoundTripFormats() => new()
    {
        "csv", "csvutf8", "prn", "slk", "dif", "ods", "xml", "json", "html",
    };

    private static IFileAdapter Make(string key) => key switch
    {
        "csv" => new CsvFileAdapter(),
        "csvutf8" => new CsvUtf8FileAdapter(),
        "prn" => new PrnFileAdapter(),
        "slk" => new SlkFileAdapter(),
        "dif" => new DifFileAdapter(),
        "ods" => new OdsFileAdapter(),
        "xml" => new SpreadsheetXmlFileAdapter(),
        "json" => new NativeJsonAdapter(),
        "html" => new HtmlFileAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    /// <summary>A1=2, A2=3, A3==A1+A2 evaluated to 5.</summary>
    private static Workbook WorkbookWithFormula()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3));

        // SetFormula stores the expression without a leading '='; the calc engine supplies the
        // value separately, so the cached result is set directly to mimic a recalculated workbook.
        var formulaAddress = new CellAddress(sheet.Id, 3, 1);
        sheet.SetFormula(formulaAddress, "A1+A2");
        sheet.GetCell(formulaAddress)!.Value = new NumberValue(5);
        return workbook;
    }

    private static Sheet RoundTrip(string key, Workbook workbook)
    {
        var adapter = Make(key);
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream).Sheets.First();
    }

    /// <summary>
    /// The computed value must survive EVERY format. A format without a formula slot writes the
    /// result, which is correct; a format that loses the result as well has lost the user's data.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllRoundTripFormats))]
    public void TheComputedValueSurvivesEveryFormat(string key)
    {
        var sheet = RoundTrip(key, WorkbookWithFormula());

        sheet.GetValue(new CellAddress(sheet.Id, 3, 1))
            .Should().Be(new NumberValue(5),
                $"{key} must carry at least the result of the formula; losing that loses the user's "
                + "data rather than merely flattening it");
    }

    /// <summary>
    /// Formats with a formula slot must keep the formula itself, not just its result -- otherwise a
    /// save/open cycle silently converts a live model into static numbers.
    /// </summary>
    [Theory]
    [MemberData(nameof(FormulaCarryingFormats))]
    public void AFormulaCarryingFormatKeepsTheFormula(string key)
    {
        var sheet = RoundTrip(key, WorkbookWithFormula());

        var cell = sheet.GetCell(new CellAddress(sheet.Id, 3, 1));

        cell.Should().NotBeNull($"{key} must round-trip the formula cell");
        cell!.FormulaText.Should().NotBeNullOrEmpty(
            $"{key} has a formula slot, so a save/open cycle must not silently flatten a live "
            + "formula into a static number");
        cell.FormulaText.Should().Contain("A1",
            "the round-tripped formula must still reference its precedents");
    }

    /// <summary>
    /// DIF flattens a formula to its value by design, and that is worth pinning rather than leaving
    /// as an absence. If someone later teaches the adapter to emit formulas, this test says the
    /// format has no slot for them and a reader would not understand the file.
    /// </summary>
    [Fact]
    public void DifFlattensFormulasToValuesDeliberately()
    {
        var sheet = RoundTrip("dif", WorkbookWithFormula());
        var cell = sheet.GetCell(new CellAddress(sheet.Id, 3, 1));

        cell.Should().NotBeNull();
        cell!.Value.Should().Be(new NumberValue(5),
            "the result must survive even though the expression cannot");
        cell.FormulaText.Should().BeNull(
            "DIF is a value-only interchange format -- it has no formula slot, and Excel writes "
            + "values for it too, so flattening is correct rather than a loss");
    }
}
