using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.RenderCompare;

internal sealed record NotesPagePreviewEvidencePlan(
    string DeckPath,
    string OutputDirectory,
    string PdfPath,
    string SummaryCsvPath)
{
    internal bool RequiresPowerPointBaseline => false;
}

internal sealed record NotesPagePreviewEvidenceRow(
    int OutputPageNumber,
    int? SlideNumber,
    int SlideRenderedPageNumber,
    bool IsContinuation,
    int FirstNoteLineIndex,
    int NoteLineCount,
    bool ShowsPlaceholder,
    int StyledRunCount,
    string ThumbnailLabel,
    string Detail,
    string WpfEvidence,
    string AvaloniaEvidence,
    string PowerPointBaseline);

internal static class NotesPagePreviewEvidence
{
    private const string SharedEvidence = "shared-notes-page-pdf-render-plan";
    private const string NoComBaseline = "not-required-for-local-wpf-avalonia-evidence";

    internal static NotesPagePreviewEvidencePlan CreatePlan(string deckPath, string outputDirectory)
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        return new NotesPagePreviewEvidencePlan(
            Path.GetFullPath(deckPath),
            fullOutputDirectory,
            Path.Combine(fullOutputDirectory, "freep-notes-page-preview.pdf"),
            Path.Combine(fullOutputDirectory, "notes-page-preview-evidence.csv"));
    }

    internal static int Run(string deckPath, string outputDirectory)
    {
        var plan = CreatePlan(deckPath, outputDirectory);
        if (!File.Exists(plan.DeckPath))
        {
            Console.Error.WriteLine($"File not found: {plan.DeckPath}");
            return 1;
        }

        Directory.CreateDirectory(plan.OutputDirectory);

        var presentation = PptxPackageReader.Read(plan.DeckPath);
        var renderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(presentation);
        var rows = BuildRows(renderPlan);

        File.WriteAllBytes(plan.PdfPath, PresentationNotesPagePdfExporter.ExportToBytes(presentation));
        WriteSummaryCsv(plan.SummaryCsvPath, rows);

        Console.WriteLine("Notes-page preview evidence");
        Console.WriteLine($"  input       : {plan.DeckPath}");
        Console.WriteLine($"  outDir      : {plan.OutputDirectory}");
        Console.WriteLine($"  pdf         : {plan.PdfPath}");
        Console.WriteLine($"  summary     : {plan.SummaryCsvPath}");
        Console.WriteLine($"  pages       : {rows.Count}");
        Console.WriteLine("  PowerPoint  : not required for local WPF/Avalonia evidence");
        Console.WriteLine();
        PrintTable(rows);

        return 0;
    }

    internal static IReadOnlyList<NotesPagePreviewEvidenceRow> BuildRows(PresentationNotesPagePdfRenderPlan renderPlan)
    {
        ArgumentNullException.ThrowIfNull(renderPlan);

        var rows = new List<NotesPagePreviewEvidenceRow>();
        var outputPageNumber = 1;
        foreach (var preview in renderPlan.PreviewPlans)
        {
            foreach (var page in preview.RenderPages)
            {
                var styledRunCount = preview.StyledNoteLines
                    .Skip(page.FirstNoteLineIndex)
                    .Take(page.NoteLineCount)
                    .Sum(line => line.Runs.Count);

                rows.Add(new NotesPagePreviewEvidenceRow(
                    outputPageNumber++,
                    preview.SlideNumber,
                    page.PageNumber,
                    page.IsContinuation,
                    page.FirstNoteLineIndex,
                    page.NoteLineCount,
                    page.ShowsPlaceholder,
                    styledRunCount,
                    page.ThumbnailLabel,
                    page.Detail,
                    SharedEvidence,
                    SharedEvidence,
                    NoComBaseline));
            }
        }

        return rows;
    }

    internal static void WriteSummaryCsv(string path, IReadOnlyList<NotesPagePreviewEvidenceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path);
        writer.WriteLine(
            "outputPage,slideNumber,slideRenderedPage,isContinuation,firstNoteLine,noteLineCount,showsPlaceholder,styledRunCount,thumbnailLabel,detail,wpfEvidence,avaloniaEvidence,powerPointBaseline");
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(
                ',',
                row.OutputPageNumber.ToString(CultureInfo.InvariantCulture),
                row.SlideNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.SlideRenderedPageNumber.ToString(CultureInfo.InvariantCulture),
                CsvBool(row.IsContinuation),
                row.FirstNoteLineIndex.ToString(CultureInfo.InvariantCulture),
                row.NoteLineCount.ToString(CultureInfo.InvariantCulture),
                CsvBool(row.ShowsPlaceholder),
                row.StyledRunCount.ToString(CultureInfo.InvariantCulture),
                Csv(row.ThumbnailLabel),
                Csv(row.Detail),
                Csv(row.WpfEvidence),
                Csv(row.AvaloniaEvidence),
                Csv(row.PowerPointBaseline)));
        }
    }

    private static void PrintTable(IReadOnlyList<NotesPagePreviewEvidenceRow> rows)
    {
        Console.WriteLine($"{"Page",-6} {"Slide",-7} {"Lines",-7} {"Cont.",-6} {"Styled",-7} Evidence");
        Console.WriteLine(new string('-', 72));
        foreach (var row in rows)
        {
            Console.WriteLine(
                $"{row.OutputPageNumber,-6} {FormatSlide(row.SlideNumber),-7} {row.NoteLineCount,-7} {FormatBool(row.IsContinuation),-6} {row.StyledRunCount,-7} WPF/Avalonia shared");
        }

        Console.WriteLine(new string('-', 72));
    }

    private static string FormatSlide(int? slideNumber) =>
        slideNumber?.ToString(CultureInfo.InvariantCulture) ?? "n/a";

    private static string FormatBool(bool value) => value ? "yes" : "no";

    private static string CsvBool(bool value) => value ? "true" : "false";

    private static string Csv(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
