using System.Text;
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
    public void Save_RejectsNonAsciiWorkbookTextWithoutWritingPdfBytes()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Summary");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("R\u00e9sum\u00e9"));
        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:A1", sheet.Id));
        using var stream = new MemoryStream();

        var act = () => PortablePdfDocumentExporter.Save(workbook, exportPlan, stream);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Portable PDF export currently supports ASCII text only:*");
        stream.ToArray().Should().BeEmpty();
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
