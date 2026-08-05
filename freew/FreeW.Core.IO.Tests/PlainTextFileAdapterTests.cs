using System;
using System.IO;
using System.Linq;
using System.Text;

namespace FreeW.Core.IO.Tests;

public class PlainTextFileAdapterTests
{
    private static TextDocument DocOf(params string[] lines)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var line in lines)
            document.Blocks.Add(new Paragraph(line));
        return document;
    }

    private static byte[] Save(TextDocument document, PlainTextFileAdapter adapter)
    {
        using var ms = new MemoryStream();
        adapter.Save(document, ms);
        return ms.ToArray();
    }

    private static string[] LinesOf(TextDocument document) =>
        document.Blocks.OfType<Paragraph>().Select(p => p.PlainText).ToArray();

    [Fact]
    public void RoundTrip_PreservesLinesAndCount()
    {
        var adapter = new PlainTextFileAdapter();
        var bytes = Save(DocOf("First line", "Second", "", "Last"), adapter);

        using var ms = new MemoryStream(bytes);
        LinesOf(adapter.Load(ms)).Should().Equal("First line", "Second", "", "Last");
    }

    [Fact]
    public void Save_DefaultsToUtf8_NoBom_Crlf()
    {
        var bytes = Save(DocOf("hi", "there"), new PlainTextFileAdapter());

        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        Encoding.UTF8.GetString(bytes).Should().Be("hi\r\nthere");
    }

    [Fact]
    public void Save_HonoursLfEol()
    {
        var adapter = new PlainTextFileAdapter(new TextSaveOptions(new UTF8Encoding(false), EolStyle.Lf));

        Encoding.UTF8.GetString(Save(DocOf("a", "b"), adapter)).Should().Be("a\nb");
    }

    [Fact]
    public void Save_EmitsBom_WhenRequested()
    {
        var adapter = new PlainTextFileAdapter(new TextSaveOptions(new UTF8Encoding(false), EolStyle.Crlf, EmitBom: true));

        Save(DocOf("x"), adapter).Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [Fact]
    public void Load_DetectsUtf16LeBom()
    {
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("héllo\r\nwörld"))
            .ToArray();

        using var ms = new MemoryStream(bytes);
        LinesOf(new PlainTextFileAdapter().Load(ms)).Should().Equal("héllo", "wörld");
    }

    [Fact]
    public void Load_EmptyInput_YieldsSingleEmptyParagraph()
    {
        using var ms = new MemoryStream(Array.Empty<byte>());
        var lines = LinesOf(new PlainTextFileAdapter().Load(ms));

        lines.Should().ContainSingle().Which.Should().BeEmpty();
    }

    [Fact]
    public void RoundTrip_PreservesNonAsciiUtf8()
    {
        var adapter = new PlainTextFileAdapter();
        var bytes = Save(DocOf("café ☕ — naïve"), adapter);

        using var ms = new MemoryStream(bytes);
        LinesOf(adapter.Load(ms)).Should().Equal("café ☕ — naïve");
    }

    [Fact]
    public void Load_BomlessInvalidUtf8_FallsBackToWindows1252()
    {
        byte[] bytes =
        [
            0x43, 0x61, 0x66, 0xE9, 0x20,
            0x80, 0x20,
            0x93, 0x71, 0x75, 0x6F, 0x74, 0x65, 0x94,
        ];

        using var stream = new MemoryStream(bytes);
        LinesOf(new PlainTextFileAdapter().Load(stream))
            .Should().Equal("Caf\u00e9 \u20ac \u201cquote\u201d");
    }

    [Fact]
    public void Load_BomlessValidUtf8_RemainsUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes("Caf\u00e9 \u20ac \u201cquote\u201d");

        using var stream = new MemoryStream(bytes);
        LinesOf(new PlainTextFileAdapter().Load(stream))
            .Should().Equal("Caf\u00e9 \u20ac \u201cquote\u201d");
    }

    [Fact]
    public void Load_LeavesCallerStreamOpen()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("text"));

        _ = new PlainTextFileAdapter().Load(stream);

        stream.CanRead.Should().BeTrue();
        stream.Position.Should().Be(stream.Length);
    }

    [Fact]
    public void Save_ProjectsTableCellsAsTabDelimitedRows()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Plain "),
                new Run("bold", RunFormatting.Default with { Bold = true }),
            },
        });
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("A1");
        table.Rows[0].Cells[1].Paragraphs[0] = new Paragraph("B1");
        table.Rows[1].Cells[0].Paragraphs[0] = new Paragraph("A2");
        table.Rows[1].Cells[1].Paragraphs[0] = new Paragraph("B2");
        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph("After table"));

        var adapter = new PlainTextFileAdapter();
        var bytes = Save(document, adapter);

        Encoding.UTF8.GetString(bytes).Should().Be("Plain bold\r\nA1\tB1\r\nA2\tB2\r\nAfter table");
        using var stream = new MemoryStream(bytes);
        LinesOf(adapter.Load(stream)).Should().Equal("Plain bold", "A1\tB1", "A2\tB2", "After table");
    }

    [Fact]
    public void Save_TableProjectionPreservesCellParagraphsAndEmptyCellsUsingConfiguredEol()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("First");
        table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("Second"));
        table.Rows[0].Cells[1].Paragraphs[0] = new Paragraph("Right");
        table.Rows[1].Cells[1].Paragraphs[0] = new Paragraph("Tail");
        document.Blocks.Add(table);

        var adapter = new PlainTextFileAdapter(
            new TextSaveOptions(new UTF8Encoding(false), EolStyle.Lf));

        Encoding.UTF8.GetString(Save(document, adapter))
            .Should().Be("First\nSecond\tRight\n\tTail");
    }
}
