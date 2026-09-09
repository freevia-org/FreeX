using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r588: one malformed attribute must not cost the user the whole document -- asked of the app that
/// does NOT delegate its parsing.
///
/// <para>r583 and r584 found nineteen and then ninety-eight aborted LOADS in FreeX by mutating every
/// integer attribute of a saved workbook. Every throw came from inside DocumentFormat.OpenXml,
/// reached through ClosedXML. That raised a question the FreeX evidence alone could not answer: is
/// the class a property of the FORMAT, or of delegating to a strict third-party parser?</para>
///
/// <para>This is the same probe against a reader this repository writes itself, and the answer is
/// unambiguous -- the hand-rolled reader tolerates every mutation the library-backed one aborted on.
/// The class is library-induced, which is why FreeX carries two normalizers to compensate and this
/// app needs none.</para>
///
/// <para>Kept as a tripwire rather than deleted as a passing experiment: it is the fence that fires
/// the day this reader adopts a strict parser, or hand-rolls a strict parse of its own.</para>
/// </summary>
public class R588_MalformedAttributeDocxLoadTests
{
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

    private static readonly (string Name, string Value)[] Mutations =
    [
        ("oversized", "4294967296"),
        ("negative", "-1"),
        ("word", "yes"),
        ("empty", ""),
        ("float-in-int", "1.5"),
    ];

    [Fact]
    public void Probe_MalformedAttributesInDocx()
    {
        var source = RichDocumentBytes();
        var failures = new List<string>();
        var mutants = 0;

        List<(string Part, string Xml)> parts;
        using (var zip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read))
        {
            parts = zip.Entries
                .Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(e =>
                {
                    using var s = e.Open();
                    using var r = new StreamReader(s);
                    return (e.FullName, r.ReadToEnd());
                })
                .ToList();
        }

        foreach (var (mutationName, mutationValue) in Mutations)
        {
            var seen = new HashSet<string>();
            foreach (var (part, xml) in parts)
            {
                XDocument document;
                try { document = XDocument.Parse(xml); }
                catch (System.Xml.XmlException) { continue; }

                foreach (var element in document.Descendants().ToList())
                foreach (var attribute in element.Attributes().ToList())
                {
                    if (attribute.IsNamespaceDeclaration) continue;
                    if (attribute.Value.Length == 0 || !attribute.Value.All(char.IsAsciiDigit)) continue;

                    var key = $"{part}|{element.Name.LocalName}|{attribute.Name.LocalName}";
                    if (!seen.Add(key)) continue;

                    var original = attribute.Value;
                    attribute.Value = mutationValue;
                    var mutated = WithPartReplaced(source, part, document);
                    attribute.Value = original;
                    mutants++;

                    try
                    {
                        using var stream = new MemoryStream(mutated);
                        var reloaded = DocxReader.Read(stream);
                        if (reloaded.Blocks.Count == 0)
                            failures.Add($"EMPTY [{mutationName}] {key}");
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"THROW [{mutationName}] {key} -> {ex.GetType().Name}");
                    }
                }
            }
        }

        mutants.Should().BeGreaterThan(100,
            "the probe must actually be mutating the package");

        failures.Should().BeEmpty(
            "one malformed attribute must not cost the user the whole document");
    }
}
