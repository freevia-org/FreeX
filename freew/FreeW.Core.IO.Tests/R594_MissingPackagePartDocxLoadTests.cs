using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r594: carrying r592's part-deletion lens to FreeW, and fencing what it found.
///
/// <para>r588 established that the ABORTED-LOAD class was library-induced and FreeW was immune to it.
/// It did not follow that FreeW is immune to everything FreeX had, and it is not: removing
/// <c>word/_rels/document.xml.rels</c> drops the chart run entirely, and removing
/// <c>word/charts/chart1.xml</c> keeps the run but loses its chart. Both are silent -- the same
/// "declared chart the package cannot resolve" gap r593 fixed in FreeX, in an app that reached it by
/// completely different code.</para>
///
/// <para>Recorded and FENCED rather than fixed, because the fix is a product decision rather than a
/// review one. FreeX already had a warnings channel for r593 to thread; FreeW's reader has none at
/// all -- <c>DocxReader.Read</c> returns a bare <c>TextDocument</c> -- so reporting this means a new
/// public <c>ReadWithWarnings</c> API and a shell surface to show it in. FreeW has a save-time
/// compatibility warning pane and nothing for load. Inventing that surface is not a review fix, and
/// the severity does not force it: as in r592, the chart is ALREADY gone from the file, so this is a
/// failure to REPORT damage rather than corruption.</para>
///
/// <para>What this test does is stop the silence spreading. The two chart paths are named exactly;
/// any OTHER part whose removal changes what loads fails here.</para>
/// </summary>
public class R594_MissingPackagePartDocxLoadTests
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

    [Fact]
    public void RemovingAnyPartDoesNotSilentlyLoseContent()
    {
        var source = RichDocumentBytes();
        string baseline;
        using (var ms = new MemoryStream(source))
            baseline = Describe(DocxReader.Read(ms));

        List<string> parts;
        using (var zip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read))
            parts = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();

        var unexpectedLoss = new List<string>();

        foreach (var part in parts)
        {
            var mutated = WithoutPart(source, part);
            try
            {
                using var ms = new MemoryStream(mutated);
                var result = Describe(DocxReader.Read(ms));
                if (result != baseline && !KnownSilentChartLoss.Contains(part))
                    unexpectedLoss.Add($"{part} -> {result} (baseline {baseline})");
            }
            catch (Exception)
            {
                // Refusing a damaged package is a legitimate outcome and not what this fences.
            }
        }

        parts.Should().HaveCountGreaterThan(5,
            "the fence is worthless if the probe stopped finding parts to remove");

        unexpectedLoss.Should().BeEmpty(
            "content may not vanish from a loaded document without the load saying so; the two chart " +
            "paths below are the known, recorded exceptions and nothing else may join them");
    }
}
