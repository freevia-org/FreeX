using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Free.Shared.Pdf;
using Free.Shared.Pdf.Skia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Pdf;
using FreeW.Core.IO;
using FreeW.Core.Model;
using SkiaSharp;

namespace FreeW.App.Avalonia.Tests;

public sealed class FreeWAvaloniaPdfFontSourceTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData(PdfFontFace.Regular, "f6418f708baede9789daef5d458c0f53d2a888af9820e8062934e504fedc6595")]
    [InlineData(PdfFontFace.Bold, "bb5d20f79b82599ec72983597437373a80f2d2085fa91fc144fd74e876a594db")]
    [InlineData(PdfFontFace.Italic, "0b019225e58d702bfedcbd35c21696769f8ee115cb6343f84c2f240312450d1c")]
    [InlineData(PdfFontFace.BoldItalic, "b32928186c119599e03ca6a1ffc680fdcb7fac95772f4b95d989cf6cd3861517")]
    public Task SelectedCarlitoOpensFreshUnmodifiedProductFontStreams(PdfFontFace face, string sha256) => Session.Dispatch(() =>
    {
        using var first = FreeWAvaloniaPdfFontSource.OpenResolvedFamily(" CALIBRI ", face, "carlito");
        using var second = FreeWAvaloniaPdfFontSource.OpenResolvedFamily("Calibri", face, "Carlito");

        first.Should().NotBeNull();
        second.Should().NotBeSameAs(first);
        first!.Position.Should().Be(0);
        Convert.ToHexStringLower(SHA256.HashData(first)).Should().Be(sha256);
        Convert.ToHexStringLower(SHA256.HashData(second!)).Should().Be(sha256);
    }, CancellationToken.None);

    [Theory]
    [InlineData(null, "Carlito")]
    [InlineData("Arial", "Carlito")]
    [InlineData("Calibri, Arial", "Carlito")]
    [InlineData("Calibri", "Calibri")]
    [InlineData("calibri", "Arial")]
    [InlineData("Calibri", null)]
    [InlineData("Other family", "Other family")]
    public void InstalledCalibriAndUnrelatedFontChoicesKeepSystemResolution(string? authored, string? selected) =>
        FreeWAvaloniaPdfFontSource.OpenResolvedFamily(authored, PdfFontFace.BoldItalic, selected).Should().BeNull();

    [Fact]
    public void MissingFontServicesDeclineWithoutTryingToResolveOrOpenAFont()
    {
        var resolved = false;
        using var stream = FreeWAvaloniaPdfFontSource.OpenCore("Calibri", PdfFontFace.Regular,
            () => throw new InvalidOperationException("Unable to locate font services."),
            _ => { resolved = true; throw new InvalidOperationException("Face lookup must not run."); });

        stream.Should().BeNull();
        resolved.Should().BeFalse();
    }

    [Fact]
    public void ActualFaceResolutionErrorsAreOutsideTheServiceAvailabilityFallback()
    {
        var error = new InvalidOperationException("The selected font could not be resolved.");
        Action open = () => FreeWAvaloniaPdfFontSource.OpenCore("Calibri", PdfFontFace.Regular,
            () => { }, _ => throw error);

        open.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
    }

    [Fact]
    public void NonAvailabilityErrorsDuringServiceAcquisitionAreNotSilentlyIgnored()
    {
        var error = new IOException("Font services failed.");
        Action open = () => FreeWAvaloniaPdfFontSource.OpenCore("Calibri", PdfFontFace.Regular,
            () => throw error, _ => "Carlito");

        open.Should().Throw<IOException>().Which.Should().BeSameAs(error);
    }

    [Fact]
    public Task FaceLookupUsesTheSameCompositeFamilyWeightAndStyleAsTheEditor() => Session.Dispatch(() =>
    {
        using var font = FreeWAvaloniaPdfFontSource.OpenCore("Calibri", PdfFontFace.BoldItalic, () => { }, typeface =>
        {
            typeface.FontFamily.FamilyNames.Should().Equal("Calibri", "Carlito");
            typeface.Weight.Should().Be(FontWeight.Bold);
            typeface.Style.Should().Be(FontStyle.Italic);
            return "Carlito";
        });

        font.Should().NotBeNull();
    }, CancellationToken.None);

    [Fact]
    public Task AlreadyResolvedCarlitoProducesFourRealEmbeddedSkiaFontsAndUnicodeMappings() => Session.Dispatch(() =>
    {
        // Headless initializes resource loading only. This test draws with real Skia using the
        // selected product font bytes; actual native font-manager selection is a separate probe.
        var document = Content();
        var bytes = SkiaPdfWriter.WriteToBytes(document, null,
            (family, face) => FreeWAvaloniaPdfFontSource.OpenResolvedFamily(family, face, "Carlito"));
        var pdf = Encoding.Latin1.GetString(bytes);

        foreach (var name in new[] { "Carlito-Regular", "Carlito-Bold", "Carlito-Italic", "Carlito-BoldItalic" })
            Regex.IsMatch(pdf, @"/BaseFont\s*/(?:[A-Z]{6}\+)?" + Regex.Escape(name) + @"\b").Should().BeTrue(name);
        Regex.Matches(pdf, @"/FontFile2\b").Should().HaveCount(4);
        Regex.Matches(pdf, @"/ToUnicode\b").Should().HaveCount(4);
        pdf.Should().NotContain("/Subtype /Image", "font matching must keep vector PDF text");
        document.Pages.Single().Ops.OfType<PdfText>().Should().OnlyContain(text => text.FontFamily == "Calibri");
    }, CancellationToken.None);

    [Fact]
    public Task PngFallbackCanUseTheSameFourResolvedProductFaces() => Session.Dispatch(() =>
    {
        var requested = new List<PdfFontFace>();
        var pages = SkiaPdfWriter.RenderPagesToPng(Content(), 96, (family, face) =>
        {
            requested.Add(face);
            return FreeWAvaloniaPdfFontSource.OpenResolvedFamily(family, face, "Carlito");
        });

        requested.Should().HaveCount(4).And.OnlyHaveUniqueItems();
        using var image = SKBitmap.Decode(pages.Single());
        image.Pixels.Should().Contain(pixel => pixel.Red < 128 && pixel.Green < 128 && pixel.Blue < 128);
    }, CancellationToken.None);

    [Fact]
    public Task PublicSaveLeavesAuthoredCalibriAndTheDocxFontNamesUnchanged() => Session.Dispatch(() =>
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var face in Enum.GetValues<PdfFontFace>())
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Font name survives export.", RunFormatting.Default with
            {
                FontFamily = "Calibri", Bold = face is PdfFontFace.Bold or PdfFontFace.BoldItalic,
                Italic = face is PdfFontFace.Italic or PdfFontFace.BoldItalic,
            }));
            document.Blocks.Add(paragraph);
        }
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 1400));
        using var pdf = new MemoryStream();

        FreeWAvaloniaPdfExport.Save(view, pdf).Backend.Should().Be(PdfExportBackend.Skia);

        document.Paragraphs.SelectMany(paragraph => paragraph.Runs).Should().OnlyContain(run => run.Formatting.FontFamily == "Calibri");
        using var docx = new MemoryStream();
        DocxWriter.Write(document, docx);
        docx.Position = 0;
        DocxReader.Read(docx).Paragraphs.SelectMany(paragraph => paragraph.Runs).Should()
            .OnlyContain(run => run.Formatting.FontFamily == "Calibri");
    }, CancellationToken.None);

    private static PdfContentDocument Content() => new([new PdfContentPage(400, 160,
        Enum.GetValues<PdfFontFace>().Select((face, index) => (PdfDrawOp)new PdfText(
            10, 130 - index * 30, 16, face, PdfColor.Black, "AVATAR office 0123456789", "Calibri")).ToArray())]);
}
