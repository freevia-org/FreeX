using System.IO.Compression;
using System.Xml.Linq;

namespace FreeP.RenderCompare.Tests;

public sealed class CorpusSummaryTests
{
    [Fact]
    public void CreateReportsReadyIncompleteAndMissingReferenceDecks()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-render-summary-");
        var root = temporaryDirectory.Path;
        var corpus = Path.Combine(root, "corpus");
        var refs = Path.Combine(corpus, "pptx-ref");
        Directory.CreateDirectory(corpus);

        CreatePresentationZip(Path.Combine(corpus, "01-ready.pptx"), slideCount: 2);
        CreatePresentationZip(Path.Combine(corpus, "02-incomplete.pptx"), slideCount: 2);
        CreatePresentationZip(Path.Combine(corpus, "03-missing.pptx"), slideCount: 1);

        CreateRef(refs, "01-ready", "slide-01.png");
        CreateRef(refs, "01-ready", "slide-02.png");
        CreateRef(refs, "02-incomplete", "slide-01.png");

        var summary = CorpusSummary.Create(corpus, refs);

        summary.Decks.Should().HaveCount(3);
        summary.Decks.Single(d => d.DeckName == "01-ready.pptx").Status
            .Should().Be(CorpusDeckReferenceStatus.ReferenceReady);
        summary.Decks.Single(d => d.DeckName == "02-incomplete.pptx").Status
            .Should().Be(CorpusDeckReferenceStatus.IncompleteReferences);
        summary.Decks.Single(d => d.DeckName == "03-missing.pptx").Status
            .Should().Be(CorpusDeckReferenceStatus.MissingReferences);
    }

    [Fact]
    public void PrintIncludesCompactCorpusTotals()
    {
        var summary = new CorpusSummary(
            "corpus",
            "refs",
            new[]
            {
                new CorpusDeckStatus("a.pptx", "a.pptx", 1, 1, CorpusDeckReferenceStatus.ReferenceReady),
                new CorpusDeckStatus("b.pptx", "b.pptx", 2, 1, CorpusDeckReferenceStatus.IncompleteReferences),
                new CorpusDeckStatus("c.pptx", "c.pptx", 1, 0, CorpusDeckReferenceStatus.MissingReferences),
                new CorpusDeckStatus("d.pptx", "d.pptx", null, 1, CorpusDeckReferenceStatus.ReferenceReady),
            });
        using var writer = new StringWriter();

        summary.Print(writer);

        writer.ToString().Should().Contain("total=4; refs-ready=2; refs-incomplete=1; refs-missing=1; slide-count-unknown=1");
    }

    [Fact]
    public void CreateManifestIncludesPowerPointComGuardAndDeckTotals()
    {
        var summary = new CorpusSummary(
            "corpus",
            "refs",
            new[]
            {
                new CorpusDeckStatus("a.pptx", "a.pptx", 1, 1, CorpusDeckReferenceStatus.ReferenceReady),
                new CorpusDeckStatus("b.pptx", "b.pptx", 2, 1, CorpusDeckReferenceStatus.IncompleteReferences),
                new CorpusDeckStatus("c.pptx", "c.pptx", 1, 0, CorpusDeckReferenceStatus.MissingReferences),
            });
        var powerPoint = PowerPointComAvailability.Unavailable(
            PowerPointInterop.ProgId,
            new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero),
            "TESTBOX",
            "PowerPoint COM unavailable in test");

        var manifest = summary.CreateManifest(powerPoint);

        manifest.GeneratedAtUtc.Should().Be(powerPoint.CheckedAtUtc);
        manifest.MachineName.Should().Be("TESTBOX");
        manifest.PowerPoint.Should().Be(powerPoint);
        manifest.TotalDecks.Should().Be(3);
        manifest.ReferenceReadyCount.Should().Be(1);
        manifest.IncompleteReferenceCount.Should().Be(1);
        manifest.MissingReferenceCount.Should().Be(1);
        manifest.Decks.Should().ContainSingle(deck => deck.DeckName == "b.pptx" && deck.Status == CorpusDeckReferenceStatus.IncompleteReferences);
    }

    [Theory]
    [InlineData(false, false, true, 0)]
    [InlineData(true, false, true, 1)]
    [InlineData(true, true, false, 0)]
    [InlineData(true, true, true, 1)]
    public void GetBaselineVerificationExitCode_RequiresCompleteRefsUnlessMissingComIsAllowed(
        bool requireCompleteReferences,
        bool allowMissingPowerPoint,
        bool powerPointRegistered,
        int expected)
    {
        var summary = new CorpusSummary(
            "corpus",
            "refs",
            new[]
            {
                new CorpusDeckStatus("a.pptx", "a.pptx", 1, 1, CorpusDeckReferenceStatus.ReferenceReady),
                new CorpusDeckStatus("b.pptx", "b.pptx", 1, 0, CorpusDeckReferenceStatus.MissingReferences),
            });
        var powerPoint = powerPointRegistered
            ? PowerPointComAvailability.Available(PowerPointInterop.ProgId, DateTimeOffset.UtcNow, "TESTBOX")
            : PowerPointComAvailability.Unavailable(PowerPointInterop.ProgId, DateTimeOffset.UtcNow, "TESTBOX", "missing");

        summary.GetBaselineVerificationExitCode(
                powerPoint,
                requireCompleteReferences,
                allowMissingPowerPoint)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void CheckAvailabilityReportsMissingProgIdWithoutLaunchingPowerPoint()
    {
        var availability = PowerPointInterop.CheckAvailability(
            _ => null,
            new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero),
            "TESTBOX");

        availability.IsRegistered.Should().BeFalse();
        availability.ProgId.Should().Be(PowerPointInterop.ProgId);
        availability.MachineName.Should().Be("TESTBOX");
        availability.UnavailableReason.Should().Contain("is not registered");
    }

    private static void CreateRef(string refs, string deckStem, string fileName)
    {
        var directory = Path.Combine(refs, deckStem);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), "png placeholder");
    }

    private static void CreatePresentationZip(string path, int slideCount)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("ppt/presentation.xml");
        using var stream = entry.Open();
        var document = new XDocument(
            new XElement(
                XName.Get("presentation", "http://schemas.openxmlformats.org/presentationml/2006/main"),
                new XElement(
                    XName.Get("sldIdLst", "http://schemas.openxmlformats.org/presentationml/2006/main"),
                    Enumerable.Range(1, slideCount).Select(
                        slide => new XElement(
                            XName.Get("sldId", "http://schemas.openxmlformats.org/presentationml/2006/main"),
                            new XAttribute("id", 255 + slide))))));
        document.Save(stream);
    }
}
