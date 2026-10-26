using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r450: a worksheet that cannot be read must be reported, not opened empty in silence.
///
/// <para>Third and last application of the malformed-input probe, after r448 (FreeP) and r449
/// (FreeW). FreeX's reader is the most robust of the three -- eight of nine mutations threw, including
/// the top-level <c>workbook.xml</c> case its siblings both failed -- but a worksheet part whose root
/// element is not <c>worksheet</c> is loaded by ClosedXML as an EMPTY sheet, with every other sheet
/// intact and nothing said. A 13-cell workbook came back with 1.</para>
///
/// <para>Unlike its siblings this is REPORTED rather than refused. One damaged sheet must not cost
/// the user the others, and this adapter -- unlike FreeP's reader -- already owns a warning channel
/// to say so with, so the fix is to use it rather than to invent a new failure mode.</para>
/// </summary>
public sealed class R450_DamagedWorksheetIsReportedNotSilentTests
{
    private static byte[] WorkbookBytes()
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
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    private static byte[] WithUnreadableFirstSheet(byte[] original)
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

                var isFirstSheet =
                    entry.FullName.Contains("xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase) &&
                    !entry.FullName.Contains("_rels", StringComparison.OrdinalIgnoreCase);

                var created = writer.CreateEntry(entry.FullName);
                using var createdStream = created.Open();
                var bytes = isFirstSheet ? Encoding.UTF8.GetBytes("<unrecognised/>") : buffer.ToArray();
                createdStream.Write(bytes, 0, bytes.Length);
            }
        }

        return output.ToArray();
    }

    private static XlsxLoadResult Load(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new XlsxFileAdapter().LoadWithWarnings(stream);
    }

    [Fact]
    public void ADamagedWorksheetProducesAWarning()
    {
        var result = Load(WithUnreadableFirstSheet(WorkbookBytes()));

        result.Warnings.Should().ContainSingle(
                "opening a sheet empty without saying so lets the user save the loss over their file")
            .Which.Should().Contain("damaged").And.Contain("sheet1.xml");
    }

    [Fact]
    public void TheDamageIsRealAndCostsThatSheetsCells()
    {
        // The premise, asserted rather than assumed: if the mutation had not actually emptied the
        // sheet, warning about it would be noise. Sheet2 is untouched, which is why the fix warns
        // instead of refusing the whole file.
        var result = Load(WithUnreadableFirstSheet(WorkbookBytes()));

        result.Workbook.Sheets.Should().HaveCount(2, "the workbook still opens with both sheets");
        result.Workbook.Sheets.Sum(sheet => sheet.EnumerateCells().Count())
            .Should().Be(1, "only Sheet2's single cell survives, which is exactly what the user must be told");
    }

    [Fact]
    public void AHealthyWorkbookWarnsAboutNothing()
    {
        // A warning that fires on undamaged files is worse than none: it trains the user to dismiss
        // the dialog that matters.
        var result = Load(WorkbookBytes());

        result.Warnings.Should().BeEmpty("nothing is damaged here");
        result.Workbook.Sheets.Sum(sheet => sheet.EnumerateCells().Count())
            .Should().Be(13, "and the ordinary load is untouched");
    }
}
