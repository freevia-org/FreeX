using System.Diagnostics;
using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit.Abstractions;

namespace FreeX.Core.IO.Tests;

public sealed partial class CsvFileAdapterTests
{
    private readonly ITestOutputHelper output;

    public CsvFileAdapterTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    public static TheoryData<byte[]> Utf32BomCsvPayloads() => new()
    {
        Encoding.UTF32.GetPreamble().Concat(Encoding.UTF32.GetBytes("TRUE,42\r\n")).ToArray(),
        new UTF32Encoding(bigEndian: true, byteOrderMark: true)
            .GetPreamble()
            .Concat(new UTF32Encoding(bigEndian: true, byteOrderMark: true).GetBytes("TRUE,42\r\n"))
            .ToArray()
    };

    public static TheoryData<byte[]> Utf16BomCsvPayloads() => new()
    {
        Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("Name,Amount,Flag\r\nCaf\u00e9,42,TRUE\r\n"))
            .ToArray(),
        Encoding.BigEndianUnicode.GetPreamble()
            .Concat(Encoding.BigEndianUnicode.GetBytes("Name,Amount,Flag\r\nCaf\u00e9,42,TRUE\r\n"))
            .ToArray()
    };

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

    private sealed class ForwardOnlyReadStream(byte[] bytes) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (position >= bytes.Length)
                return 0;

            var read = Math.Min(count, bytes.Length - position);
            Array.Copy(bytes, position, buffer, offset, read);
            position += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
