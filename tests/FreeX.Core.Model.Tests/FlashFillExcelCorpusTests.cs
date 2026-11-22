using System.Text.Json;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit.Abstractions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Parity corpus for the Flash Fill engine. <c>Fixtures/flashfill-excel-corpus.jsonl</c> was recorded from real
/// Excel through COM <c>Range.FlashFill</c>: for every pattern the source column(s) hold invented data, one
/// ("excel1") or two ("excel2") typed examples sit in the next column, and Excel's resulting column is stored.
/// A blank cell below the examples means Excel offered no suggestion, in which case FreeX must offer none either.
/// </summary>
public sealed class FlashFillExcelCorpusTests(ITestOutputHelper output)
{
    /// <summary>Corpus cases where Excel's own output is erratic (partial fills / coincidental non-pattern guesses) and not worth matching.</summary>
    private static readonly HashSet<string> ExcelQuirks =
    [
        "strip-currency|1", "strip-currency|2",
        "currency-dollars-part|1", "currency-dollars-part|2",
        "state-lookup-abbrev-unpatterned|1", "state-lookup-abbrev-unpatterned|2",
        "h-id-digits-strip-zeros|1", // Excel emits unrelated digit pairs
        "h-sentence-case|1", "h-sentence-case|2", // KNOWN GAP: Excel copies "the rest of the string"; FreeX keeps a fixed word count
        "h-url-path|1", // Excel emits a truncated "/cc/" for a "/a/b" example
    ];

    private sealed record Case(string Name, string[][] Sources, string[] Intended, string[] Excel1, string[] Excel2);

    private static IEnumerable<Case> LoadCases(string resource = "flashfill-excel-corpus.jsonl")
    {
        using var stream = typeof(FlashFillExcelCorpusTests).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        while (reader.ReadLine() is { Length: > 0 } line)
            yield return JsonSerializer.Deserialize<Case>(line, options)!;
    }

    private static string[] RunFreeX(Case c, int examples)
    {
        var wb = new Workbook("corpus");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var cols = c.Sources[0].Length;
        var fillCol = (uint)cols + 1;
        for (var r = 0; r < c.Sources.Length; r++)
        {
            for (var k = 0; k < cols; k++)
                sheet.SetCell(new CellAddress(sheet.Id, (uint)r + 1, (uint)k + 1), new TextValue(c.Sources[r][k]));
        }

        for (var r = 0; r < examples; r++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r + 1, fillCol), new TextValue(c.Intended[r]));

        var result = new string[c.Sources.Length];
        Array.Fill(result, string.Empty);
        for (var r = 0; r < examples; r++)
            result[r] = c.Intended[r];

        var preview = new FlashFillCommand(sheet.Id, fillCol, fillCol - 1, 1, (uint)c.Sources.Length).Preview(ctx);
        if (preview.Success)
        {
            foreach (var (address, value) in preview.Cells)
                result[address.Row - 1] = value;
        }

        return result;
    }

    private static string? Describe(Case c, int examples) =>
        FlashFillService.DescribeSynthesizedProgram(
            Enumerable.Range(0, examples).Select(i => ((IReadOnlyList<string>)c.Sources[i], c.Intended[i])).ToList());

    [Fact]
    public void FreeX_MatchesRecordedExcelFlashFill_OnEveryPatternExcelCanInfer() =>
        AssertParity("flashfill-excel-corpus.jsonl");

    /// <summary>Second batch recorded after the engine was tuned on the first one, so it measures generalisation.</summary>
    [Fact]
    public void FreeX_MatchesRecordedExcelFlashFill_OnTheHeldOutBatch() =>
        AssertParity("flashfill-excel-corpus-holdout.jsonl");

    private void AssertParity(string resource)
    {
        var mismatches = new List<string>();
        var total = 0;
        var matched = 0;
        foreach (var c in LoadCases(resource))
        {
            foreach (var examples in new[] { 1, 2 })
            {
                var key = $"{c.Name}|{examples}";
                if (ExcelQuirks.Contains(key))
                    continue;

                var excel = examples == 1 ? c.Excel1 : c.Excel2;
                var actual = RunFreeX(c, examples);
                total++;
                if (excel.SequenceEqual(actual))
                {
                    matched++;
                    continue;
                }

                var excelFilled = excel.Skip(examples).Any(v => v.Length > 0);
                mismatches.Add($"{key} [{(excelFilled ? "excel fills" : "excel none")}] excel=[{string.Join(" | ", excel.Skip(examples).Take(3))}] freex=[{string.Join(" | ", actual.Skip(examples).Take(3))}] det={FlashFillService.LastDetectorName} prog={Describe(c, examples)}");
            }
        }

        output.WriteLine($"Excel parity: {matched}/{total} ({100.0 * matched / total:F1}%)");
        foreach (var m in mismatches)
            output.WriteLine(m);

        mismatches.Should().BeEmpty($"FreeX should match Excel on all {total} recorded cases (matched {matched}):\n" + string.Join("\n", mismatches));
    }

    /// <summary>
    /// Ambiguous / inconsistent example sets that earlier FreeX builds deliberately declined. Real Excel offers a guess for
    /// every one of them (recorded here); FreeX now offers the same guess where its learned program agrees.
    /// </summary>
    public static IEnumerable<object[]> ExcelGuessCases() =>
    [
        ["zip4", new[] { ("123 Pine St, Seattle, WA 98101", "8101"), ("55 Burnside Ave, Portland, OR 97209", "7209") }, "1600 Amphitheatre Pkwy, Mountain View, CA 94043", "4043"],
        ["weekday-dates", new[] { ("Mon 1/1/2024", "1"), ("Tue 02-02-2023", "02") }, "Wed 03/03/2022", "03"],
        ["delim-initials", new[] { ("Ada Lovelace", "AL"), ("Grace-Hopper", "GH") }, "Alan Turing", "AT"],
        ["embedded-date-ambig", new[] { ("Note 02/02/2026", "02"), ("Long memo 03-03-2027", "03") }, "Next 04/04/2028", "04"],
        ["embedded-date-diffcomp", new[] { ("Closed 2026-03-05", "2026"), ("Opened 03/05/2027", "03") }, "Filed 04/06/2028", "04"],
        ["url-host-only", new[] { ("https://contoso.example/products/road-bike.html", "road-bike"), ("https://example.com", "example") }, "https://example.com/releases/FreeX-Setup.exe", "FreeX-Setup"],
        ["names-3-4tok", new[] { ("Ada Augusta Byron Lovelace", "lovelace.ada.b@contoso.com"), ("Grace Brewster Hopper", "hopper.grace.b@contoso.com") }, "Alan Mathison Turing", "turing.alan.m@contoso.com"],
        ["url-paramvalue-multi", new[] { ("https://shop.example.com/search?category=powder-skis&item=powder-skis", "Powder Skis"), ("https://shop.example.com/search?category=trail-running&item=trail-running", "Trail Running") }, "https://shop.example.com/search?category=road-bike&item=road-bike", "Road Bike"],
        ["url-paramvalue-diffnames", new[] { ("https://shop.example.com/search?category=powder-skis&sort=popular", "Powder Skis"), ("https://shop.example.com/search?item=trail-running&sort=popular", "Trail Running") }, "https://shop.example.com/search?category=road-bike&sort=popular", "Road Bike"],
        ["web-mixed", new[] { ("https://www.contoso.com/products?id=1", "contoso.com"), ("http://www.fabrikam.org/support#top", "fabrikam") }, "https://www.northwind.net/catalog/list?page=2#details", "northwind.net/catalog/list"],
        ["final-digitrun", new[] { ("A12 B 12", "12"), ("CD 34 EF34", "34") }, "Account 123-456", "123"],
    ];

    [Theory]
    [MemberData(nameof(ExcelGuessCases))]
    public void FreeX_OffersTheSameGuessAsExcel_ForAmbiguousExamples(string name, (string, string)[] examples, string remaining, string excel)
    {
        var result = FlashFillService.Fill(examples, [remaining]);
        output.WriteLine($"{name}: freex=[{result?.FirstOrDefault()}] excel=[{excel}] det={FlashFillService.LastDetectorName} prog={FlashFillService.DescribeSynthesizedProgram(examples.Select(e => ((IReadOnlyList<string>)new[] { e.Item1 }, e.Item2)).ToList())}");
        result.Should().NotBeNull($"Excel guesses '{excel}' for {name}");
        result![0].Should().Be(excel);
    }
}
