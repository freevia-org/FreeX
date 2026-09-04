using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r391: numbers written into a .pptx package must be culture-invariant.
///
/// <para>Most DrawingML geometry is EMU integers, which are safe by construction, but the model also
/// carries doubles (rotation, gradient direction, scale and skew percentages) that reach the wire as
/// text. A comma decimal separator there produces a deck PowerPoint rejects, for users of that
/// locale only.</para>
///
/// <para>Rather than checking known strings, this scans EVERY attribute of every part and flags any
/// whose entire value is a comma-decimal number -- so it catches a culture-formatted value wherever
/// it appears, including parts added later. Each case self-checks that the culture took effect
/// first.</para>
/// </summary>
public sealed class R391_PptxNumbersAreCultureInvariantTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void WrittenPackageHasNoCultureFormattedNumbers(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            Assert.Equal("3,14", 3.14.ToString());

            var presentation = new Presentation();
            var slide = new Slide();
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
            paragraph.Runs.Add(new Run { Text = "probe" });
            shape.TextBody!.Paragraphs.Add(paragraph);
            slide.Shapes.Add(shape);
            presentation.Slides.Add(slide);

            using var stream = new MemoryStream();
            PptxPackageWriter.Write(presentation, stream);
            stream.Position = 0;

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var report = new System.Text.StringBuilder();
            var commaDecimal = new Regex(@"^-?\d{1,15},\d+$");

            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".xml", StringComparison.Ordinal) &&
                    !entry.FullName.EndsWith(".rels", StringComparison.Ordinal))
                {
                    continue;
                }

                using var entryStream = entry.Open();
                XDocument document;
                try { document = XDocument.Load(entryStream); }
                catch (System.Xml.XmlException) { continue; }

                foreach (var element in document.Descendants())
                {
                    foreach (var attribute in element.Attributes())
                    {
                        if (commaDecimal.IsMatch(attribute.Value))
                            report.AppendLine($"{entry.FullName}: {element.Name.LocalName}/@{attribute.Name.LocalName} = {attribute.Value}");
                    }
                }
            }

            Assert.True(report.Length == 0, "culture-formatted numbers on the wire:\n" + report);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
