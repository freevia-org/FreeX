using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class PortablePdfDocumentExporterTests
{
    [Fact]
    public void Save_WritesPortablePdfWithWorkbookSheetAndCellText()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Summary");
        var titleStyle = workbook.RegisterStyle(new CellStyle { Bold = true, FillColor = new CellColor(230, 240, 255) });
        var currencyStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.GetCell(1, 1)!.StyleId = titleStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));
        sheet.GetCell(2, 2)!.StyleId = currencyStyle;

        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:B2", sheet.Id));
        using var stream = new MemoryStream();

        var result = PortablePdfDocumentExporter.Save(workbook, exportPlan, stream);

        result.PageCount.Should().Be(1);
        result.StatusText.Should().Be("Exported portable PDF: 1 page.");
        var pdf = Encoding.ASCII.GetString(stream.ToArray());
        pdf.Should().StartWith("%PDF-1.7");
        pdf.Should().Contain("/Type /Catalog");
        pdf.Should().Contain("xref");
        pdf.Should().Contain("(Budget) Tj");
        pdf.Should().Contain("(Summary - sheet page 1 - export page 1 of 1) Tj");
        pdf.Should().Contain("(Region) Tj");
        pdf.Should().Contain("(North) Tj");
        pdf.Should().Contain("($42.00) Tj");
    }

    [Fact]
    public void Save_RejectsUnavailableExportPlan()
    {
        var workbook = new Workbook("Hidden");
        var sheet = workbook.AddSheet("Hidden");
        sheet.IsHidden = true;
        var printPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 5),
            WorkbookExportPrintSurface.MacOs);
        var exportPlan = PortablePdfExportPlanner.CreatePlan(printPlan);
        using var stream = new MemoryStream();

        var act = () => PortablePdfDocumentExporter.Save(workbook, exportPlan, stream);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Portable PDF export cannot start because the export print plan is not ready:*");
    }

    [Fact]
    public void Save_PathOverloadDoesNotOverwriteExistingFileWhenPlanIsUnavailable()
    {
        var workbook = new Workbook("Hidden");
        var sheet = workbook.AddSheet("Hidden");
        sheet.IsHidden = true;
        var exportPlan = PortablePdfExportPlanner.CreatePlan(
            WorkbookExportPrintPlanner.CreatePlan(
                workbook,
                new WorkbookExportPrintIntent(
                    WorkbookExportPrintScope.ActiveSheet,
                    WorkbookExportPrintOutputKind.Pdf),
                new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 5),
                WorkbookExportPrintSurface.MacOs));
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "keep me", Encoding.ASCII);

        try
        {
            var act = () => PortablePdfDocumentExporter.Save(workbook, exportPlan, path);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("Portable PDF export cannot start because the export print plan is not ready:*");
            File.ReadAllText(path, Encoding.ASCII).Should().Be("keep me");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_StreamOverloadSupportsNonSeekableWritableStreams()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Summary");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:A1", sheet.Id));
        using var stream = new NonSeekableWriteStream();

        PortablePdfDocumentExporter.Save(workbook, exportPlan, stream);

        Encoding.ASCII.GetString(stream.ToArray()).Should().StartWith("%PDF-1.7");
    }

    [Fact]
    public void Save_StreamOverloadOverwritesSeekableStreamFromStart()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Summary");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:A1", sheet.Id));
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes("old-prefix"));

        PortablePdfDocumentExporter.Save(workbook, exportPlan, stream);

        var pdf = Encoding.ASCII.GetString(stream.ToArray());
        pdf.Should().StartWith("%PDF-1.7");
        pdf.Should().NotContain("old-prefix");
    }

    [Fact]
    public void Save_WritesStructurallyConsistentStreamLengthXrefAndStartxref()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Summary");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:A1", sheet.Id));
        using var stream = new MemoryStream();

        PortablePdfDocumentExporter.Save(workbook, exportPlan, stream);

        var bytes = stream.ToArray();
        var pdf = Encoding.ASCII.GetString(bytes);
        var xrefIndex = pdf.IndexOf("xref\n", StringComparison.Ordinal);
        xrefIndex.Should().BeGreaterThan(0);
        var startxrefMatch = Regex.Match(pdf, @"startxref\n(?<offset>\d+)\n%%EOF\n$");
        startxrefMatch.Success.Should().BeTrue();
        int.Parse(startxrefMatch.Groups["offset"].Value, CultureInfo.InvariantCulture)
            .Should()
            .Be(xrefIndex);

        var streamMatch = Regex.Match(
            pdf,
            @"<< /Length (?<length>\d+) >>\nstream\n(?<content>.*?)endstream",
            RegexOptions.Singleline);
        streamMatch.Success.Should().BeTrue();
        Encoding.ASCII.GetByteCount(streamMatch.Groups["content"].Value)
            .Should()
            .Be(int.Parse(streamMatch.Groups["length"].Value, CultureInfo.InvariantCulture));

        var xrefLines = pdf[(xrefIndex + "xref\n".Length)..]
            .Split('\n')
            .TakeWhile(line => !line.StartsWith("trailer", StringComparison.Ordinal))
            .ToArray();
        xrefLines.Should().NotBeEmpty();
        var xrefLineIndex = 0;
        var inUseEntries = 0;
        while (xrefLineIndex < xrefLines.Length)
        {
            var subsectionMatch = Regex.Match(xrefLines[xrefLineIndex++], @"^(?<first>\d+) (?<count>\d+)$");
            subsectionMatch.Success.Should().BeTrue();
            var firstObjectId = int.Parse(subsectionMatch.Groups["first"].Value, CultureInfo.InvariantCulture);
            var entryCount = int.Parse(subsectionMatch.Groups["count"].Value, CultureInfo.InvariantCulture);
            xrefLines.Length.Should().BeGreaterThanOrEqualTo(xrefLineIndex + entryCount);

            for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
            {
                var entry = xrefLines[xrefLineIndex++];
                var entryMatch = Regex.Match(entry, @"^(?<offset>\d{10}) \d{5} (?<state>[fn]) ?$");
                entryMatch.Success.Should().BeTrue();
                if (entryMatch.Groups["state"].Value != "n")
                    continue;

                inUseEntries++;
                var objectId = firstObjectId + entryIndex;
                var offset = int.Parse(entryMatch.Groups["offset"].Value, CultureInfo.InvariantCulture);
                Encoding.ASCII.GetString(bytes, offset, $"{objectId} 0 obj\n".Length)
                    .Should()
                    .Be($"{objectId} 0 obj\n");
            }
        }
        inUseEntries.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Save_WritesWinAnsiWorkbookSheetAndCellTextAsHex()
    {
        var workbook = new Workbook("Budget Caf\u00e9");
        var sheet = workbook.AddSheet("R\u00e9sum\u00e9");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("S\u00e3o Paulo \u20ac \u2013"));
        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:A1", sheet.Id));
        using var stream = new MemoryStream();

        var result = PortablePdfDocumentExporter.Save(workbook, exportPlan, stream);

        result.PageCount.Should().Be(1);
        var pdf = Encoding.ASCII.GetString(stream.ToArray());
        pdf.Should().Contain("/Encoding /WinAnsiEncoding");
        pdf.Should().NotContain("/Subtype /Type0");
        pdf.Should().NotContain("/Encoding /Identity-H");
        pdf.Should().NotContain("/ArialMT");
        pdf.Should().Contain("<42756467657420436166E9> Tj");
        pdf.Should().Contain("<53E36F205061756C6F20802096> Tj");
    }

    [Fact]
    public void Save_WritesCurrentCultureWinAnsiDateTextAsHex()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("fr-FR");
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Summary");
        var dateStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "[$-F800]" });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(new DateTime(2026, 12, 1)));
        sheet.GetCell(1, 1)!.StyleId = dateStyle;
        // R112-pdf-width-overflow-1: a long-date format ("1 décembre 2026") is wider than the
        // default ~8-character column, and the PDF export now correctly reproduces Excel's '#'
        // width-overflow indicator for over-wide dates (see PortablePdfPageContentPlannerTests'
        // R112_* tests) -- widen the column here so this test keeps exercising what it actually
        // targets (WinAnsi hex encoding of the accented date text) instead of colliding with that
        // now-correct overflow behavior.
        sheet.ColumnWidths[1] = 60;
        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:A1", sheet.Id));
        using var stream = new MemoryStream();

        PortablePdfDocumentExporter.Save(workbook, exportPlan, stream);

        var expectedDate = new DateTime(2026, 12, 1)
            .ToString(CultureInfo.CurrentCulture.DateTimeFormat.LongDatePattern, CultureInfo.CurrentCulture.DateTimeFormat);
        expectedDate.Should().Contain("\u00e9");
        var pdf = Encoding.ASCII.GetString(stream.ToArray());
        pdf.Should().Contain($"<{EncodeExpectedFrWinAnsiHex(expectedDate)}> Tj");
    }

    [Fact]
    public void Save_RejectsTextOutsideWinAnsiWithoutWritingPdfBytes()
    {
        var workbook = new Workbook("Budget \u041a\u0438\u0457\u0432");
        var sheet = workbook.AddSheet("Summary");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region \uD83D\uDCC8"));
        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:A1", sheet.Id));
        using var stream = new MemoryStream();

        var act = () => PortablePdfDocumentExporter.Save(workbook, exportPlan, stream);

        var exception = act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Portable PDF export currently supports ASCII and WinAnsi text only;*")
            .Which;
        exception.Message.Should().Contain("real licensed TrueType/OpenType font subset");
        exception.Message.Should().Contain("Type0/Identity-H text");
        exception.Message.Should().Contain("ToUnicode mappings");
        exception.Message.Should().Contain("parser, render, and text extraction validation");
        exception.Message.Should().Contain("workbook name on export page 1 contains U+041A");
        exception.Message.Should().Contain("cell A1 on export page 1 contains U+1F4C8");
        stream.ToArray().Should().BeEmpty();
    }

    [Fact]
    public void Save_PathOverloadDoesNotOverwriteExistingFileWhenTextIsOutsideWinAnsi()
    {
        var workbook = new Workbook("Budget \u041a\u0438\u0457\u0432");
        var sheet = workbook.AddSheet("Summary");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:A1", sheet.Id));
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "keep me", Encoding.ASCII);

        try
        {
            var act = () => PortablePdfDocumentExporter.Save(workbook, exportPlan, path);

            var exception = act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("Portable PDF export currently supports ASCII and WinAnsi text only;*")
                .Which;
            exception.Message.Should().Contain("workbook name on export page 1 contains U+041A");
            File.ReadAllText(path, Encoding.ASCII).Should().Be("keep me");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static PortablePdfExportPlan CreateExportPlan(
        Workbook workbook,
        Sheet sheet,
        GridRange range)
    {
        var printPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: ResolveSheetIndex(workbook, sheet)),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 5),
            WorkbookExportPrintSurface.MacOs);

        printPlan.IsReady.Should().BeTrue();
        printPlan.SheetPlans.Single().PrintRange.Should().Be(range);
        return PortablePdfExportPlanner.CreatePlan(printPlan);
    }

    private static int ResolveSheetIndex(Workbook workbook, Sheet sheet)
    {
        for (var index = 0; index < workbook.Sheets.Count; index++)
        {
            if (workbook.Sheets[index].Id == sheet.Id)
                return index;
        }

        throw new InvalidOperationException("Test workbook does not contain the requested sheet.");
    }

    private static string EncodeExpectedFrWinAnsiHex(string text)
    {
        var builder = new StringBuilder(text.Length * 2);
        foreach (var ch in text)
        {
            var value = ch switch
            {
                >= ' ' and <= '~' => (byte)ch,
                '\u00e9' => (byte)0xE9,
                _ => throw new InvalidOperationException($"Unexpected non-WinAnsi test character: U+{(int)ch:X4}.")
            };
            builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private sealed class NonSeekableWriteStream : Stream
    {
        private readonly MemoryStream _inner = new();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public byte[] ToArray() => _inner.ToArray();

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            _inner.Write(buffer, offset, count);
    }
}
