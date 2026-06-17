using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Smoke;

/// <summary>
/// Headless engine smoke (no display) for the Linux packaging lane: builds a document, edits it,
/// round-trips it through the DOCX writer/reader, and verifies the model survives. Mirrors the
/// FreeX <c>--packaging-smoke</c> contract so the same CI step shape validates both apps.
/// </summary>
internal static class PackagingSmoke
{
    public const string Argument = "--packaging-smoke";

    public static bool TryRun(IReadOnlyList<string> args, TextWriter output, TextWriter error, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!args.Any(a => string.Equals(a, Argument, StringComparison.OrdinalIgnoreCase)))
        {
            exitCode = 0;
            return false;
        }

        try
        {
            var doc = SampleDocument.Create();

            // Exercise the command bus on a fresh paragraph.
            var bus = new DocumentCommandBus(new SmokeContext(doc));
            var added = new Paragraph();
            added.Runs.Add(new Run("Round-trip marker.", RunFormatting.Default with { Bold = true, FontSizePt = 12 }));
            bus.Execute(new InsertParagraphCommand(doc.Blocks.Count, added));
            var blocksAfterEdit = doc.Blocks.Count;
            var undoOk = bus.Undo() && doc.Blocks.Count == blocksAfterEdit - 1;
            bus.Redo();

            // DOCX round-trip in memory.
            using var stream = new MemoryStream();
            DocxWriter.Write(doc, stream);
            var writtenBytes = stream.Length;
            stream.Position = 0;
            var reopened = DocxReader.Read(stream);

            var textPreserved = reopened.PlainText.Contains("Welcome to FreeW", StringComparison.Ordinal)
                && reopened.PlainText.Contains("Round-trip marker.", StringComparison.Ordinal);
            var blocksPreserved = reopened.Blocks.Count == doc.Blocks.Count;

            output.WriteLine("=== FreeW packaging smoke ===");
            output.WriteLine($"sample_blocks={doc.Blocks.Count}");
            output.WriteLine($"command_bus_undo_redo={undoOk.ToString().ToLowerInvariant()}");
            output.WriteLine($"docx_bytes_written={writtenBytes}");
            output.WriteLine($"reopened_blocks={reopened.Blocks.Count}");
            output.WriteLine($"text_preserved={textPreserved.ToString().ToLowerInvariant()}");
            output.WriteLine($"blocks_preserved={blocksPreserved.ToString().ToLowerInvariant()}");

            var passed = undoOk && writtenBytes > 0 && textPreserved && blocksPreserved;
            output.WriteLine($"freew_packaging_smoke={(passed ? "passed" : "failed")}");
            exitCode = passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            error.WriteLine($"freew_packaging_smoke=failed: {ex}");
            exitCode = 1;
        }

        return true;
    }

    private sealed class SmokeContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
