using FreeW.Core.IO;
using FreeW.Core.Model;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;

namespace FreeW.Validation.Avalonia;

/// <summary>
/// Headless engine smoke (no display) for the Linux packaging lane: builds a document, edits it,
/// round-trips it through the DOCX writer/reader, and verifies the model survives. Mirrors the
/// FreeX <c>--packaging-smoke</c> contract so the same CI step shape validates both apps.
/// </summary>
internal static class PackagingSmoke
{
    public static bool TryRun(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        out int exitCode) =>
        SisterAppPackagingSmoke.TryRun(args, output, error, Execute, HandleException, out exitCode);

    private static SisterAppPackagingSmokeResult Execute(IReadOnlyList<string> _)
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

        var passed = undoOk && writtenBytes > 0 && textPreserved && blocksPreserved;
        var report =
            "=== FreeW packaging smoke ===\n" +
            $"sample_blocks={doc.Blocks.Count}\n" +
            $"command_bus_undo_redo={undoOk.ToString().ToLowerInvariant()}\n" +
            $"docx_bytes_written={writtenBytes}\n" +
            $"reopened_blocks={reopened.Blocks.Count}\n" +
            $"text_preserved={textPreserved.ToString().ToLowerInvariant()}\n" +
            $"blocks_preserved={blocksPreserved.ToString().ToLowerInvariant()}\n" +
            $"freew_packaging_smoke={(passed ? "passed" : "failed")}\n";

        return new SisterAppPackagingSmokeResult(
            passed ? 0 : 1,
            SisterAppPackagingSmokeOutputTarget.StandardOutput,
            report);
    }

    private static SisterAppPackagingSmokeResult HandleException(Exception exception) =>
        new(
            1,
            SisterAppPackagingSmokeOutputTarget.StandardError,
            $"freew_packaging_smoke=failed: {exception}{Environment.NewLine}");

    private sealed class SmokeContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
