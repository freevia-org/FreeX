using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r454: a slide that could not be read is recovered as blank -- and the user is told.
///
/// <para>r448 fixed the case where NOTHING could be read and deliberately left this half alone,
/// recording it as needing a channel the reader did not have. Absorbing one bad slide rather than
/// losing the whole deck is right and matches PowerPoint; what was wrong is that PowerPoint TELLS you
/// it repaired the file, while this reader replaced the slide with a blank one and said nothing.</para>
///
/// <para>Silence is what makes it destructive. A blank slide is indistinguishable from a slide the
/// author left blank, so the user has no reason to suspect anything -- and the next save writes the
/// blank over whatever the file still held.</para>
///
/// <para>The channel is shaped after FreeX's <c>XlsxLoadResult</c>, so the two apps report load
/// damage the same way rather than each inventing one.</para>
/// </summary>
public sealed class R454_ADamagedSlideIsReportedNotSilentlyBlankedTests
{
    private static byte[] DeckBytes(int slideCount)
    {
        var presentation = new Presentation();

        for (var index = 0; index < slideCount; index++)
        {
            var slide = new Slide();
            var shape = new SlideShape
            {
                Id = (uint)(index + 2),
                Name = "Body" + index,
                OffsetXEmu = 100000,
                OffsetYEmu = 200000,
                ExtentCxEmu = 900000,
                ExtentCyEmu = 400000,
                TextBody = new TextBody(),
            };

            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = "slide " + index });
            shape.TextBody!.Paragraphs.Add(paragraph);
            slide.Shapes.Add(shape);
            presentation.Slides.Add(slide);
        }

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static byte[] WithDamagedSlide(byte[] original, string slidePart)
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

                var damaged =
                    entry.FullName.Contains(slidePart, StringComparison.OrdinalIgnoreCase) &&
                    !entry.FullName.Contains("_rels", StringComparison.OrdinalIgnoreCase);

                var created = writer.CreateEntry(entry.FullName);
                using var createdStream = created.Open();
                var bytes = damaged ? Encoding.UTF8.GetBytes("not xml at all") : buffer.ToArray();
                createdStream.Write(bytes, 0, bytes.Length);
            }
        }

        return output.ToArray();
    }

    private static PptxReadResult Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return PptxPackageReader.ReadWithWarnings(stream);
    }

    [Fact]
    public void ADamagedSlideProducesAWarningNamingIt()
    {
        var result = Read(WithDamagedSlide(DeckBytes(3), "ppt/slides/slide2.xml"));

        result.Warnings.Should().ContainSingle(
                "the user cannot tell a recovered blank slide from one that was always blank")
            .Which.Should().Contain("Slide 2").And.Contain("damaged");
    }

    [Fact]
    public void TheRestOfTheDeckStillOpens()
    {
        // The recovery this warning describes must still happen: reporting the damage is an addition,
        // not a replacement for keeping the surviving slides.
        var result = Read(WithDamagedSlide(DeckBytes(3), "ppt/slides/slide2.xml"));

        result.Presentation.Slides.Should().HaveCount(3, "one bad slide must not cost the whole deck");
        result.Presentation.Slides.Sum(slide => slide.Shapes.Count)
            .Should().Be(2, "and exactly the damaged slide is the empty one");
    }

    [Fact]
    public void AnUndamagedDeckWarnsAboutNothing()
    {
        // A warning that fires on healthy files trains the user to ignore the one that matters.
        var result = Read(DeckBytes(3));

        result.Warnings.Should().BeEmpty();
        result.Presentation.Slides.Should().HaveCount(3);
    }

    [Fact]
    public void TheWarningReachesTheOpenResult()
    {
        // End to end: a warning the reader produces but no layer carries is worth nothing. This is
        // the seam that was missing when r448 recorded this half as unfixable.
        var path = Path.Combine(Path.GetTempPath(), "r454_" + Guid.NewGuid().ToString("N") + ".pptx");
        File.WriteAllBytes(path, WithDamagedSlide(DeckBytes(3), "ppt/slides/slide2.xml"));

        try
        {
            var opened = PresentationFilePersistenceWorkflow.Open(path);

            opened.LoadWarnings.Should().NotBeNullOrEmpty(
                "the open path is what the shell reports from; a warning that stops at the reader " +
                "never reaches anybody");
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void AnUndamagedFileCarriesNoWarningThroughTheOpenPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "r454ok_" + Guid.NewGuid().ToString("N") + ".pptx");
        File.WriteAllBytes(path, DeckBytes(2));

        try
        {
            var opened = PresentationFilePersistenceWorkflow.Open(path);

            opened.LoadWarnings.Should().BeNullOrEmpty("nothing was damaged");
            opened.Presentation.Slides.Should().HaveCount(2);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }
}
