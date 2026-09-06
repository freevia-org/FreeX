using System.IO;
using System.IO.Compression;
using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r499: save, reload, save again - the second and third packages must be identical.
///
/// <para>This is the invariant behind a whole family of defects rather than a single one. Accumulation
/// (an element re-added on every save because the rewrite removes some children and not others,
/// which is what r498 found statically), reordering, and nondeterminism (a fresh GUID or timestamp
/// written into a part) all show up here as drift between two consecutive saves, and all of them are
/// invisible to a test that saves only once.</para>
///
/// <para>Deliberately compares save2 against save3, not save1 against save2. The first save comes
/// from an in-memory model and the second from a parsed one, so they may legitimately differ where
/// the reader normalises; once a document has been through a round trip, further saves must be
/// stable.</para>
///
/// <para>LIMITATION, recorded because it bounds what this proves: the deck is built in memory, so it
/// exercises the writer's own output and NOT the preserved-source-XML rewrite paths where r483, r484
/// and r498 live. A synthetic Zoom shape was tried and never reached the package, so that path stays
/// unexercised here.</para>
/// </summary>
public sealed class R499_SavingTwiceProducesTheSamePackageTests
{
    private static byte[] Save(Presentation presentation)
    {
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static Presentation Load(byte[] package)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pptx");
        File.WriteAllBytes(path, package);
        try { return PptxPackageReader.Read(path); }
        finally { try { File.Delete(path); } catch { /* best effort */ } }
    }

    private static Dictionary<string, string> Parts(byte[] package)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            parts[entry.FullName] = reader.ReadToEnd();
        }

        return parts;
    }

    private static Presentation DeckWithAFilledShape()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            Name = "Box",
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 685800,
            Fill = new ShapeFill.Solid(new SrgbColor(0x40, 0x80, 0xC0)),
        });
        presentation.Slides.Add(slide);
        return presentation;
    }

    [Fact]
    public void ASecondAndThirdSaveAreByteIdentical()
    {
        var second = Parts(Save(Load(Save(DeckWithAFilledShape()))));
        var third = Parts(Save(Load(Save(Load(Save(DeckWithAFilledShape()))))));

        // Non-vacuity: the comparison is worthless if the deck's own content never reached the
        // package. The shape's fill colour must be in there, or the test is comparing empty scaffolding.
        second.Values.Should().Contain(part => part.Contains("4080C0", StringComparison.OrdinalIgnoreCase),
            "the authored shape fill must actually be written, or this compares nothing");
        second.Should().NotBeEmpty();

        third.Keys.Should().BeEquivalentTo(second.Keys, "a resave must not add or drop parts");

        foreach (var (name, content) in second)
        {
            third[name].Should().Be(
                content,
                $"part '{name}' drifted between two consecutive saves -- that is accumulation, " +
                "reordering or nondeterminism, and it compounds every time the user presses Ctrl+S");
        }
    }
}
