using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeP.Core.IO;
using FreeP.Core.Model;
using FluentAssertions;
using FluentAssertions;
using FluentAssertions;
using Xunit;

namespace FreeP.App.Host.Tests;

public class R601_SwappedPartContentPptxLoadTests
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

    private static byte[] SwappedContent(byte[] source, string part, string donorPart)
    {
        using var input = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        byte[] donor;
        using (var ds = input.GetEntry(donorPart)!.Open())
        using (var buf = new MemoryStream())
        {
            ds.CopyTo(buf);
            donor = buf.ToArray();
        }

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                var target = zip.CreateEntry(entry.FullName);
                using var ts = target.Open();
                if (entry.FullName == part) { ts.Write(donor, 0, donor.Length); continue; }
                using var ss = entry.Open();
                ss.CopyTo(ts);
            }
        }

        return output.ToArray();
    }

    private static byte[] Truncated(byte[] source, string part, double fraction)
    {
        using var input = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                var target = zip.CreateEntry(entry.FullName);
                using var ts = target.Open();
                using var ss = entry.Open();
                using var buf = new MemoryStream();
                ss.CopyTo(buf);
                var bytes = buf.ToArray();
                var keep = entry.FullName == part ? (int)(bytes.Length * fraction) : bytes.Length;
                ts.Write(bytes, 0, keep);
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

    /// <summary>
    /// r599: completing the truncation lens on the third app. Two findings, one of them the worst
    /// outcome a reader can have.
    /// <para>
    /// Truncating ppt/presentation.xml opened the deck as slides=0 with NO warning. FreeP has a guard
    /// written for precisely that -- r448's "slides on disk, none reachable" contradiction check,
    /// whose comment calls the end state intolerable and describes it as "a partially written save
    /// ... yields ZERO slides and no error at all". It sits after sldIdLst is read, and a
    /// presentation.xml that will not PARSE returns long before reaching it, so the guard could not
    /// see the case it was written for. It now runs on that path too, with the same narrowness: a
    /// genuinely slide-less package still opens quietly, so the deliberate blocked-DTD "quarantine
    /// the payload, do not crash" contract is untouched.
    /// </para>
    /// <para>
    /// A truncated docProps/core.xml threw a raw XmlException, the same shape r596 and r598 fixed in
    /// the xlsx and docx readers.
    /// </para>
    /// </summary>
    /// <summary>
    /// r601: the swapped-content shape on FreeP -- a part holding ANOTHER part's XML. Valid zip,
    /// well-formed, wrong schema, so neither r599's truncation mapping nor r595's missing-part
    /// reporting reaches it: TryLoadXmlPart succeeds and every p:sld-relative lookup then finds
    /// nothing, leaving the slide BLANK and silent.
    /// <para>
    /// r454 built the reporting for exactly this harm -- "a slide blank because it was damaged is
    /// indistinguishable from one the author left blank" -- but it fires only when the part will not
    /// parse. The root element name is the decisive test: a slide part whose root is not p:sld is
    /// wrong whatever else it holds. That is why this one is FIXED where FreeX's equivalent (r600)
    /// is pinned -- there, "declares nothing" is legitimate for a drawing, so no such clean
    /// contradiction exists.
    /// </para>
    /// </summary>
    [Fact]
    public void SwappingAnyPartsContentRefusesTypedOrReportsWhatItLost()
    {
        var source = RichDocumentBytes();
        string baseline;
        using (var ms = new MemoryStream(source))
            baseline = Describe(PptxPackageReader.ReadWithWarnings(ms).Presentation);

        List<string> parts;
        using (var zip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read))
            parts = zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();

        parts.Should().HaveCountGreaterThan(8, "the fence is worthless if it stopped finding parts");

        var leaked = new List<string>();
        var silent = new List<string>();

        foreach (var part in parts)
        foreach (var donor in parts)
        {
            if (part == donor) continue;
            try
            {
                using var ms = new MemoryStream(SwappedContent(source, part, donor));
                var loaded = PptxPackageReader.ReadWithWarnings(ms);
                if (Describe(loaded.Presentation) != baseline && loaded.Warnings.Count == 0)
                    silent.Add($"{part} <- {donor}");
            }
            catch (InvalidDataException) { }
            catch (Exception ex)
            {
                leaked.Add($"{part} <- {donor} -> {ex.GetType().Name}");
            }
        }

        leaked.Should().BeEmpty("a wrong-schema part must surface FreeP's own error");

        silent.Should().BeEmpty(
            "a slide that reads blank because its part is not a slide must say so -- otherwise it is " +
            "indistinguishable from one the author left blank, and saving discards what the package holds");
    }
}
