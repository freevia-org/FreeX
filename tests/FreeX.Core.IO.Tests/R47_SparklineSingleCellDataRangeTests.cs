using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R47-io-sparkline-groups-3-1: a sparkline whose data-range <c>&lt;xm:f&gt;</c> formula collapses to a
/// bare single-cell reference (no colon, e.g. "Sheet1!A1") is something real Excel legitimately writes
/// whenever a sparkline's data source is exactly one cell (picking a single cell in the Sparkline "Edit
/// Data" dialog, or later deleting columns until only one remains and Excel auto-shrinks the reference).
/// XlsxSparklineMapper.Read previously parsed this bare range with the strict <c>GridRange.Parse</c>,
/// which throws FormatException for anything without a colon; that exception was swallowed by the
/// surrounding try/catch ("Skip malformed sparkline references"), silently discarding the entire
/// sparkline (type, colors, markers, axis settings, location) with no warning. The fix switches to the
/// tolerant <c>GridRange.ParseCellOrRange</c> (already used for <c>ReadDateAxisRange</c>'s identical
/// xm:f formula shape), which accepts a colon-less single-cell reference as a degenerate 1x1 range.
/// </summary>
public sealed class R47_SparklineSingleCellDataRangeTests
{
    private static MemoryStream SaveXlsxWithLineSparkline(uint dataFromCol, uint dataToCol)
    {
        var workbook = new Workbook("SparklineSingleCell");
        var sheet = workbook.AddSheet("Sheet1");

        for (uint col = 1; col <= 5; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));

        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, dataFromCol), new CellAddress(sheet.Id, 1, dataToCol)),
            Location = new CellAddress(sheet.Id, 1, 6),
            Kind = SparklineKind.Line,
        });

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Rewrites the saved worksheet's sparkline data-range &lt;xm:f&gt; formula text in place, simulating
    /// a real Excel-authored file whose sparkline data range collapsed to a single cell. FreeX's own
    /// Save() always emits the colon form (even for a 1x1 GridRange, since GridRange.ToString() is
    /// "A1:A1"), so this is the only way to reproduce the bare single-cell shape a genuine third-party
    /// file can legitimately contain.
    /// </summary>
    private static MemoryStream RewriteSparklineDataFormula(MemoryStream saved, string oldFormula, string newFormula)
    {
        saved.Position = 0;
        var rewritten = new MemoryStream();
        using (var source = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        using (var destination = new ZipArchive(rewritten, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var destEntry = destination.CreateEntry(entry.FullName);
                using var entryStream = entry.Open();
                using var destStream = destEntry.Open();
                if (entry.FullName == "xl/worksheets/sheet1.xml")
                {
                    var text = new StreamReader(entryStream).ReadToEnd();
                    text.Should().Contain(oldFormula, "the fixture must actually contain the formula we intend to rewrite");
                    text = text.Replace(oldFormula, newFormula);
                    using var writer = new StreamWriter(destStream);
                    writer.Write(text);
                }
                else
                {
                    entryStream.CopyTo(destStream);
                }
            }
        }

        rewritten.Position = 0;
        return rewritten;
    }

    [Fact]
    public void Load_SparklineWithBareSingleCellDataRangeFormula_IsNotDropped()
    {
        using var saved = SaveXlsxWithLineSparkline(dataFromCol: 1, dataToCol: 1);

        // The fixture is built with a normal (colon-form) single-cell range so Save() round-trips it
        // uneventfully; we then rewrite the persisted formula to the bare, colon-less shape a real
        // third-party .xlsx can legitimately contain, and confirm THAT shape survives loading.
        using var rewritten = RewriteSparklineDataFormula(saved, "Sheet1!A1:A1", "Sheet1!A1");

        var reloaded = new XlsxFileAdapter().Load(rewritten);
        var sheet = reloaded.GetSheetAt(0);

        sheet.Sparklines.Should().ContainSingle(
            "a sparkline whose data range collapses to a bare single-cell reference must not be silently dropped");

        var sparkline = sheet.Sparklines[0];
        sparkline.Kind.Should().Be(SparklineKind.Line);
        sparkline.DataRange.Start.Row.Should().Be(1u);
        sparkline.DataRange.Start.Col.Should().Be(1u);
        sparkline.DataRange.End.Row.Should().Be(1u);
        sparkline.DataRange.End.Col.Should().Be(1u);
    }

    [Fact]
    public void Load_SparklineWithOrdinaryMultiCellDataRange_StillLoadsCorrectly()
    {
        // Sibling no-regression case: the ordinary, colon-form multi-cell data range (the common case)
        // must keep loading exactly as before.
        using var saved = SaveXlsxWithLineSparkline(dataFromCol: 1, dataToCol: 5);

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var sheet = reloaded.GetSheetAt(0);

        sheet.Sparklines.Should().ContainSingle();
        var sparkline = sheet.Sparklines[0];
        sparkline.Kind.Should().Be(SparklineKind.Line);
        sparkline.DataRange.Start.Row.Should().Be(1u);
        sparkline.DataRange.Start.Col.Should().Be(1u);
        sparkline.DataRange.End.Row.Should().Be(1u);
        sparkline.DataRange.End.Col.Should().Be(5u);
    }
}
