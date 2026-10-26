using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r448: a .pptx that still carries its slides must never open as an empty presentation.
///
/// <para>Found by feeding the reader deliberately damaged packages -- the technique that produced
/// eight defects in FreeX and none in the siblings, because FreeX delegates to a library that throws
/// while FreeP hand-parses with null-tolerant lookups and therefore degrades quietly.</para>
///
/// <para>The slide list is read as <c>presRoot.Element(sldIdLst)?.Elements(sldId)</c>, so a
/// presentation.xml the reader does not recognise -- a partially written save, or simply an
/// unexpected namespace -- produced ZERO slides and no error whatsoever. The deck opened empty. The
/// moment the user saved that window, the original file was overwritten with nothing.</para>
///
/// <para>This is the worst form of the shape this review keeps finding: not a crash, but damage that
/// looks deliberate. An empty document is a state the user can cause themselves, so nothing about
/// the window says the file was not read.</para>
/// </summary>
public sealed class R448_DamagedPresentationDoesNotOpenAsEmptyTests
{
    private static byte[] DeckBytes(int slideCount)
    {
        var presentation = new Presentation();

        for (var index = 0; index < slideCount; index++)
        {
            var slide = new Slide();
            slide.Shapes.Add(new SlideShape
            {
                Id = (uint)(index + 2),
                Name = "Body" + index,
                OffsetXEmu = 100000,
                OffsetYEmu = 200000,
                ExtentCxEmu = 1000000,
                ExtentCyEmu = 500000,
            });
            presentation.Slides.Add(slide);
        }

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static byte[] Rewrite(byte[] original, Func<string, byte[], byte[]?> mutate)
    {
        using var source = new MemoryStream(original);
        using var reader = new ZipArchive(source, ZipArchiveMode.Read);
        var output = new MemoryStream();

        using (var writer = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in reader.Entries)
            {
                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                entryStream.CopyTo(buffer);

                var replacement = mutate(entry.FullName, buffer.ToArray());
                if (replacement is null)
                    continue;

                var created = writer.CreateEntry(entry.FullName);
                using var createdStream = created.Open();
                createdStream.Write(replacement, 0, replacement.Length);
            }
        }

        return output.ToArray();
    }

    private static Presentation Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return PptxPackageReader.Read(stream);
    }

    [Fact]
    public void APackageWhoseSlideListCannotBeReadIsReportedAsDamaged()
    {
        var damaged = Rewrite(DeckBytes(3), (name, data) =>
            name.EndsWith("ppt/presentation.xml", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetBytes("<unrecognised/>")
                : data);

        var open = () => Read(damaged);

        open.Should().Throw<InvalidDataException>(
                "the slides are still in the package, so opening this as an empty deck and letting " +
                "the user save over it would destroy them")
            .WithMessage("*damaged*");
    }

    [Fact]
    public void TheSlidesAreStillPresentInTheDamagedPackage()
    {
        // The premise of the test above, asserted rather than assumed: if the mutation had removed
        // the slide parts too, refusing to open would be trivially correct and would prove nothing
        // about recovering a deck whose content survives.
        var damaged = Rewrite(DeckBytes(3), (name, data) =>
            name.EndsWith("ppt/presentation.xml", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetBytes("<unrecognised/>")
                : data);

        using var stream = new MemoryStream(damaged);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        archive.Entries
            .Count(entry => entry.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                            && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Should().Be(3, "the user's three slides are still on disk and are what the guard protects");
    }

    [Fact]
    public void AGenuinelySlidelessPackageStillOpensSilently()
    {
        // The guard must be narrow. A presentation with no slides carries no slide parts either, so
        // there is no contradiction to report -- and a guard that fired here would refuse to open a
        // file this very writer produces.
        var empty = DeckBytes(0);

        var presentation = Read(empty);

        presentation.Slides.Should().BeEmpty("an empty deck is a legitimate package, not a damaged one");
    }

    [Fact]
    public void AnUndamagedDeckIsUnaffected()
    {
        var presentation = Read(DeckBytes(3));

        presentation.Slides.Should().HaveCount(3, "the guard must not disturb the ordinary path");
    }

    [Fact]
    public void OneUnreadableSlideStillCostsOnlyThatSlide()
    {
        // The deliberate recovery documented in the reader -- "one malformed slide part must not cost
        // the user the whole deck" -- must survive this change. The guard fires only when NOTHING
        // could be resolved, so a single corrupt slide is still absorbed and the rest open.
        var damaged = Rewrite(DeckBytes(3), (name, data) =>
            name.Contains("ppt/slides/slide1.xml", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetBytes("not xml at all")
                : data);

        var presentation = Read(damaged);

        presentation.Slides.Should().HaveCount(3, "the surviving slides must still open");
        presentation.Slides.Sum(slide => slide.Shapes.Count)
            .Should().Be(2, "only the damaged slide loses its content");
    }
}
