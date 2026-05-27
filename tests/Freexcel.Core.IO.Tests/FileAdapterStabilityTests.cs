using System.Text;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class FileAdapterStabilityTests
{
    public static IEnumerable<object[]> SaveCapableAdapters()
    {
        yield return [".xlsx", new XlsxFileAdapter()];
        yield return [".csv", new CsvFileAdapter()];
        yield return [".txt", new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t')];
        yield return [".tsv", new DelimitedTextFileAdapter(".tsv", "TSV (Tab-separated values)", '\t')];
        yield return [".tab", new DelimitedTextFileAdapter(".tab", "Tab-delimited text", '\t')];
        yield return [".xml", new SpreadsheetXmlFileAdapter()];
        yield return [".fxl", new NativeJsonAdapter()];
    }

    [Theory]
    [MemberData(nameof(SaveCapableAdapters))]
    public void LoadThenSave_WithoutEdits_PreservesSupportedSaveFormatBytes(string extension, IFileAdapter adapter)
    {
        var originalBytes = SaveToBytes(adapter, CreateStableWorkbook(extension));

        var loaded = adapter.Load(new MemoryStream(originalBytes, writable: false));
        var resavedBytes = SaveToBytes(adapter, loaded);

        resavedBytes.Should().Equal(originalBytes, $"opening and saving {extension} without edits must be file-stable");
    }

    [Fact]
    public void StabilityFixture_CoversEverySaveCapableRealAdapterFormat()
    {
        var covered = SaveCapableAdapters()
            .Select(row => (string)row[0])
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var saveCapable = RealAdapters()
            .SelectMany(adapter => adapter.Formats)
            .Where(format => format.CanSave)
            .Select(format => format.Extension)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        covered.Should().Equal(saveCapable);
    }

    [Fact]
    public void XlsxStabilityReplay_DoesNotHideWorkbookEdits()
    {
        var adapter = new XlsxFileAdapter();
        var originalBytes = SaveToBytes(adapter, CreateStableWorkbook(".xlsx"));

        var loaded = adapter.Load(new MemoryStream(originalBytes, writable: false));
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Changed"));
        var editedBytes = SaveToBytes(adapter, loaded);

        editedBytes.Should().NotEqual(originalBytes);
        var reloaded = adapter.Load(new MemoryStream(editedBytes, writable: false));
        reloaded.GetSheetAt(0).GetCell(2, 1)!.Value.Should().Be(new TextValue("Changed"));
    }

    [Fact]
    public void XlsxStabilityReplay_DoesNotDependOnCallerOwnedMemoryStreamBufferAfterLoad()
    {
        var adapter = new XlsxFileAdapter();
        var originalBytes = SaveToBytes(adapter, CreateStableWorkbook(".xlsx"));
        var callerOwnedBytes = originalBytes.ToArray();

        var loaded = adapter.Load(new MemoryStream(
            callerOwnedBytes,
            index: 0,
            count: callerOwnedBytes.Length,
            writable: true,
            publiclyVisible: true));

        Array.Fill<byte>(callerOwnedBytes, 0);
        var resavedBytes = SaveToBytes(adapter, loaded);

        resavedBytes.Should().Equal(originalBytes);
    }

    [Fact]
    public void XlsxStabilityReplay_CapturesOnlyAccessibleMemoryStreamSlice()
    {
        var adapter = new XlsxFileAdapter();
        var originalBytes = SaveToBytes(adapter, CreateStableWorkbook(".xlsx"));
        var prefix = Encoding.ASCII.GetBytes("prefix");
        var suffix = Encoding.ASCII.GetBytes("suffix");
        var callerOwnedBytes = prefix
            .Concat(originalBytes)
            .Concat(suffix)
            .ToArray();

        var loaded = adapter.Load(new MemoryStream(
            callerOwnedBytes,
            index: prefix.Length,
            count: originalBytes.Length,
            writable: true,
            publiclyVisible: true));

        Array.Fill<byte>(callerOwnedBytes, 0);
        var resavedBytes = SaveToBytes(adapter, loaded);

        resavedBytes.Should().Equal(originalBytes);
    }

    private static IReadOnlyList<IFileAdapter> RealAdapters() =>
    [
        new XlsxFileAdapter(),
        new LegacyXlsFileAdapter(),
        new CsvFileAdapter(),
        new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t'),
        new DelimitedTextFileAdapter(".tsv", "TSV (Tab-separated values)", '\t'),
        new DelimitedTextFileAdapter(".tab", "Tab-delimited text", '\t'),
        new SpreadsheetXmlFileAdapter(),
        new NativeJsonAdapter()
    ];

    private static byte[] SaveToBytes(IFileAdapter adapter, Workbook workbook)
    {
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        return stream.ToArray();
    }

    private static Workbook CreateStableWorkbook(string extension)
    {
        var workbook = new Workbook($"Stability{extension.TrimStart('.').ToUpperInvariant()}");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(12.5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Comma, quote \" test"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("=not a formula"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Unicode Kyiv"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new DateTimeValue(new DateTime(2026, 5, 26, 9, 30, 15).ToOADate()));

        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return workbook;

        sheet.SetFormula(new CellAddress(sheet.Id, 5, 2), "SUM(B2:B2)");
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue(Encoding.UTF8.GetString([0xE2, 0x9C, 0x93])));
        return workbook;
    }
}
