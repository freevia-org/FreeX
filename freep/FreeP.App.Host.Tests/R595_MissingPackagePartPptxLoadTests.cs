using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeP.Core.IO;
using FreeP.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeP.App.Host.Tests;

public class R595_MissingPackagePartPptxLoadTests
{
    private static byte[] RichDocumentBytes()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Title = "First" });
        presentation.Slides.Add(new Slide { Title = "Second" });

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(presentation, ms);
        return ms.ToArray();
    }

    private static byte[] WithPartReplaced(byte[] source, string part, XDocument replacement)
    {
        using var input = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                var target = zip.CreateEntry(entry.FullName);
                using var targetStream = target.Open();
                if (entry.FullName == part)
                {
                    replacement.Save(targetStream);
                }
                else
                {
                    using var sourceStream = entry.Open();
                    sourceStream.CopyTo(targetStream);
                }
            }
        }

        return output.ToArray();
    }

    private static byte[] WithoutPart(byte[] source, string part)
    {
        using var input = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                if (entry.FullName == part) continue;
                var target = zip.CreateEntry(entry.FullName);
                using var targetStream = target.Open();
                using var sourceStream = entry.Open();
                sourceStream.CopyTo(targetStream);
            }
        }

        return output.ToArray();
    }

    private static string Describe(Presentation presentation)
    {
        var shapes = 0;
        foreach (var slide in presentation.Slides)
            shapes += slide.Shapes.Count;

        return $"slides={presentation.Slides.Count} shapes={shapes}";
    }

    [Fact]
    public void RemovingAnyPartEitherRefusesOrReportsTheLoss()
    {
        var source = RichDocumentBytes();
        string baseline;
        using (var ms = new MemoryStream(source))
        {
            var healthy = PptxPackageReader.ReadWithWarnings(ms);
            healthy.Warnings.Should().BeEmpty("a healthy package must load silently");
            baseline = Describe(healthy.Presentation);
        }

        List<string> parts;
        using (var zip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read))
            parts = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();

        parts.Should().HaveCountGreaterThan(10, "the fence is worthless if it stopped finding parts");

        var silent = new List<string>();
        foreach (var part in parts)
        {
            var mutated = WithoutPart(source, part);
            try
            {
                using var ms = new MemoryStream(mutated);
                var loaded = PptxPackageReader.ReadWithWarnings(ms);
                if (Describe(loaded.Presentation) != baseline && loaded.Warnings.Count == 0)
                    silent.Add($"{part} -> {Describe(loaded.Presentation)} with NO warning");
            }
            catch (InvalidDataException)
            {
                // FreeP's own typed refusal, with the actionable sentence PptxPackageReader owns.
            }
        }

        silent.Should().BeEmpty(
            "content may not vanish from a loaded presentation without the load either refusing or " +
            "reporting it");
    }
}
