namespace FreeP.RenderCompare.Tests;

public sealed class PowerPointCorpusValidatorTests
{
    [Fact]
    public void Validate_ReportsSuccessfulExportsAndMatchingReferences()
    {
        using var fixture = new CorpusFixture();
        fixture.AddDeck("02-deck.pptx");
        fixture.AddDeck("01-deck.pptx");
        fixture.AddReference("01-deck", "slide-01.png", "one");
        fixture.AddReference("02-deck", "slide-01.png", "two");

        var result = PowerPointCorpusValidator.Validate(
            fixture.CorpusDirectory,
            fixture.OutputDirectory,
            fixture.ReferenceDirectory,
            1280,
            720,
            ExportOneSlide);

        result.ExitCode.Should().Be(0);
        result.ExportedDecks.Should().Be(2);
        result.FailedDecks.Should().Be(0);
        result.ComparedSlides.Should().Be(2);
        result.MatchingSlides.Should().Be(2);
        result.Decks.Select(deck => deck.DeckName).Should().Equal("01-deck.pptx", "02-deck.pptx");
    }

    [Fact]
    public void Validate_FailsOnReferenceDriftOrMissingReference()
    {
        using var fixture = new CorpusFixture();
        fixture.AddDeck("deck.pptx");
        fixture.AddReference("deck", "slide-01.png", "old");
        fixture.AddReference("deck", "slide-02.png", "extra");

        var result = PowerPointCorpusValidator.Validate(
            fixture.CorpusDirectory,
            fixture.OutputDirectory,
            fixture.ReferenceDirectory,
            1280,
            720,
            (deck, output, _, _) =>
            {
                File.WriteAllText(Path.Combine(output, "slide-01.png"), "new");
                return PowerPointExportResult.Success(1);
            });

        result.ExitCode.Should().Be(1);
        result.FailedDecks.Should().Be(0);
        result.MismatchedReferences.Should().Be(1);
        result.MissingReferences.Should().Be(1);
    }

    [Fact]
    public void Validate_FailsWhenPowerPointDoesNotExportEverySlide()
    {
        using var fixture = new CorpusFixture();
        fixture.AddDeck("deck.pptx");

        var result = PowerPointCorpusValidator.Validate(
            fixture.CorpusDirectory,
            fixture.OutputDirectory,
            referenceDirectory: null,
            width: 1280,
            height: 720,
            (_, _, _, _) => PowerPointExportResult.Failed(
                PowerPointExportFailureKind.ExportFailed,
                exportedSlides: 0,
                totalSlides: 1));

        result.ExitCode.Should().Be(1);
        result.FailedDecks.Should().Be(1);
        result.ExportedDecks.Should().Be(0);
    }

    private static PowerPointExportResult ExportOneSlide(
        string deck,
        string output,
        int width,
        int height)
    {
        var text = Path.GetFileNameWithoutExtension(deck).StartsWith("01", StringComparison.Ordinal)
            ? "one"
            : "two";
        File.WriteAllText(Path.Combine(output, "slide-01.png"), text);
        return PowerPointExportResult.Success(1);
    }

    private sealed class CorpusFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "freep-corpus-validator-" + Guid.NewGuid().ToString("N"));

        internal string CorpusDirectory { get; }
        internal string OutputDirectory { get; }
        internal string ReferenceDirectory { get; }

        internal CorpusFixture()
        {
            CorpusDirectory = Path.Combine(_root, "corpus");
            OutputDirectory = Path.Combine(_root, "output");
            ReferenceDirectory = Path.Combine(_root, "refs");
            Directory.CreateDirectory(CorpusDirectory);
            Directory.CreateDirectory(OutputDirectory);
            Directory.CreateDirectory(ReferenceDirectory);
        }

        internal void AddDeck(string name) => File.WriteAllText(Path.Combine(CorpusDirectory, name), string.Empty);

        internal void AddReference(string stem, string fileName, string contents)
        {
            var directory = Path.Combine(ReferenceDirectory, stem);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
