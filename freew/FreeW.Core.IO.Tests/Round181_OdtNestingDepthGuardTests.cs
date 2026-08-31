using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round 181. ReadBlocks -> ReadTable -> ReadBlocks (and ReadList -> ReadList) recursed with no
/// depth bound, so a small hand-written .odt that nests a table inside a cell a few thousand times
/// overflowed the stack while opening. A StackOverflowException cannot be caught in .NET: the
/// process dies outright, taking every other open document with it. Neither existing guard sees the
/// file -- the package is a few kilobytes, so the zip-bomb ratio check passes, and the character cap
/// in SecureXmlReaderSettings bounds total characters, not nesting depth.
///
/// These build genuinely deep packages and require the open to RETURN. A crash here takes the test
/// host with it, which is itself the signal.
/// </summary>
public sealed class Round181_OdtNestingDepthGuardTests
{
    [Fact]
    public void Load_DeeplyNestedTables_ReturnsInsteadOfOverflowingTheStack()
    {
        using var package = CreateOdt(NestedTablesContent(5000));

        var document = OdtFileAdapter.Odt().Load(package);

        document.Should().NotBeNull("the open must complete rather than kill the process");
    }

    [Fact]
    public void Load_DeeplyNestedLists_ReturnsInsteadOfOverflowingTheStack()
    {
        using var package = CreateOdt(NestedListsContent(5000));

        var document = OdtFileAdapter.Odt().Load(package);

        document.Should().NotBeNull();
    }

    [Fact]
    public void Load_ModestlyNestedTable_StillReadsItsContent()
    {
        // Sibling no-regression: the cap must be far above anything a real document uses, so a
        // genuinely nested table still round-trips its text.
        using var package = CreateOdt(NestedTablesContent(3, "deep text"));

        var document = OdtFileAdapter.Odt().Load(package);

        document.Blocks.Should().NotBeEmpty("a three-deep nested table is ordinary content");
    }

    private static string NestedTablesContent(int depth, string? innerText = null)
    {
        var open = new StringBuilder();
        var close = new StringBuilder();
        for (var i = 0; i < depth; i++)
        {
            open.Append("<table:table><table:table-row><table:table-cell>");
            close.Insert(0, "</table:table-cell></table:table-row></table:table>");
        }

        var inner = innerText is null ? string.Empty : $"<text:p>{innerText}</text:p>";
        return open + inner + close.ToString();
    }

    private static string NestedListsContent(int depth)
    {
        var open = new StringBuilder();
        var close = new StringBuilder();
        for (var i = 0; i < depth; i++)
        {
            open.Append("<text:list><text:list-item>");
            close.Insert(0, "</text:list-item></text:list>");
        }

        return open + "<text:p>leaf</text:p>" + close;
    }

    private static MemoryStream CreateOdt(string bodyContent)
    {
        var contentXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<office:document-content " +
            "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
            "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
            "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\">" +
            "<office:body><office:text>" + bodyContent + "</office:text></office:body>" +
            "</office:document-content>";

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "mimetype", "application/vnd.oasis.opendocument.text");
            Write(archive, "content.xml", contentXml);
        }

        stream.Position = 0;
        return stream;
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var entry = archive.CreateEntry(name).Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        entry.Write(bytes, 0, bytes.Length);
    }
}
