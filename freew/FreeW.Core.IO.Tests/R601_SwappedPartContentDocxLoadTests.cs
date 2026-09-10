using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public class R601_SwappedPartContentDocxLoadTests
{
    /// <summary>
    /// The two parts whose removal loses a chart silently. Pinned exactly, so the gap cannot widen
    /// while the reporting question is open.
    /// </summary>
    private static readonly HashSet<string> KnownSilentChartLoss =
    [
        "word/_rels/document.xml.rels",
        "word/charts/chart1.xml",
    ];
    private static byte[] RichDocumentBytes()
    {
        var document = new TextDocument();

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Hello", new RunFormatting { Bold = true }));
        paragraph.Runs.Add(new Run(" world"));
        document.Blocks.Add(paragraph);

        var table = new Table();
        for (var r = 0; r < 2; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < 2; c++)
                row.Cells.Add(new TableCell($"r{r}c{c}"));

            table.Rows.Add(row);
        }

        document.Blocks.Add(table);

        var chartParagraph = new Paragraph();
        chartParagraph.Runs.Add(Run.FromChart(
            Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0], seriesName: "S")));
        document.Blocks.Add(chartParagraph);

        using var ms = new MemoryStream();
        DocxWriter.Write(document, ms);
        return ms.ToArray();
    }

    private static byte[] WithPartReplaced(byte[] source, string part, XDocument replacement)
    {
        using var input = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                var target = zip.CreateEntry(entry.FullName);
                using var targetStream = target.Open();
                if (entry.FullName == part)
                {
                    replacement.Save(targetStream);
                }
                else
                {
                    using var sourceStream = entry.Open();
                    sourceStream.CopyTo(targetStream);
                }
            }
        }

        return output.ToArray();
    }

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
                if (entry.FullName == part) { ts.Write(donor, 0, donor.Length); continue; }
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
                using var ts = target.Open();
                using var ss = entry.Open();
                using var buf = new MemoryStream();
                ss.CopyTo(buf);
                var bytes = buf.ToArray();
                var keep = entry.FullName == part ? (int)(bytes.Length * fraction) : bytes.Length;
                ts.Write(bytes, 0, keep);
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
                if (entry.FullName == part) continue;
                var target = zip.CreateEntry(entry.FullName);
                using var targetStream = target.Open();
                using var sourceStream = entry.Open();
                sourceStream.CopyTo(targetStream);
            }
        }

        return output.ToArray();
    }

    private static string Describe(TextDocument document)
    {
        var runs = 0;
        var tables = 0;
        var charts = 0;
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph p)
            {
                runs += p.Runs.Count;
                charts += p.Runs.Count(r => r.Chart is not null);
            }
            if (block is Table) tables++;
        }

        return $"blocks={document.Blocks.Count} runs={runs} charts={charts} tables={tables}";
    }

    /// <summary>
    /// r598: carrying r596's truncation lens to FreeW. Truncating a part leaves a valid ZIP holding
    /// malformed XML -- the interrupted-download shape -- which slipped past OpenZipArchive's checks
    /// and threw a raw XmlException, even though this reader already owns the sentence for exactly
    /// this damage ("it may be corrupted or truncated"). Same damage, two different errors depending
    /// on which layer noticed, one of them framework text the user would read verbatim.
    /// </summary>
    /// <summary>
    /// r601: the swapped-content shape on FreeW -- clean, and recorded as a fence rather than deleted.
    /// Every part given another part's XML either loads or refuses with FreeW's own
    /// InvalidDataException; nothing leaks a framework sentence. FreeX needed a fix for this shape
    /// (r600) and FreeP needed one (r601); carrying the lens rather than either result is what
    /// established that, since r588 and r594 between them showed that an app's outcome on one shape
    /// predicts nothing about the next.
    /// </summary>
    [Fact]
    public void SwappingAnyPartsContentRefusesWithFreeWsOwnError()
    {
        var source = RichDocumentBytes();

        List<string> parts;
        using (var zip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read))
            parts = zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();

        parts.Should().HaveCountGreaterThan(3, "the fence is worthless if it stopped finding parts");

        var leaked = new List<string>();
        foreach (var part in parts)
        foreach (var donor in parts)
        {
            if (part == donor) continue;
            try
            {
                using var ms = new MemoryStream(SwappedContent(source, part, donor));
                DocxReader.Read(ms);
            }
            catch (InvalidDataException) { }
            catch (Exception ex)
            {
                leaked.Add($"{part} <- {donor} -> {ex.GetType().Name}");
            }
        }

        leaked.Should().BeEmpty(
            "a wrong-schema part must surface FreeW's own InvalidDataException, not a framework " +
            "sentence the user would read verbatim");
    }
}
