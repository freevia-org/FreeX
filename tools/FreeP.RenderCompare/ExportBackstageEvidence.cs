using System;
using System.Collections.Generic;
using System.IO;
using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.RenderCompare;

internal sealed record ExportBackstageEvidenceRunPlan(
    string DeckPath,
    string OutputDirectory,
    string SummaryCsvPath)
{
    internal bool RequiresPowerPointBaseline => false;
}

internal static class ExportBackstageEvidence
{
    internal static ExportBackstageEvidenceRunPlan CreatePlan(string deckPath, string outputDirectory)
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        return new ExportBackstageEvidenceRunPlan(
            Path.GetFullPath(deckPath),
            fullOutputDirectory,
            Path.Combine(fullOutputDirectory, "export-backstage-evidence.csv"));
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
        var evidence = PresentationExportBackstageEvidencePlanner.Build(
            presentation,
            Path.GetFileName(plan.DeckPath));
        WriteSummaryCsv(plan.SummaryCsvPath, evidence.Rows);

        Console.WriteLine("Export/backstage evidence");
        Console.WriteLine($"  input       : {plan.DeckPath}");
        Console.WriteLine($"  outDir      : {plan.OutputDirectory}");
        Console.WriteLine($"  summary     : {plan.SummaryCsvPath}");
        Console.WriteLine($"  rows        : {evidence.Rows.Count}");
        Console.WriteLine("  PowerPoint  : n/a/deferred for local WPF/Avalonia evidence");
        Console.WriteLine();
        PrintTable(evidence.Rows);

        return 0;
    }

    internal static void WriteSummaryCsv(
        string path,
        IReadOnlyList<PresentationExportBackstageEvidenceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path);
        writer.WriteLine(
            "evidenceId,area,sharedPlanner,status,wpfEvidence,avaloniaEvidence,powerPointBaseline,requiresPowerPointComBaseline,detail");
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(
                ',',
                Csv(row.EvidenceId),
                Csv(row.Area),
                Csv(row.SharedPlanner),
                Csv(row.Status),
                Csv(row.WpfEvidence),
                Csv(row.AvaloniaEvidence),
                Csv(row.PowerPointBaseline),
                row.RequiresPowerPointComBaseline ? "true" : "false",
                Csv(row.Detail)));
        }
    }

    private static void PrintTable(IReadOnlyList<PresentationExportBackstageEvidenceRow> rows)
    {
        Console.WriteLine($"{"Evidence",-44} {"Status",-42} PowerPoint");
        Console.WriteLine(new string('-', 108));
        foreach (var row in rows)
            Console.WriteLine($"{row.EvidenceId,-44} {row.Status,-42} {row.PowerPointBaseline}");
        Console.WriteLine(new string('-', 108));
    }

    private static string Csv(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
