using System.Globalization;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r315: a file must mean the same thing on every machine that opens it.
///
/// <para>The third side of the culture class. r275 scans the parse side for a provider-less
/// <c>double.TryParse</c>, which under de-DE turns <c>"1.5"</c> into <c>15</c> -- silently, no
/// exception. r313 proved the WRITE side behaviourally by varying the culture and comparing bytes.
/// Nothing yet loads a file under another culture, so a parse a source scan cannot see -- inside a
/// custom tokenizer, a date splitter, a third-party reader -- has been free to misread numbers on a
/// German machine.</para>
///
/// <para>The workbook is written once under the invariant culture and then loaded under each of
/// de-DE, fr-FR and tr-TR; the values that come back must be identical. Comparing the loaded MODEL
/// rather than re-saved bytes is deliberate: it is the values the user sees that matter, and it
/// isolates the reader from anything the writer might also get wrong.</para>
/// </summary>
public sealed class R315_ReadingIsCultureIndependentTests
{
    private static readonly (uint Row, double Value)[] Numbers =
    [
        (1, 1.5), (2, 1234567.891), (3, -0.125), (5, 0.075),
    ];

    private static Workbook SourceWorkbook()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Data");
        foreach (var (row, value) in Numbers)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(value));

        var dateStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "yyyy-mm-dd" });
        var dateCell = Cell.FromValue(DateTimeValue.FromDateTime(new DateTime(2024, 6, 1)));
        dateCell.StyleId = dateStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), dateCell);

        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("Istanbul FILE file"));
        sheet.SetFormula(new CellAddress(sheet.Id, 7, 1), "A1*2.5");
        return workbook;
    }

    public static TheoryData<string, string> AdaptersAndCultures()
    {
        var data = new TheoryData<string, string>();
        foreach (var adapter in new[]
                 {
                     nameof(XlsxFileAdapter), nameof(OdsFileAdapter),
                     nameof(NativeJsonAdapter), nameof(SpreadsheetXmlFileAdapter),
                 })
        {
            foreach (var culture in new[] { "de-DE", "fr-FR", "tr-TR" })
                data.Add(adapter, culture);
        }

        return data;
    }

    private static IFileAdapter Create(string name) => name switch
    {
        nameof(XlsxFileAdapter) => new XlsxFileAdapter(),
        nameof(OdsFileAdapter) => new OdsFileAdapter(),
        nameof(NativeJsonAdapter) => new NativeJsonAdapter(),
        nameof(SpreadsheetXmlFileAdapter) => new SpreadsheetXmlFileAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unmapped adapter"),
    };

    /// <summary>
    /// Runs the load on a DEDICATED thread carrying the culture, for the reason recorded in r313's
    /// equivalent helper: setting the ambient culture on a shared, pooled test thread made that test
    /// pass alone and fail intermittently in a full-lane run.
    /// </summary>
    private static Workbook LoadUnder(IFileAdapter adapter, byte[] saved, string cultureName)
    {
        Workbook? loaded = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;

                using var stream = new MemoryStream(saved);
                loaded = adapter.Load(stream);
            }
            catch (Exception ex)
            {
                // Rethrown on the test's own thread below; without this the thread dies silently and
                // the failure reads as "loaded was null" rather than as what actually went wrong.
                failure = ex;
            }
        });

        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new InvalidOperationException($"loading under {cultureName} threw", failure);

        return loaded ?? throw new InvalidOperationException("the load thread produced nothing");
    }

    [Theory]
    [MemberData(nameof(AdaptersAndCultures))]
    public void AFileWrittenOnceMeansTheSameThingInEveryCulture(string adapterName, string cultureName)
    {
        var adapter = Create(adapterName);
        using var written = new MemoryStream();
        adapter.Save(SourceWorkbook(), written);
        var bytes = written.ToArray();

        var invariant = LoadUnder(Create(adapterName), bytes, CultureInfo.InvariantCulture.Name);
        var localized = LoadUnder(Create(adapterName), bytes, cultureName);

        var invariantSheet = invariant.Sheets[0];
        var localizedSheet = localized.Sheets[0];

        foreach (var (row, expected) in Numbers)
        {
            var address = new CellAddress(localizedSheet.Id, row, 1);
            var invariantValue = invariantSheet.GetCell(new CellAddress(invariantSheet.Id, row, 1))?.Value;
            var localizedValue = localizedSheet.GetCell(address)?.Value;

            invariantValue.Should().BeOfType<NumberValue>(
                $"the fixture must load a number at row {row} for this comparison to mean anything");
            ((NumberValue)invariantValue!).Value.Should().Be(expected,
                $"{adapterName} must read back what it wrote at row {row}");

            localizedValue.Should().Be(invariantValue,
                $"{adapterName} read row {row} differently under {cultureName}; a provider-less parse "
                + "turns \"1.5\" into 15 on a machine whose decimal separator is a comma");
        }

        localizedSheet.GetCell(new CellAddress(localizedSheet.Id, 6, 1))?.Value
            .Should().Be(invariantSheet.GetCell(new CellAddress(invariantSheet.Id, 6, 1))?.Value,
                "text must survive a culture whose case mapping differs");
    }
}
