using System.IO;
using System.Security.Cryptography;

namespace FreeP.RenderCompare;

internal static class PowerPointCorpusValidator
{
    internal static PowerPointCorpusValidationResult CaptureReferences(
        string corpusDirectory,
        string referenceDirectory,
        int width,
        int height,
        Func<string, string, int, int, PowerPointExportResult>? exporter = null,
        TimeSpan? deckTimeout = null)
    {
        // Capture uses the same isolated worker and stale-slide cleanup as validation,
        // but deliberately omits reference comparison because the output is the refs.
        return Validate(
            corpusDirectory,
            referenceDirectory,
            referenceDirectory: null,
            width: width,
            height: height,
            exporter: exporter,
            deckTimeout: deckTimeout);
    }

    internal static PowerPointCorpusValidationResult Validate(
        string corpusDirectory,
        string outputDirectory,
        string? referenceDirectory,
        int width,
        int height,
        Func<string, string, int, int, PowerPointExportResult>? exporter = null,
        TimeSpan? deckTimeout = null,
        IReadOnlySet<string>? deckFilter = null,
        Action<PowerPointCorpusDeckResult>? onDeckCompleted = null)
    {
        exporter ??= (deckPath, deckOutputDirectory, exportWidth, exportHeight) =>
            PowerPointCorpusProcessExporter.Export(
                deckPath,
                deckOutputDirectory,
                exportWidth,
                exportHeight,
                deckTimeout ?? PowerPointCorpusProcessExporter.DefaultDeckTimeout);

        var decks = Directory.GetFiles(corpusDirectory, "*.pptx", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Where(deckPath => deckFilter is null ||
                deckFilter.Contains(Path.GetFileName(deckPath)) ||
                deckFilter.Contains(Path.GetFileNameWithoutExtension(deckPath)))
            .ToArray();
        if (deckFilter is not null && decks.Length == 0)
        {
            throw new InvalidOperationException(
                $"No PowerPoint corpus decks matched --decks: {string.Join(", ", deckFilter)}.");
        }
        var results = new List<PowerPointCorpusDeckResult>(decks.Length);

        Directory.CreateDirectory(outputDirectory);
        foreach (var deckPath in decks)
        {
            var deckName = Path.GetFileName(deckPath);
            var stem = Path.GetFileNameWithoutExtension(deckPath);
            var deckOutputDirectory = Path.Combine(outputDirectory, stem);
            Directory.CreateDirectory(deckOutputDirectory);
            foreach (var stalePng in Directory.GetFiles(deckOutputDirectory, "slide-*.png"))
                File.Delete(stalePng);

            var export = exporter(deckPath, deckOutputDirectory, width, height);
            var generated = Directory.GetFiles(deckOutputDirectory, "slide-*.png")
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var referenceRoot = referenceDirectory is null
                ? null
                : Path.Combine(referenceDirectory, stem);
            var comparisons = CompareReferences(generated, referenceRoot);

            var deckResult = new PowerPointCorpusDeckResult(
                deckName,
                export.ExitCode,
                export.FailureKind,
                export.ExportedSlides,
                export.TotalSlides,
                generated.Length,
                comparisons.ComparedSlides,
                comparisons.MatchingSlides,
                comparisons.MissingReferences,
                comparisons.MismatchedReferences);
            results.Add(deckResult);
            onDeckCompleted?.Invoke(deckResult);
        }

        return new PowerPointCorpusValidationResult(
            corpusDirectory,
            outputDirectory,
            referenceDirectory,
            width,
            height,
            results);
    }

    private static ReferenceComparison CompareReferences(
        IReadOnlyList<string> generated,
        string? referenceRoot)
    {
        if (referenceRoot is null)
            return new ReferenceComparison(0, 0, 0, 0);

        var generatedByName = generated.ToDictionary(GetFileName, StringComparer.OrdinalIgnoreCase);
        var referenceByName = Directory.Exists(referenceRoot)
            ? Directory.GetFiles(referenceRoot, "slide-*.png", SearchOption.TopDirectoryOnly)
                .ToDictionary(GetFileName, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var compared = 0;
        var matching = 0;
        var missing = 0;
        var mismatched = 0;
        foreach (var fileName in generatedByName.Keys.Union(referenceByName.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (!generatedByName.TryGetValue(fileName, out var generatedPath) ||
                !referenceByName.TryGetValue(fileName, out var referencePath))
            {
                missing++;
                continue;
            }

            compared++;
            if (CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(File.ReadAllBytes(generatedPath)),
                    SHA256.HashData(File.ReadAllBytes(referencePath))))
            {
                matching++;
            }
            else
            {
                mismatched++;
            }
        }

        return new ReferenceComparison(compared, matching, missing, mismatched);
    }

    private static string GetFileName(string path) => Path.GetFileName(path);

    private readonly record struct ReferenceComparison(
        int ComparedSlides,
        int MatchingSlides,
        int MissingReferences,
        int MismatchedReferences);
}

internal sealed record PowerPointCorpusValidationResult(
    string CorpusDirectory,
    string OutputDirectory,
    string? ReferenceDirectory,
    int Width,
    int Height,
    IReadOnlyList<PowerPointCorpusDeckResult> Decks)
{
    internal int ExportedDecks => Decks.Count(deck => deck.ExitCode == 0 && deck.GeneratedSlides == deck.TotalSlides);
    internal int FailedDecks => Decks.Count(deck => deck.ExitCode != 0 || deck.GeneratedSlides != deck.TotalSlides);
    internal int ComparedSlides => Decks.Sum(deck => deck.ComparedSlides);
    internal int MatchingSlides => Decks.Sum(deck => deck.MatchingSlides);
    internal int MissingReferences => Decks.Sum(deck => deck.MissingReferences);
    internal int MismatchedReferences => Decks.Sum(deck => deck.MismatchedReferences);
    internal int ExitCode => FailedDecks == 0 && MissingReferences == 0 && MismatchedReferences == 0 ? 0 : 1;

    internal void Print(TextWriter writer)
    {
        writer.WriteLine("PowerPoint corpus validation");
        writer.WriteLine($"  corpus     : {CorpusDirectory}");
        writer.WriteLine($"  output     : {OutputDirectory}");
        writer.WriteLine($"  references : {ReferenceDirectory ?? "n/a"}");
        writer.WriteLine($"  size       : {Width}x{Height}");
        writer.WriteLine();
        writer.WriteLine($"{"Deck",-28} {"Export",-10} {"Slides",7} {"Refs",12}");
        writer.WriteLine(new string('-', 64));
        foreach (var deck in Decks)
        {
            var export = deck.ExitCode == 0 && deck.GeneratedSlides == deck.TotalSlides
                ? "PASS"
                : $"FAIL({deck.FailureKind})";
            var refs = ReferenceDirectory is null
                ? "n/a"
                : $"{deck.MatchingSlides}/{deck.ComparedSlides}" +
                    (deck.MissingReferences > 0 ? $" +{deck.MissingReferences} missing" : string.Empty) +
                    (deck.MismatchedReferences > 0 ? $" +{deck.MismatchedReferences} diff" : string.Empty);
            writer.WriteLine($"{deck.DeckName,-28} {export,-10} {deck.GeneratedSlides,3}/{deck.TotalSlides,-3} {refs,12}");
        }

        writer.WriteLine(new string('-', 64));
        writer.WriteLine(
            $"decks={Decks.Count}; exported={ExportedDecks}; failed={FailedDecks}; " +
            $"reference-matches={MatchingSlides}/{ComparedSlides}; missing-refs={MissingReferences}; " +
            $"reference-diffs={MismatchedReferences}; exit={ExitCode}");
    }

    internal void PrintCapture(TextWriter writer)
    {
        writer.WriteLine("PowerPoint corpus reference capture");
        writer.WriteLine($"  corpus : {CorpusDirectory}");
        writer.WriteLine($"  refs   : {OutputDirectory}");
        writer.WriteLine($"  size   : {Width}x{Height}");
        writer.WriteLine();
        writer.WriteLine($"decks={Decks.Count}; exported={ExportedDecks}; failed={FailedDecks}; exit={ExitCode}");
    }
}

internal sealed record PowerPointCorpusDeckResult(
    string DeckName,
    int ExitCode,
    PowerPointExportFailureKind FailureKind,
    int ExportedSlides,
    int TotalSlides,
    int GeneratedSlides,
    int ComparedSlides,
    int MatchingSlides,
    int MissingReferences,
    int MismatchedReferences);
