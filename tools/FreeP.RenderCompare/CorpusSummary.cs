using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Free.Shared.AppServices;

namespace FreeP.RenderCompare;

internal sealed record CorpusSummary(
    string CorpusDirectory,
    string ReferenceDirectory,
    IReadOnlyList<CorpusDeckStatus> Decks)
{
    internal static CorpusSummary Create(string corpusDirectory, string referenceDirectory)
    {
        var decks = Directory.GetFiles(corpusDirectory, "*.pptx", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => CorpusDeckStatus.Create(path, referenceDirectory))
            .ToList();

        return new CorpusSummary(corpusDirectory, referenceDirectory, decks);
    }

    internal void Print(TextWriter writer)
    {
        writer.WriteLine("FreeP render corpus summary");
        writer.WriteLine($"  corpus : {CorpusDirectory}");
        writer.WriteLine($"  refs   : {ReferenceDirectory}");
        writer.WriteLine();
        writer.WriteLine($"{"Deck",-28} {"Slides",6} {"Refs",5} Status");
        writer.WriteLine(new string('-', 56));

        foreach (var deck in Decks)
        {
            var slides = deck.ExpectedSlides?.ToString() ?? "?";
            writer.WriteLine($"{deck.DeckName,-28} {slides,6} {deck.ReferenceSlideCount,5} {deck.Status}");
        }

        writer.WriteLine(new string('-', 56));
        writer.WriteLine(
            $"total={Decks.Count}; refs-ready={Decks.Count(d => d.Status == CorpusDeckReferenceStatus.ReferenceReady)}; " +
            $"refs-incomplete={Decks.Count(d => d.Status == CorpusDeckReferenceStatus.IncompleteReferences)}; " +
            $"refs-missing={Decks.Count(d => d.Status == CorpusDeckReferenceStatus.MissingReferences)}; " +
            $"slide-count-unknown={Decks.Count(d => d.ExpectedSlides is null)}");
    }

    internal bool HasCompleteReferences =>
        Decks.All(deck => deck.Status == CorpusDeckReferenceStatus.ReferenceReady);

    internal CorpusBaselineManifest CreateManifest(PowerPointComAvailability powerPoint) =>
        new(
            GeneratedAtUtc: powerPoint.CheckedAtUtc,
            MachineName: powerPoint.MachineName,
            PowerPoint: powerPoint,
            CorpusDirectory: CorpusDirectory,
            ReferenceDirectory: ReferenceDirectory,
            TotalDecks: Decks.Count,
            ReferenceReadyCount: Decks.Count(d => d.Status == CorpusDeckReferenceStatus.ReferenceReady),
            IncompleteReferenceCount: Decks.Count(d => d.Status == CorpusDeckReferenceStatus.IncompleteReferences),
            MissingReferenceCount: Decks.Count(d => d.Status == CorpusDeckReferenceStatus.MissingReferences),
            SlideCountUnknownCount: Decks.Count(d => d.ExpectedSlides is null),
            Decks: Decks);

    internal int GetBaselineVerificationExitCode(
        PowerPointComAvailability powerPoint,
        bool requireCompleteReferences,
        bool allowMissingPowerPoint)
    {
        if (!requireCompleteReferences || HasCompleteReferences)
            return 0;

        return allowMissingPowerPoint && !powerPoint.IsRegistered
            ? 0
            : 1;
    }

    internal void PrintBaselineVerification(
        TextWriter writer,
        PowerPointComAvailability powerPoint,
        bool requireCompleteReferences,
        bool allowMissingPowerPoint)
    {
        writer.WriteLine();
        writer.WriteLine("PowerPoint baseline verifier");
        writer.WriteLine($"  COM ProgID registered : {powerPoint.IsRegistered}");
        if (!powerPoint.IsRegistered)
            writer.WriteLine($"  skip reason           : {powerPoint.UnavailableReason}");

        if (!requireCompleteReferences)
        {
            writer.WriteLine("  policy                : report only");
            return;
        }

        if (HasCompleteReferences)
        {
            writer.WriteLine("  policy                : complete references required; all references present");
            return;
        }

        if (allowMissingPowerPoint && !powerPoint.IsRegistered)
        {
            writer.WriteLine("  policy                : complete references required; missing refs allowed because PowerPoint COM is unavailable");
            return;
        }

        writer.WriteLine("  policy                : complete references required; missing refs fail this run");
    }

    internal static void WriteManifest(string path, CorpusBaselineManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        JsonArtifactIO.Write(path, manifest, options);
    }
}

internal sealed record CorpusBaselineManifest(
    DateTimeOffset GeneratedAtUtc,
    string MachineName,
    PowerPointComAvailability PowerPoint,
    string CorpusDirectory,
    string ReferenceDirectory,
    int TotalDecks,
    int ReferenceReadyCount,
    int IncompleteReferenceCount,
    int MissingReferenceCount,
    int SlideCountUnknownCount,
    IReadOnlyList<CorpusDeckStatus> Decks);

internal sealed record CorpusDeckStatus(
    string DeckPath,
    string DeckName,
    int? ExpectedSlides,
    int ReferenceSlideCount,
    CorpusDeckReferenceStatus Status)
{
    internal static CorpusDeckStatus Create(string deckPath, string referenceRoot)
    {
        var deckName = Path.GetFileName(deckPath);
        var stem = Path.GetFileNameWithoutExtension(deckPath);
        var expectedSlides = TryCountSlides(deckPath);
        var referenceDir = Path.Combine(referenceRoot, stem);
        var referenceSlideCount = Directory.Exists(referenceDir)
            ? Directory.GetFiles(referenceDir, "slide-*.png", SearchOption.TopDirectoryOnly).Length
            : 0;

        var status = referenceSlideCount == 0
            ? CorpusDeckReferenceStatus.MissingReferences
            : expectedSlides is int count && referenceSlideCount < count
                ? CorpusDeckReferenceStatus.IncompleteReferences
                : CorpusDeckReferenceStatus.ReferenceReady;

        return new CorpusDeckStatus(deckPath, deckName, expectedSlides, referenceSlideCount, status);
    }

    private static int? TryCountSlides(string deckPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(deckPath);
            var entry = archive.GetEntry("ppt/presentation.xml");
            if (entry is null)
                return null;

            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            XNamespace presentation = "http://schemas.openxmlformats.org/presentationml/2006/main";
            return document.Descendants(presentation + "sldId").Count();
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

internal enum CorpusDeckReferenceStatus
{
    ReferenceReady,
    IncompleteReferences,
    MissingReferences
}
