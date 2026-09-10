using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r603: the READ half of the culture census. r391 pins what FreeP WRITES; nothing pinned what it
/// reads back, and FreeX (r392) and FreeW (r396) both round-trip. That asymmetry hid a defect the
/// existing censuses cannot see: they vary the DECIMAL SEPARATOR (de-DE / fr-FR / tr-TR), while the
/// reader's exposure is the SIGN.
///
/// <para>Two-argument <c>int.TryParse(s, out v)</c> uses CurrentCulture with
/// <see cref="NumberStyles.Integer"/>, so the accepted negative sign is the culture's
/// <c>NegativeSign</c>. 41 cultures do not use ASCII '-'; .NET forgives the U+2212 ones, but
/// fa/fa-IR/fa-AF spell it U+200E U+2212 and <c>int.TryParse("-100", Integer, fa-IR)</c> returns
/// FALSE. DrawingML always writes ASCII '-', so on those machines every negative attribute -- run
/// tracking, baseline offset, shadow direction, tab position -- silently reverts to its default.</para>
///
/// <para>The assertion re-serialises the round-tripped model under the invariant culture and
/// compares part text against the invariant round trip, so it covers every numeric attribute the
/// writer emits rather than a hand-listed few.</para>
/// </summary>
public sealed class R603_PptxReadsNumbersCultureInvariantTests(ITestOutputHelper output)
{
    private static Presentation Fixture()
    {
        var presentation = new Presentation();
        var shape = new SlideShape
        {
            Id = 2,
            Name = "Probe",
            OffsetXEmu = 1234500,
            OffsetYEmu = 987250,
            ExtentCxEmu = 4321750,
            ExtentCyEmu = 555125,
            RotationDeg = 33.5,
            TextBody = new TextBody(),
        };

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run
        {
            Text = "probe",
            // Negative on purpose: tight tracking and a raised baseline are ordinary authoring
            // values, and the sign is the whole exposure this test exists for.
            CharacterSpacingHundredthsPt = -150,
            BaselineOffset = -25000,
        });
        shape.TextBody!.Paragraphs.Add(paragraph);

        var slide = new Slide();
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        return presentation;
    }

    private static byte[] WriteInvariant(Presentation presentation)
    {
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static Dictionary<string, string> XmlParts(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".xml", StringComparison.Ordinal) &&
                !entry.FullName.EndsWith(".rels", StringComparison.Ordinal))
            {
                continue;
            }

            using var reader = new StreamReader(entry.Open());
            parts[entry.FullName] = reader.ReadToEnd();
        }

        return parts;
    }

    private static T UnderCulture<T>(string culture, Func<T> body)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture.Length == 0
            ? CultureInfo.InvariantCulture
            : new CultureInfo(culture);
        try
        {
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    [InlineData("fa-IR")]
    [InlineData("sv-SE")]
    [InlineData("ar-SA")]
    public void ReadingAPackageDoesNotDependOnTheCurrentCulture(string culture)
    {
        var package = UnderCulture("", () => WriteInvariant(Fixture()));

        // Non-vacuity: the fixture must actually put a negative number on the wire, or the whole
        // comparison below is a green over a surface it never touched.
        var written = XmlParts(package);
        var negativeBearing = written
            .Where(p => p.Value.Contains("spc=\"-150\"", StringComparison.Ordinal))
            .Select(p => p.Key)
            .ToList();
        Assert.True(
            negativeBearing.Count > 0,
            "fixture wrote no negative attribute; the census would be vacuous. Parts: " +
            string.Join(", ", written.Keys));

        var baseline = UnderCulture("", () => XmlParts(WriteInvariant(PptxPackageReader.Read(new MemoryStream(package)))));

        // Self-check: the culture actually took effect for this thread.
        var applied = UnderCulture(culture, () => CultureInfo.CurrentCulture.Name);
        Assert.Equal(new CultureInfo(culture).Name, applied);

        // Read under the foreign culture, then WRITE back under the invariant one, so any difference
        // is attributable to the read alone (r391 already pins the write).
        var model = UnderCulture(culture, () => PptxPackageReader.Read(new MemoryStream(package)));
        var actual = UnderCulture("", () => XmlParts(WriteInvariant(model)));

        var differing = baseline.Keys
            .Where(k => !actual.TryGetValue(k, out var text) || !string.Equals(text, baseline[k], StringComparison.Ordinal))
            .ToList();

        foreach (var part in differing)
            output.WriteLine($"DIFFERS {part}");

        Assert.True(
            differing.Count == 0,
            $"reading under {culture} produced a different model than reading under the invariant " +
            $"culture, in: {string.Join(", ", differing)}");
    }
}
