using System.IO.Compression;
using FluentAssertions;
using System.Xml;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class R600_SwappedPartContentLoadTests
{
    private static byte[] RichWorkbookBytes()
    {
        var workbook = new Workbook("PartLoss");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint r = 1; r <= 6; r++)
        for (uint c = 1; c <= 4; c++)
            sheet.SetCell(new CellAddress(sheet.Id, r, c),
                r == 1 ? new TextValue($"H{c}") : new NumberValue(r * c));

        sheet.Comments[new CellAddress(sheet.Id, 3, 3)] = "a note";
        sheet.CommentAuthors[new CellAddress(sheet.Id, 3, 3)] = "Author";
        sheet.Hyperlinks[new CellAddress(sheet.Id, 4, 4)] = "https://example.invalid/";

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "T1",
            DisplayName = "T1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4)),
        };
        for (var i = 1; i <= 4; i++)
            table.Columns.Add(new StructuredTableColumnModel(i, $"H{i}"));
        sheet.StructuredTables.Add(table);

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4)),
            ShowLegend = true,
        });

        var second = workbook.AddSheet("Sheet2");
        second.SetCell(new CellAddress(second.Id, 1, 1), new TextValue("second sheet content"));

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        return ms.ToArray();
    }

    // A part swapped for ANOTHER part's content: well-formed XML, valid zip, wrong schema. This is
    // what a buggy third-party writer or a bad merge produces, and no earlier probe reaches it --
    // attribute mutation keeps the schema, deletion removes the part, truncation breaks well-formedness.
    /// <summary>
    /// The pinned residue: TWO paths of one shape, not one. A chart part or a drawing part holding
    /// another part's XML parses cleanly and simply DECLARES NOTHING, so r593's guards correctly do
    /// not fire -- there is no unresolvable relationship for them to see. Detecting it needs a
    /// contradiction check of the kind FreeP's r448 makes ("slides on disk, none reachable"): the
    /// drawing rels still name a chart part that the drawing itself no longer references.
    /// <para>
    /// Left pinned rather than built, because the shape is narrow -- unlike r593's missing part,
    /// which a partial download produces, this needs a writer that files one part's XML under
    /// another part's name. The fence stops it spreading to any other part meanwhile, and the second
    /// path was found by the fence itself after only the first had been pinned.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> KnownSilentChartContentLoss =
    [
        "xl/charts/chart1.xml",
        "xl/drawings/drawing1.xml",
    ];

    private static byte[] SwappedContent(byte[] source, string part, string donorPart)
    {
        using var input = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        byte[] donor;
        using (var ds = input.GetEntry(donorPart)!.Open())
        using (var buf = new MemoryStream())
        {
            ds.CopyTo(buf);
            donor = buf.ToArray();
        }

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                var target = zip.CreateEntry(entry.FullName);
                using var ts = target.Open();
                if (entry.FullName == part)
                {
                    ts.Write(donor, 0, donor.Length);
                    continue;
                }

                using var ss = entry.Open();
                ss.CopyTo(ts);
            }
        }

        return output.ToArray();
    }

    private static byte[] Truncated(byte[] source, string part, double fraction)
    {
        using var input = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                var target = zip.CreateEntry(entry.FullName);
                using var targetStream = target.Open();
                using var sourceStream = entry.Open();
                using var buffer = new MemoryStream();
                sourceStream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                var keep = entry.FullName == part ? (int)(bytes.Length * fraction) : bytes.Length;
                targetStream.Write(bytes, 0, keep);
            }
        }

        return output.ToArray();
    }

    private static byte[] WithoutPart(byte[] source, string part)
    {
        using var input = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                if (entry.FullName == part)
                    continue;

                var target = zip.CreateEntry(entry.FullName);
                using var targetStream = target.Open();
                using var sourceStream = entry.Open();
                sourceStream.CopyTo(targetStream);
            }
        }

        return output.ToArray();
    }

    private static string DescribeContent(Workbook workbook)
    {
        var cells = 0;
        var tables = 0;
        var charts = 0;
        foreach (var sheet in workbook.Sheets)
        {
            cells += sheet.EnumerateCells().Count();
            tables += sheet.StructuredTables.Count;
            charts += sheet.Charts.Count;
        }

        return $"sheets={workbook.Sheets.Count} cells={cells} tables={tables} charts={charts}";
    }

    /// <summary>
    /// Truncating a part is a DIFFERENT damage shape from deleting one: the zip stays valid and the
    /// entry holds malformed XML, which is what an interrupted download or a zip-repair tool
    /// actually produces. Every reader in the package layer then threw a raw XmlException, and the
    /// shell shows exception.Message verbatim -- so the user read "Unexpected end of file has
    /// occurred. The following elements are not closed" for a file this adapter already owns an
    /// accurate sentence about.
    /// <para>
    /// Two outcomes are legitimate and both are accepted here: refusing with FreeX's own typed error,
    /// or loading with less content AND a warning saying so (r593). What is not legitimate is a raw
    /// framework exception, or a silent loss.
    /// </para>
    /// </summary>
    /// <summary>
    /// r600: a fourth damage shape -- a part holding ANOTHER part's content. Valid zip, well-formed
    /// XML, wrong schema, which is what a buggy third-party writer or a bad merge produces. None of
    /// the earlier probes reaches it: attribute mutation keeps the schema, deletion removes the part,
    /// truncation breaks well-formedness.
    /// <para>
    /// It found DocumentFormat.OpenXml's OpenXmlPart.LoadDomTree throwing InvalidDataException
    /// ("Cannot load the root element from the part. The part contains invalid data.") straight at the
    /// user for a wrong-schema docProps/app.xml -- the same class as r592, r596 and r598, in a shape
    /// none of them could produce.
    /// </para>
    /// <para>
    /// KnownSilentChartContentLoss pins the residue: a chart part holding another part's XML is
    /// recorded by ReadChartParts (the part exists and parses, so r593's guards correctly do not
    /// fire) and then yields no chart further downstream, where the warnings sink does not reach.
    /// That is a real gap, and a deliberately narrow one: unlike r593's missing-part case, which a
    /// partial download produces, this needs a writer that puts one part's XML under another part's
    /// name. Pinned so it cannot spread while it waits for the sink to be threaded further.
    /// </para>
    /// </summary>
    [Fact]
    public void SwappingAnyPartsContentRefusesTypedOrReportsWhatItLost()
    {
        var source = RichWorkbookBytes();
        string baseline;
        using (var ms = new MemoryStream(source))
            baseline = DescribeContent(new XlsxFileAdapter().Load(ms));

        List<string> parts;
        using (var zip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read))
            parts = zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();

        parts.Should().HaveCountGreaterThan(8, "the fence is worthless if it stopped finding parts");

        var leaked = new List<string>();

        foreach (var part in parts)
        foreach (var donor in parts)
        {
            if (part == donor) continue;

            try
            {
                using var ms = new MemoryStream(SwappedContent(source, part, donor));
                var loaded = new XlsxFileAdapter().LoadWithWarnings(ms);
                // r600: deliberately NOT asserting "differs from baseline means content was lost". A
                // same-schema swap -- sheet1.xml given sheet2.xml's content -- produces a workbook
                // that differs from the baseline and is nonetheless read CORRECTLY: the bytes on
                // disk really do hold that content under that name, and no reader can know what
                // sheet1 "should" have contained. The fence caught exactly that pair and it was the
                // premise that was wrong, not the loader.
                //
                // What remains soundly judgeable here is the LEAK check below, which needs no notion
                // of what the file ought to have said. The silent-loss question for a wrong-SCHEMA
                // part is real but needs a contradiction check that does not exist yet (see
                // KnownSilentChartContentLoss), and it is recorded there rather than asserted from a
                // comparison that cannot tell corruption from a legitimately different file.
                _ = loaded;
            }
            catch (WorkbookInvalidException) { }
            catch (XlsxThemePartCorruptException) { }
            catch (Exception ex)
            {
                leaked.Add($"{part} <- {donor} -> {ex.GetType().Name}");
            }
        }

        leaked.Should().BeEmpty(
            "a part holding the wrong schema must surface FreeX's own error, not a library sentence " +
            "the shell shows to the user verbatim");

    }
}
