using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Corpus-gated fidelity runner: opens every real-world DOCX in the (download-on-demand)
/// <c>freew-fidelity-corpus/files/</c> folder, round-trips it through <see cref="DocxWriter"/> +
/// <see cref="DocxReader"/>, and asserts no exception and no loss of <em>modelled</em> content.
///
/// Repository-local fixtures are committed in nested feature folders. Additional third-party binaries are
/// fetched on demand via <c>tools/Fetch-FreeWFidelityCorpus.ps1</c> into the same corpus tree.
/// </summary>
public class FreeWFidelityCorpusRoundTripTests
{
    [Fact]
    public void Corpus_Files_Open_And_RoundTrip_PreservingModelledContent()
    {
        var files = CorpusFiles();

        files.Should().Contain(
            file => file.EndsWith(Path.Combine("review", "citation-bibliography.docx"), StringComparison.OrdinalIgnoreCase),
            "the committed nested review corpus must be exercised");
        files.Should().Contain(
            file => file.EndsWith(Path.Combine("tables", "01-banded-rows-header.docx"), StringComparison.OrdinalIgnoreCase),
            "the committed nested table corpus must be exercised");

        var tmpDir = Path.Combine(Path.GetTempPath(), "freew-fidelity-corpus-roundtrip");
        Directory.CreateDirectory(tmpDir);

        var failures = new List<string>();

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);

            TextDocument original;
            try
            {
                original = DocxReader.Read(file);
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: OPEN threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            TextDocument reopened;
            try
            {
                var outPath = Path.Combine(tmpDir, name);
                DocxWriter.Write(original, outPath);
                reopened = DocxReader.Read(outPath);
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: ROUND-TRIP threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            var before = ContentStats.Of(original);
            var after = ContentStats.Of(reopened);
            var drift = before.Describe(after);
            if (drift is not null)
                failures.Add($"{name}: modelled-content drift after round-trip — {drift}");
        }

        failures.Should().BeEmpty(
            "every corpus document should open and round-trip without losing modelled content:\n  "
            + string.Join("\n  ", failures));
    }

    private static IReadOnlyList<string> CorpusFiles()
    {
        var corpusDirectory = TestWorkspaceFileLocator.TryFindDirectoryFromBaseDirectory(
            "freew-fidelity-corpus", "files");

        return corpusDirectory is null
            ? []
            : Directory.GetFiles(corpusDirectory, "*.docx", SearchOption.AllDirectories)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    /// <summary>Coarse counts of the content FreeW actually models, used to detect round-trip drift.</summary>
    private readonly record struct ContentStats(
        int Blocks, int Tables, int Paragraphs, int Runs, int Chars,
        int Images, int Footnotes, int Endnotes, int Comments)
    {
        public static ContentStats Of(TextDocument doc)
        {
            int tables = 0, paragraphs = 0, runs = 0, chars = 0, images = 0;

            foreach (var paragraph in EnumerateParagraphs(doc.Blocks, ref tables))
            {
                paragraphs++;
                foreach (var run in paragraph.Runs)
                {
                    runs++;
                    chars += run.Text?.Length ?? 0;
                    if (run.Image is not null)
                        images++;
                }
            }

            return new ContentStats(
                doc.Blocks.Count, tables, paragraphs, runs, chars,
                images, doc.Footnotes.Count, doc.Endnotes.Count, doc.Comments.Count);
        }

        private static IEnumerable<Paragraph> EnumerateParagraphs(IEnumerable<Block> blocks, ref int tables)
        {
            // ref locals can't cross an iterator boundary, so flatten eagerly into a list.
            var result = new List<Paragraph>();
            foreach (var block in blocks)
            {
                if (block is Paragraph p)
                {
                    result.Add(p);
                }
                else if (block is Table t)
                {
                    tables++;
                    foreach (var row in t.Rows)
                        foreach (var cell in row.Cells)
                            result.AddRange(cell.Paragraphs);
                }
            }

            return result;
        }

        public string? Describe(ContentStats a)
        {
            var diffs = new List<string>();
            void Compare(string label, int x, int y)
            {
                if (x != y)
                    diffs.Add($"{label} {x}->{y}");
            }

            Compare("blocks", Blocks, a.Blocks);
            Compare("tables", Tables, a.Tables);
            Compare("paragraphs", Paragraphs, a.Paragraphs);
            Compare("runs", Runs, a.Runs);
            Compare("chars", Chars, a.Chars);
            Compare("images", Images, a.Images);
            Compare("footnotes", Footnotes, a.Footnotes);
            Compare("endnotes", Endnotes, a.Endnotes);
            Compare("comments", Comments, a.Comments);

            return diffs.Count == 0 ? null : string.Join(", ", diffs);
        }
    }
}
