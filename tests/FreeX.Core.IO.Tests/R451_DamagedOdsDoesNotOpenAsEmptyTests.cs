using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r451: a damaged .ods must not open as an empty workbook.
///
/// <para>Fourth instance of the class found by the mutation probe (r448 FreeP, r449 FreeW, r450
/// FreeX/xlsx), and the first found in a SECONDARY format -- the earlier sweep covered only each
/// app's primary reader, leaving ODS and ODT unprobed even though both are user-facing open paths.</para>
///
/// <para>A <c>content.xml</c> whose root the reader does not recognise produced a workbook with one
/// default sheet and no cells, silently: 13 cells became 0. Saving over the original then wrote that
/// loss to disk.</para>
///
/// <para>FreeW's ODT adapter already guarded its equivalent (<c>office:body/office:text</c> missing
/// throws), so this aligns the sibling rather than inventing a contract -- the same "one path fixed,
/// siblings left" pattern as r438 and r441.</para>
/// </summary>
public sealed class R451_DamagedOdsDoesNotOpenAsEmptyTests
{
    private static byte[] OdsBytes()
    {
        var workbook = new Workbook("probe");
        var first = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 4; row++)
        {
            for (uint col = 1; col <= 3; col++)
                first.SetCell(new CellAddress(first.Id, row, col), new TextValue($"r{row}c{col}"));
        }

        var second = workbook.AddSheet("Sheet2");
        second.SetCell(new CellAddress(second.Id, 1, 1), new NumberValue(42));

        using var stream = new MemoryStream();
        new OdsFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    private static byte[] Rewrite(byte[] original, Func<string, byte[], byte[]?> mutate)
    {
        using var source = new MemoryStream(original);
        using var reader = new ZipArchive(source, ZipArchiveMode.Read);
        var output = new MemoryStream();

        using (var writer = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in reader.Entries)
            {
                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                entryStream.CopyTo(buffer);

                var replacement = mutate(entry.FullName, buffer.ToArray());
                if (replacement is null)
                    continue;

                var created = writer.CreateEntry(entry.FullName);
                using var createdStream = created.Open();
                createdStream.Write(replacement, 0, replacement.Length);
            }
        }

        return output.ToArray();
    }

    private static Workbook Load(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new OdsFileAdapter().Load(stream);
    }

    [Fact]
    public void AContentPartWithNoBodyIsReportedAsDamaged()
    {
        var damaged = Rewrite(OdsBytes(), (name, data) =>
            name.Equals("content.xml", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetBytes("<unrecognised/>")
                : data);

        var open = () => Load(damaged);

        open.Should().Throw<InvalidDataException>(
                "opening this as an empty workbook and letting the user save over it destroys the " +
                "13 cells the file was written with")
            .WithMessage("*damaged*");
    }

    [Fact]
    public void AnUndamagedOdsIsUnaffected()
    {
        var workbook = Load(OdsBytes());

        workbook.Sheets.Should().HaveCount(2);
        workbook.Sheets.Sum(sheet => sheet.EnumerateCells().Count())
            .Should().Be(13, "the ordinary path must not change");
    }

    [Fact]
    public void AWorkbookWithAnEmptySheetStillOpens()
    {
        // The guard must be narrow: it asks only whether office:body exists, never whether the body
        // holds any tables. A legitimately empty sheet has a body and must still open, or the guard
        // would reject files this very adapter writes.
        var workbook = new Workbook("empty");
        workbook.AddSheet("Sheet1");

        using var saved = new MemoryStream();
        new OdsFileAdapter().Save(workbook, saved);

        var reloaded = Load(saved.ToArray());

        reloaded.Sheets.Should().NotBeEmpty("an empty sheet is legitimate, not damage");
    }

    [Fact]
    public void AMalformedStylesPartIsStillTolerated()
    {
        // The adapter deliberately tolerates a broken styles.xml because content.xml is
        // authoritative. That tolerance is correct and this guard must not narrow it.
        var damaged = Rewrite(OdsBytes(), (name, data) =>
            name.Equals("styles.xml", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetBytes("not xml at all")
                : data);

        var workbook = Load(damaged);

        workbook.Sheets.Sum(sheet => sheet.EnumerateCells().Count())
            .Should().Be(13, "styles are decoration; the cells are the document");
    }
}
