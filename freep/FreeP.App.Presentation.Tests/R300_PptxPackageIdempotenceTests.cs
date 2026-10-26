using System.IO.Compression;
using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r300: completes the idempotence sweep begun in r298 (FreeX spreadsheets) and continued in r299
/// (FreeW documents) with FreeP's presentation package.
///
/// <para>Three separate properties, separated because they fail for different reasons and a single
/// "the bytes match" assertion cannot tell them apart:</para>
/// <list type="number">
/// <item>the writer is DETERMINISTIC -- writing one model twice gives identical bytes, so any
/// difference downstream is attributable to the reader rather than to a timestamp or a fresh GUID;</item>
/// <item>a round trip preserves the PART SET -- nothing is dropped from or added to the package;</item>
/// <item>the round trip CONVERGES -- two parts are normalised on first reload and stable thereafter.</item>
/// </list>
///
/// <para>Establishing (1) first is what made (3) interpretable. `ppt/presProps.xml` differs between
/// the first and second save at IDENTICAL length, which is the signature of a regenerated
/// identifier -- and had the writer been nondeterministic, every save of an unedited file would
/// dirty it, which is a different and worse problem than a one-time normalisation.</para>
/// </summary>
public sealed class R300_PptxPackageIdempotenceTests
{
    private static Presentation Sample()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600,
        });
        return presentation;
    }

    private static byte[] Write(Presentation presentation)
    {
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static Dictionary<string, string> Parts(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            parts[entry.FullName] = reader.ReadToEnd();
        }

        return parts;
    }

    /// <summary>
    /// Compared by PART rather than by raw bytes: zip container metadata is not the adapter's
    /// output, and comparing it would report a difference that means nothing.
    /// </summary>
    [Fact]
    public void WritingTheSameModelTwiceProducesIdenticalParts()
    {
        var first = Parts(Write(Sample()));
        var second = Parts(Write(Sample()));

        first.Keys.Should().BeEquivalentTo(second.Keys);
        foreach (var (name, content) in first)
        {
            second[name].Should().Be(content,
                $"{name} differs between two writes of the SAME model. A nondeterministic writer "
                + "dirties an unedited file on every save, which version control and "
                + "external-modification detection both read as a real change");
        }
    }

    [Fact]
    public void ARoundTripKeepsEveryPart()
    {
        var first = Parts(Write(Sample()));
        var reloaded = PptxPackageReader.Read(new MemoryStream(Write(Sample())));
        var second = Parts(Write(reloaded));

        second.Keys.Should().BeEquivalentTo(first.Keys,
            "a part dropped on reload is content the next reader never sees, and a part invented is "
            + "content the author never wrote");
    }

    /// <summary>
    /// Two parts -- the theme and the presentation properties -- are rewritten on first reload and
    /// stable from then on. Convergence is the property that matters: growth that compounded would
    /// inflate the file on every open-and-save cycle.
    /// </summary>
    [Fact]
    public void TheRoundTripConvergesAfterOneReload()
    {
        var presentation = Sample();
        var shapes = new List<string>();

        for (var i = 0; i < 4; i++)
        {
            var bytes = Write(presentation);
            var parts = Parts(bytes);
            shapes.Add($"{parts["ppt/theme/theme1.xml"]}|{parts["ppt/presProps.xml"]}");
            presentation = PptxPackageReader.Read(new MemoryStream(bytes));
        }

        shapes.Skip(1).Distinct().Should().ContainSingle(
            "the second save onwards must be identical. A package that kept changing would never "
            + "settle, so no save could ever be compared against its predecessor");
    }
}
