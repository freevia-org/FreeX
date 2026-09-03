using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class R275_CultureProbeTests
{
    // Text-based formats whose payload is inspectable as characters. Excludes DBF (read-only by
    // design) and the binary/zip formats, which the round-trip theory covers instead.
    public static TheoryData<string> TextFormats() => new()
    {
        "csv", "csvutf8", "delimited", "prn", "slk", "dif", "xml", "json", "html",
    };

    public static TheoryData<string> RoundTripFormats() => new()
    {
        "csv", "csvutf8", "delimited", "prn", "slk", "dif", "xml", "json", "html", "ods",
    };

    private static IFileAdapter Make(string key) => key switch
    {
        "csv" => new CsvFileAdapter(),
        "csvutf8" => new CsvUtf8FileAdapter(),
        "delimited" => new DelimitedTextFileAdapter(".txt", "Text", '\t', false, false),
        "prn" => new PrnFileAdapter(),
        "slk" => new SlkFileAdapter(),
        "dif" => new DifFileAdapter(),
        "ods" => new OdsFileAdapter(),
        "xml" => new SpreadsheetXmlFileAdapter(),
        "json" => new NativeJsonAdapter(),
        "html" => new HtmlFileAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private static Workbook SingleNumber(double value)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(value));
        return workbook;
    }

    private static T UnderGermanCulture<T>(Func<T> body)
    {
        var previousCulture = Thread.CurrentThread.CurrentCulture;
        var previousUiCulture = Thread.CurrentThread.CurrentUICulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("de-DE");
        try
        {
            return body();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previousCulture;
            Thread.CurrentThread.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [MemberData(nameof(RoundTripFormats))]
    public void NumberSurvivesRoundTripUnderCommaDecimalCulture(string key)
    {
        var value = UnderGermanCulture(() =>
        {
            var adapter = Make(key);
            using var stream = new MemoryStream();
            adapter.Save(SingleNumber(1234.5678), stream);
            stream.Position = 0;
            var loaded = adapter.Load(stream);
            var sheet = loaded.Sheets.First();
            return sheet.GetValue(new CellAddress(sheet.Id, 1, 1));
        });

        value.Should().BeOfType<NumberValue>($"{key} must still read 1234.5678 back as a number");
        ((NumberValue)value).Value.Should().BeApproximately(1234.5678, 1e-9, key);
    }

    [Theory]
    [MemberData(nameof(TextFormats))]
    public void SavedPayloadUsesTheInvariantDecimalPointUnderCommaDecimalCulture(string key)
    {
        var payload = UnderGermanCulture(() =>
        {
            using var stream = new MemoryStream();
            Make(key).Save(SingleNumber(1234.5678), stream);
            return Encoding.UTF8.GetString(stream.ToArray());
        });

        payload.Should().Contain("1234.5678",
            $"{key} writes a file other applications read, so the decimal separator is part of the "
            + "wire format and must not follow the operator's locale");
        payload.Should().NotContain("1234,5678",
            $"{key} would round-trip a comma decimal through its own reader and still hand Excel a "
            + "file it parses as a different number -- the failure a symmetric round-trip cannot see");
    }
}
