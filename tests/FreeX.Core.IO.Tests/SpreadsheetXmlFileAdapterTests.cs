using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class SpreadsheetXmlFileAdapterTests
{
    private static MemoryStream StreamFromString(string value) =>
        new(Encoding.UTF8.GetBytes(value));

    private static MemoryStream Utf16StreamFromString(string value) =>
        new(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(value)).ToArray());

    private static Stream NonSeekableStreamFromString(string value) =>
        new NonSeekableReadStream(StreamFromString(value));

    private static string FindRepoFile(params string[] relativeParts) => TestWorkspaceFiles.FindRepoFile(relativeParts);

    private static Workbook CreateDenseWorkbook(int sheetCount, int rowCount, int columnCount)
    {
        var workbook = new Workbook("SpreadsheetML Dense");
        var currency = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var percent = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00%" });
        for (var sheetIndex = 1; sheetIndex <= sheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Sheet {sheetIndex}");
            for (uint row = 1; row <= rowCount; row++)
            {
                for (uint column = 1; column <= columnCount; column++)
                {
                    var address = new CellAddress(sheet.Id, row, column);
                    var selector = (row + column + (uint)sheetIndex) % 11;
                    if (selector == 0)
                    {
                        sheet.SetCell(address, new Cell
                        {
                            FormulaText = $"SUM(A{Math.Max(1u, row - 1)}:A{row})",
                            Value = new NumberValue(row + column),
                            StyleId = currency
                        });
                    }
                    else if (selector == 1)
                    {
                        sheet.SetCell(address, new Cell
                        {
                            Value = new NumberValue(row * column),
                            StyleId = percent
                        });
                    }
                    else if (selector == 2)
                    {
                        sheet.SetCell(address, new TextValue($"R{row}C{column}"));
                    }
                    else
                    {
                        sheet.SetCell(address, new NumberValue(row + column + (uint)sheetIndex));
                    }
                }
            }
        }

        return workbook;
    }

    private static Workbook CreateRichDenseWorkbook(int sheetCount, int rowCount, int columnCount)
    {
        var workbook = CreateDenseWorkbook(sheetCount, rowCount, columnCount);
        var styleOnly = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.0000" });
        var styleOnlyCol = (uint)columnCount + 1;

        for (var sheetIndex = 0; sheetIndex < workbook.Sheets.Count; sheetIndex++)
        {
            var sheet = workbook.Sheets[sheetIndex];
            for (uint row = 1; row <= rowCount; row += 8)
            {
                var hyperlinkAddress = new CellAddress(sheet.Id, row, 2);
                sheet.Hyperlinks[hyperlinkAddress] = $"https://example.com/sheet-{sheetIndex + 1}/row-{row}";
                sheet.HyperlinkMetadata[hyperlinkAddress] = new HyperlinkMetadata(
                    HyperlinkTargetKind.ExistingFileOrWebPage,
                    $"Open row {row}",
                    "");

                sheet.Comments[new CellAddress(sheet.Id, row, 3)] = $"Review row {row}";
                sheet.SetStyleOnly(row, styleOnlyCol, styleOnly);
                sheet.RowHeights[row] = 18.5 + row % 3;
            }

            for (uint row = 2; row < rowCount; row += 12)
            {
                sheet.AddMergedRegion(new GridRange(
                    new CellAddress(sheet.Id, row, 4),
                    new CellAddress(sheet.Id, row + 1, 6)));
            }

            sheet.HiddenRows.Add(Math.Min((uint)rowCount, 7u));
        }

        return workbook;
    }

    private static MemoryStream PositionedStreamFromString(string prefix, string value)
    {
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var stream = new MemoryStream(prefixBytes.Concat(valueBytes).ToArray());
        stream.Position = prefixBytes.Length;
        return stream;
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
