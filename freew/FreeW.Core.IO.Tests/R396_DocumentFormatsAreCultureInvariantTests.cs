using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r396: numbers FreeW writes into a document must be culture-invariant.
///
/// <para>Earlier rounds pinned this for FreeX's adapters, FreeP's .pptx writer and the shared PDF
/// tier. FreeW was surveyed by inspection only, never exercised -- so this closes the third app.
/// The exposure here is not cell values but FORMATTING: font sizes are half-points, indents and
/// spacing are twips, table widths and image extents are their own units, and every one of them is
/// written as text. WordprocessingML attributes are xsd numeric types that accept only '.', so a
/// value formatted on a German machine yields a document Word rejects -- for that user alone.</para>
///
/// <para>Driven by reflection over <see cref="IDocumentFileAdapter"/> so a format added later is
/// covered when it appears, and each case self-checks that the culture actually took effect before
/// trusting a pass. Adapters that cannot save are reported rather than silently skipped.</para>
/// </summary>
public sealed class R396_DocumentFormatsAreCultureInvariantTests
{
    private static TextDocument RichlyFormattedDocument()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph
        {
            Formatting = new ParagraphFormatting
            {
                SpaceBeforePt = 10.5,
                SpaceAfterPt = 7.25,
            },
        };

        paragraph.Runs.Add(new Run(
            "culture probe",
            new RunFormatting
            {
                FontSizePt = 10.5,
                CharacterSpacingPt = 1.75,
                PositionPt = 2.5,
            }));

        // A fractionally sized image matters: HtmlFileAdapter's only decimal formatter (FormatPt) is
        // used for image width/height alone, so a fixture without one leaves that path unexercised
        // and the scan reports a confident green over code it never reached. Verified by breaking
        // the formatter -- without this image HTML produced no offender, with it the break is caught.
        // Text paragraph first: the round-trip control below reads Paragraphs.First() for its font
        // size, so the image must not displace it.
        document.Blocks.Add(paragraph);

        var imageParagraph = new Paragraph();
        imageParagraph.Runs.Add(Run.FromImage(new InlineImage(MinimalPng(), 100, 80)
        {
            WidthPt = 12.5,
            HeightPt = 7.25,
        }));
        document.Blocks.Add(imageParagraph);

        return document;
    }

    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static IEnumerable<string> CommaDecimalAttributeValues(byte[] saved)
    {
        // Matches a comma BETWEEN digits anywhere in the value, not a value that is wholly a
        // comma-decimal. The stricter form was blind to precisely the formats that emit decimals:
        // ODT writes lengths with a unit suffix ("1.25cm") and HTML puts them inside a CSS
        // declaration ("font-size:10.5pt"), so neither is ever an attribute whose entire value is a
        // number. The fixture's only text is "culture probe", which has no digits, so a digit-comma-
        // digit sequence cannot arise from prose.
        var commaDecimal = new Regex(@"\d,\d");
        var offenders = new List<string>();

        void ScanXml(Stream stream, string label)
        {
            XDocument parsed;
            try { parsed = XDocument.Load(stream); }
            catch (System.Xml.XmlException) { return; }

            foreach (var attribute in parsed.Descendants().SelectMany(element => element.Attributes()))
            {
                if (commaDecimal.IsMatch(attribute.Value))
                    offenders.Add($"{label}: @{attribute.Name.LocalName} = {attribute.Value}");
            }
        }

        // A package (docx) is a zip of XML parts; the flat formats are inspected as text below.
        if (saved.Length > 4 && saved[0] == 'P' && saved[1] == 'K')
        {
            using var archive = new ZipArchive(new MemoryStream(saved, writable: false), ZipArchiveMode.Read);
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
            {
                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                entryStream.CopyTo(buffer);
                buffer.Position = 0;
                ScanXml(buffer, entry.FullName);
            }
        }
        else
        {
            // Flat formats keep their numbers outside XML attributes -- HTML in CSS declarations,
            // RTF in control words -- so the whole payload is scanned as text. Safe for the same
            // reason: the fixture contributes no digits of its own.
            var text = System.Text.Encoding.UTF8.GetString(saved);
            foreach (Match match in commaDecimal.Matches(text))
            {
                var start = Math.Max(0, match.Index - 30);
                offenders.Add($"(flat): ...{text.Substring(start, Math.Min(60, text.Length - start))}...");
            }
        }

        return offenders;
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void NoAdapterWritesACultureFormattedNumber(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);

        try
        {
            10.5.ToString().Should().Be(
                "10,5", "the culture must be in effect or a pass below means nothing");

            var adapters = typeof(IDocumentFileAdapter).Assembly
                .GetTypes()
                .Where(type => type is { IsAbstract: false, IsPublic: true } &&
                               typeof(IDocumentFileAdapter).IsAssignableFrom(type) &&
                               type.GetConstructor(Type.EmptyTypes) is not null)
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ToList();

            adapters.Should().HaveCountGreaterThanOrEqualTo(
                5,
                "the reflection query must still reach FreeW's adapters -- a smaller number means it " +
                "stopped covering formats. Found: " + string.Join(", ", adapters.Select(t => t.Name)));

            var offenders = new List<string>();
            var cannotSave = new List<string>();

            foreach (var type in adapters)
            {
                var adapter = (IDocumentFileAdapter)Activator.CreateInstance(type)!;
                byte[] saved;

                try
                {
                    using var stream = new MemoryStream();
                    adapter.Save(RichlyFormattedDocument(), stream);
                    saved = stream.ToArray();
                }
                catch (Exception exception) when (exception is NotSupportedException or NotImplementedException)
                {
                    cannotSave.Add($"{type.Name} ({exception.GetType().Name})");
                    continue;
                }

                offenders.AddRange(CommaDecimalAttributeValues(saved).Select(o => $"{type.Name} -> {o}"));
            }

            offenders.Should().BeEmpty(
                "a comma decimal makes the document invalid for readers of the format, and only ever " +
                "on machines running this locale:\n" + string.Join("\n", offenders) +
                "\n(adapters that cannot save: " +
                (cannotSave.Count == 0 ? "none" : string.Join(", ", cannotSave)) + ")");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// The positive control: a fractional font size must actually survive a .docx round trip under a
    /// foreign locale. Without this, an adapter that dropped the value entirely would satisfy the
    /// scan above by writing nothing at all.
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void AFractionalFontSizeSurvivesADocxRoundTrip(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);

        try
        {
            using var stream = new MemoryStream();
            DocxWriter.Write(RichlyFormattedDocument(), stream);
            stream.Position = 0;

            var reloaded = DocxReader.Read(stream);
            reloaded.Paragraphs.First().Runs.First().Formatting!.FontSizePt
                .Should().Be(10.5, "half-point font sizes must survive whatever locale the machine runs");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
