using System.Text;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r452: the SYLK and DIF readers must not open an arbitrary text file as an empty workbook.
///
/// <para>Continuation of the r448-r451 sweep into the record-based text importers. Both are
/// record/section formats, and both skipped anything they did not recognise -- so a file that was not
/// a .slk or .dif at all "opened" as a blank sheet with no error. The user sees an empty grid, and a
/// save writes an empty file over whatever the original was.</para>
///
/// <para>Deliberately NOT a header check. The SYLK spec puts an <c>ID</c> record first and DIF starts
/// with <c>TABLE</c>, but real writers vary and rejecting a file that plainly IS the format would be
/// the worse error. Instead each reader now asks the narrow question: did ANYTHING in this file
/// parse as the format? A SYLK file with any recognised record opens, and a DIF file with a DATA
/// section opens even when that section holds no rows.</para>
///
/// <para>Aligned with what the sibling format in the same assembly already did: the SpreadsheetML
/// adapter rejects a wrong root outright, which is what made these two stand out as inconsistent.</para>
/// </summary>
public sealed class R452_TextFormatsRejectFilesThatAreNotTheFormatTests
{
    private static Workbook Sample()
    {
        var workbook = new Workbook("probe");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 3; row++)
        {
            for (uint col = 1; col <= 2; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue($"r{row}c{col}"));
        }

        return workbook;
    }

    private static byte[] SavedBy(Func<Workbook, MemoryStream, object?> save)
    {
        using var stream = new MemoryStream();
        save(Sample(), stream);
        return stream.ToArray();
    }

    [Fact]
    public void SlkRefusesAFileThatIsNotSylk()
    {
        var open = () =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("this is plainly not a SYLK file"));
            return new SlkFileAdapter().Load(stream);
        };

        open.Should().Throw<InvalidDataException>(
                "an empty grid is indistinguishable from a file that legitimately had no cells, so " +
                "the user cannot tell their file was never read")
            .WithMessage("*not a SYLK*");
    }

    [Fact]
    public void SlkStillOpensWhatItWrote()
    {
        var bytes = SavedBy((workbook, stream) => { new SlkFileAdapter().Save(workbook, stream); return null; });

        using var reading = new MemoryStream(bytes);
        var reloaded = new SlkFileAdapter().Load(reading);

        reloaded.Sheets.Sum(sheet => sheet.EnumerateCells().Count())
            .Should().Be(6, "the guard must not disturb a real SYLK file");
    }

    [Fact]
    public void SlkAcceptsAFileWhoseOnlyRecordsAreNonCellOnes()
    {
        // The narrowness case. A SYLK file carrying only header/terminator records is still SYLK and
        // must open as an empty workbook -- which is exactly the state the guard rejects for a
        // NON-SYLK file, so the two must be told apart by evidence of the format, not by emptiness.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ID;PWXL;N;E\r\nE\r\n"));

        var workbook = new SlkFileAdapter().Load(stream);

        workbook.Sheets.Should().NotBeEmpty("a header-only SYLK file is valid, merely empty");
    }

    [Fact]
    public void DifRefusesAFileThatIsNotDif()
    {
        var open = () =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("this is plainly not a DIF file"));
            return new DifFileAdapter().Load(stream);
        };

        open.Should().Throw<InvalidDataException>("the same reasoning as SYLK above")
            .WithMessage("*not a Data Interchange Format*");
    }

    [Fact]
    public void DifStillOpensWhatItWrote()
    {
        var bytes = SavedBy((workbook, stream) => { new DifFileAdapter().Save(workbook, stream); return null; });

        using var reading = new MemoryStream(bytes);
        var reloaded = new DifFileAdapter().Load(reading);

        reloaded.Sheets.Sum(sheet => sheet.EnumerateCells().Count())
            .Should().Be(6, "the guard must not disturb a real DIF file");
    }

    [Fact]
    public void DifAcceptsATableWithNoRows()
    {
        // Narrowness again: a DIF header followed by a DATA section holding nothing is a legitimate
        // empty table, and must open rather than be mistaken for a foreign file.
        var dif = "TABLE\r\n0,1\r\n\"\"\r\nVECTORS\r\n0,0\r\n\"\"\r\nTUPLES\r\n0,0\r\n\"\"\r\nDATA\r\n0,0\r\n\"\"\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(dif));

        var workbook = new DifFileAdapter().Load(stream);

        workbook.Sheets.Should().NotBeEmpty("an empty DIF table is valid, merely empty");
    }
}
