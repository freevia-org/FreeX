using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using Xunit.Abstractions;

namespace Freexcel.Core.IO.Tests;

public sealed class CsvFileAdapterTests
{
    private readonly ITestOutputHelper output;

    public CsvFileAdapterTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void Save_ScansCellsWithoutCopyingUsedCellDictionary()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "CsvFileAdapter.cs"));

        source.Should().NotContain(
            "GetUsedCells()",
            "CSV save should build its output index in one streaming pass over occupied cells");
    }

    [Fact]
    public void Save_GroupsCellsByRowInsteadOfIndexingEveryCoordinate()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "CsvFileAdapter.cs"));

        source.Should().NotContain("Dictionary<(uint Row, uint Col), Cell>");
        source.Should().NotContain("TryGetValue((r, c)");
    }

    [Fact]
    public void Save_StreamsRowsWithoutPerRowStringArrayJoin()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "CsvFileAdapter.cs"));

        source.Should().NotContain("new string[endCol - startCol + 1]");
        source.Should().NotContain("string.Join(',', parts)");
    }

    [Fact]
    public void Load_ReusesAccessibleMemoryStreamBufferBeforeCopying()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.Core.IO", "DelimitedTextWorkbookReader.cs"));

        source.Should().Contain("TryGetBuffer(out var sourceBytes)");
        source.Should().NotContain(
            "stream.CopyTo(memory);",
            "accessible MemoryStream inputs should decode their remaining buffer slice without copying first");
    }

    [Fact]
    public void Save_DenseSyntheticSheet_ReportsThroughputAndAllocatedBytes()
    {
        const int rowCount = 300;
        const int colCount = 120;
        var workbook = CreateDenseWorkbook(rowCount, colCount);
        var adapter = new CsvFileAdapter();

        using (var warmup = new MemoryStream(rowCount * colCount * 12))
        {
            adapter.Save(workbook, warmup);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var stream = new MemoryStream(rowCount * colCount * 12);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        adapter.Save(workbook, stream);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        output.WriteLine(
            $"CSV dense save benchmark: rows={rowCount}, cols={colCount}, bytes={stream.Length}, elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}, allocatedBytes={allocatedBytes}");
        stream.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Save_SparseWideSyntheticSheet_ReportsThroughputAndAllocatedBytes()
    {
        const int rowCount = 5_000;
        const int colCount = 2_000;
        const int cellsPerRow = 3;
        var workbook = CreateSparseWideWorkbook(rowCount, colCount, cellsPerRow);
        var adapter = new CsvFileAdapter();
        var expectedCapacity = rowCount * (colCount + 32);

        using (var warmup = new MemoryStream(expectedCapacity))
        {
            adapter.Save(workbook, warmup);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var stream = new MemoryStream(expectedCapacity);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        adapter.Save(workbook, stream);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        output.WriteLine(
            $"CSV sparse wide save benchmark: rows={rowCount}, cols={colCount}, cellsPerRow={cellsPerRow}, bytes={stream.Length}, elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}, allocatedBytes={allocatedBytes}");
        stream.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Load_LargeAccessibleMemoryStream_ReportsThroughputAndAllocatedBytes()
    {
        const int rowCount = 20_000;
        const int colCount = 10;
        var bytes = CreateCsvBytes(rowCount, colCount);
        var adapter = new CsvFileAdapter();

        using (var warmup = new MemoryStream(bytes, index: 0, count: bytes.Length, writable: false, publiclyVisible: true))
        {
            adapter.Load(warmup);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var stream = new MemoryStream(bytes, index: 0, count: bytes.Length, writable: false, publiclyVisible: true);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var workbook = adapter.Load(stream);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        output.WriteLine(
            $"CSV accessible MemoryStream load benchmark: rows={rowCount}, cols={colCount}, bytes={bytes.Length}, elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}, allocatedBytes={allocatedBytes}");
        var sheet = workbook.Sheets.Single();
        sheet.GetValue(new CellAddress(sheet.Id, (uint)rowCount, (uint)colCount))
            .Should().Be(new NumberValue(rowCount * colCount));
        stream.Position.Should().Be(stream.Length);
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForBooleans()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("TRUE,false\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new BoolValue(true));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void Load_AccessibleMemoryStreamWithNonzeroPositionReadsRemainingSliceAndConsumesStream()
    {
        var padding = Encoding.UTF8.GetBytes("outside segment\r\n");
        var prefix = Encoding.UTF8.GetBytes("ignored,prefix\r\n");
        var csv = Encoding.UTF8.GetBytes("Name,Amount\r\nAlice,3.5\r\n");
        var buffer = padding.Concat(prefix).Concat(csv).ToArray();
        using var stream = new MemoryStream(
            buffer,
            index: padding.Length,
            count: prefix.Length + csv.Length,
            writable: false,
            publiclyVisible: true);
        stream.Position = prefix.Length;

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1)).Should().BeNull();
        stream.Position.Should().Be(stream.Length);
    }

    [Fact]
    public void Load_InaccessibleMemoryStreamWithNonzeroPositionUsesCopyPathAndConsumesStream()
    {
        var prefix = Encoding.UTF8.GetBytes("ignored,prefix\r\n");
        var csv = Encoding.UTF8.GetBytes("Name,Amount\r\nAlice,3.5\r\n");
        using var stream = new MemoryStream(prefix.Concat(csv).ToArray(), writable: false);
        stream.Position = prefix.Length;

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
        stream.Position.Should().Be(stream.Length);
    }

    [Fact]
    public void Load_AccessibleMemoryStreamPastLengthReadsEmptySliceAndConsumesStream()
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.UTF8.GetBytes("Name,Amount\r\nAlice,3.5\r\n"));
        stream.Position = stream.Length + 10;

        var workbook = new CsvFileAdapter().Load(stream);

        workbook.Sheets.Single().CellCount.Should().Be(0);
        stream.Position.Should().Be(stream.Length);
    }

    [Fact]
    public void Load_FallsBackToWindows1252WhenUtf8DecodingFails()
    {
        using var stream = new MemoryStream([0x43, 0x61, 0x66, 0xE9, 0x0D, 0x0A]);

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Café"));
    }

    [Theory]
    [MemberData(nameof(Utf32BomCsvPayloads))]
    public void Load_HonorsUtf32ByteOrderMarks(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new BoolValue(true));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Load_TreatsStandaloneCarriageReturnsAsRecordSeparators()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("A,B\rC,D\r"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("A"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("B"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("C"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new TextValue("D"));
    }

    [Fact]
    public void Load_KeepsQuotesInsideUnquotedFieldsAsLiteralText()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ab\"cd,\"quoted\"\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("ab\"cd"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("quoted"));
    }

    [Fact]
    public void Load_HonorsExcelSeparatorDirective()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=;\r\nName;Amount\r\nAlice;3.5\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
    }

    [Fact]
    public void Load_HonorsCommaSeparatorDirective()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=,\r\nName,Amount\r\nAlice,3.5\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
    }

    [Fact]
    public void Load_AppliesExcelSeparatorDirectiveOnlyToFirstPhysicalRecord()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=;\r\nsep=x\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("sep=x"));
    }

    [Fact]
    public void Load_ImportsExcelStyleFormulaFieldsAsFormulas()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2,=A1*2\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(2));
        var formulaCell = sheet.GetCell(new CellAddress(sheet.Id, 1, 2));
        formulaCell.Should().NotBeNull();
        formulaCell!.FormulaText.Should().Be("A1*2");
    }

    [Fact]
    public void Load_KeepsQuotedFormulaLikeFieldsAsLiteralText()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"=A1*2\"\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        var cell = sheet.GetCell(new CellAddress(sheet.Id, 1, 1));
        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue("=A1*2"));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForGettingDataErrorLiteral()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("#GETTING_DATA\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new ErrorValue("#GETTING_DATA"));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForPercentages()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("12.5%,-3%,4%\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(0.125));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(-0.03));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new NumberValue(0.04));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForCurrencyValues()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"$1,234.50\",($42.25)\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(1234.5));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(-42.25));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForMonthNameDates()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"May 17, 2026\",17-May-26\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForSpaceSeparatedMonthNameDates()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("17 May 2026,May 17 26\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForWeekdayMonthNameDates()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"Sunday, May 17, 2026\",\"Sun, May 17, 2026\"\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForWeekdayMonthNameDateTimes()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"Sunday, May 17, 2026 9:30\",\"Sun, May 17, 2026 9:30 PM\"\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 21, 30, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForIsoDateTimesWithOffsets()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026-05-17T12:30+03:00,2026-05-17T09:30Z\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        var expectedUtc = new DateTime(2026, 5, 17, 9, 30, 0);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(expectedUtc));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(expectedUtc));
    }

    [Fact]
    public void Save_WritesDateTimeValuesAsInvariantText()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("2026-05-17,2026-05-17 09:30:00,09:30:00\r\n");
    }

    [Fact]
    public void Save_PreservesFractionalSecondsInDateTimeValues()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 15, 250)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new DateTimeValue(new TimeSpan(0, 9, 30, 15, 250).TotalDays));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("2026-05-17 09:30:15.25,09:30:15.25\r\n");
    }

    [Fact]
    public void Save_IgnoresCellsBeyondExcelGridLimits()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("visible"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, CellAddress.MaxCol + 1), new TextValue("overflow-column"));
        sheet.SetCell(new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 1), new TextValue("overflow-row"));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("visible\r\n");
    }

    [Fact]
    public void Save_PreservesLeadingBlankColumnsFromWorksheetCoordinates()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("offset"));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be(",offset\r\n");
    }

    [Fact]
    public void Save_PreservesLeadingBlankRowsFromWorksheetCoordinates()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("offset"));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("\r\noffset\r\n");
    }

    [Fact]
    public void Save_QuotesFormulaLikeTextFieldsToPreserveLiteralText()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("=A1*2"));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("\"=A1*2\"\r\n");
    }

    [Fact]
    public void Save_QuotesTextFieldsThatNeedCsvEscaping()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a,b"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("say \"hi\""));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("line\nbreak"));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("\"a,b\",\"say \"\"hi\"\"\",\"line\nbreak\"\r\n");
    }

    [Fact]
    public void Save_RoundTripsFormulaLikeTextFieldsAsLiteralText()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("=A1*2"));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue("=A1*2"));
    }

    [Theory]
    [InlineData("+42")]
    [InlineData("-42")]
    public void Save_RoundTripsSignedNumericTextFieldsAsLiteralText(string text)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue(text));
    }

    [Theory]
    [InlineData("+$42.00")]
    [InlineData("-$42.00")]
    [InlineData("($42.25)")]
    public void Save_RoundTripsSignedCurrencyTextFieldsAsLiteralText(string text)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue(text));
    }

    [Theory]
    [InlineData("12.5%")]
    [InlineData("+12%")]
    [InlineData("-3%")]
    public void Save_RoundTripsPercentageTextFieldsAsLiteralText(string text)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue(text));
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData("false")]
    [InlineData(" TRUE ")]
    public void Save_RoundTripsBooleanLikeTextFieldsAsLiteralText(string text)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue(text));
    }

    [Theory]
    [InlineData("2026-05-17")]
    [InlineData("09:30")]
    [InlineData("May 17, 2026")]
    public void Save_RoundTripsDateTimeLikeTextFieldsAsLiteralText(string text)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue(text));
    }

    [Theory]
    [InlineData("#N/A")]
    [InlineData("#DIV/0!")]
    [InlineData("#GETTING_DATA")]
    [InlineData(" #N/A ")]
    public void Save_RoundTripsErrorLikeTextFieldsAsLiteralText(string text)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue(text));
    }

    [Fact]
    public void Save_WritesFormulaCellsAsExcelFormulaFields()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A1*2");

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("2,=A1*2\r\n");
    }

    public static TheoryData<byte[]> Utf32BomCsvPayloads() => new()
    {
        Encoding.UTF32.GetPreamble().Concat(Encoding.UTF32.GetBytes("TRUE,42\r\n")).ToArray(),
        new UTF32Encoding(bigEndian: true, byteOrderMark: true)
            .GetPreamble()
            .Concat(new UTF32Encoding(bigEndian: true, byteOrderMark: true).GetBytes("TRUE,42\r\n"))
            .ToArray()
    };

    private static string FindWorkspaceFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate workspace file {Path.Combine(parts)}.");
    }

    private static Workbook CreateDenseWorkbook(int rowCount, int colCount)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        for (var row = 1; row <= rowCount; row++)
        {
            for (var col = 1; col <= colCount; col++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), new NumberValue(row * col));
            }
        }

        return workbook;
    }

    private static Workbook CreateSparseWideWorkbook(int rowCount, int colCount, int cellsPerRow)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        for (var row = 1; row <= rowCount; row++)
        {
            for (var index = 0; index < cellsPerRow; index++)
            {
                var col = 1 + (index * (colCount - 1) / Math.Max(1, cellsPerRow - 1));
                sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), new NumberValue(row + col));
            }
        }

        return workbook;
    }

    private static byte[] CreateCsvBytes(int rowCount, int colCount)
    {
        var builder = new StringBuilder(rowCount * colCount * 8);
        for (var row = 1; row <= rowCount; row++)
        {
            for (var col = 1; col <= colCount; col++)
            {
                if (col > 1)
                    builder.Append(',');

                builder.Append(row * col);
            }

            builder.Append("\r\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
