using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Free.Shared.Pdf.Skia;
using SkiaSharp;

namespace Free.Shared.Pdf.Tests;

public sealed class SkiaPdfFontSourceTests
{
    [Fact]
    public void SuppliedFontOverridesAnExistingSystemFamilyAndKeepsVectorText()
    {
        var family = SKTypeface.Default.FamilyName;
        var source = Font(PdfFontFace.Bold);
        var calls = 0;
        var document = Document(family, PdfFontFace.Bold);

        var bytes = SkiaPdfWriter.WriteToBytes(document, null, (requested, face) =>
        {
            requested.Should().Be(family);
            face.Should().Be(PdfFontFace.Bold);
            calls++;
            return source;
        });

        calls.Should().Be(1);
        source.DisposeCount.Should().Be(1);
        AssertEmbedded(bytes, "Carlito-Bold");
        Encoding.Latin1.GetString(bytes).Should().NotContain("/Subtype /Image");
    }

    [Fact]
    public void AllFourFontsAreCachedForTheWholeExportAndReopenedForTheNextExport()
    {
        var streams = new List<TrackingStream>();
        var requests = new List<PdfFontFace>();
        var operations = Enum.GetValues<PdfFontFace>().Select((face, index) =>
            (PdfDrawOp)new PdfText(10, 90 - index * 20, 12, face, PdfColor.Black, "office AVATAR", "Source family")).ToArray();
        var page = new PdfContentPage(240, 120, operations.Concat(operations).ToArray());
        var document = new PdfContentDocument([page, page]);
        Stream Resolve(string? _, PdfFontFace face)
        {
            requests.Add(face);
            var stream = Font(face);
            streams.Add(stream);
            return stream;
        }

        var bytes = SkiaPdfWriter.WriteToBytes(document, null, Resolve);
        requests.Should().HaveCount(4);
        requests.Should().OnlyHaveUniqueItems();
        foreach (var face in Enum.GetValues<PdfFontFace>()) AssertEmbedded(bytes, "Carlito-" + Variant(face));
        Regex.Matches(Encoding.Latin1.GetString(bytes), @"/FontFile2\b").Should().HaveCount(4);
        Regex.Matches(Encoding.Latin1.GetString(bytes), @"/ToUnicode\b").Should().HaveCount(4);
        streams.Should().OnlyContain(stream => stream.DisposeCount == 1);

        SkiaPdfWriter.WriteToBytes(document, null, Resolve).Should().NotBeEmpty();
        requests.Should().HaveCount(8, "font sources belong to an export, never a process-wide cache");
        streams.Should().OnlyContain(stream => stream.DisposeCount == 1);
    }

    [Fact]
    public void NullSourceAndDeclinedRequestsKeepTheLegacyTypefaceResolution()
    {
        var document = Document("Example missing system family", PdfFontFace.Italic);
        var legacy = SkiaPdfWriter.WriteToBytes(document);
        var requests = 0;
        var declined = SkiaPdfWriter.WriteToBytes(document, null, (_, _) => { requests++; return null; });
        var unspecified = SkiaPdfWriter.WriteToBytes(document, null, fontSource: null);

        FontNames(declined).Should().Equal(FontNames(legacy));
        FontNames(unspecified).Should().Equal(FontNames(legacy));
        requests.Should().Be(1);
        SkiaPdfWriter.RenderPagesToPng(document, 72, null).Single()
            .Should().Equal(SkiaPdfWriter.RenderPagesToPng(document, 72).Single());
    }

    [Fact]
    public void ExistingOverloadsStillBindToTheirOriginalMethodGroupSignatures()
    {
        Func<PdfContentDocument, Stream, ICollection<string>?, int> write = SkiaPdfWriter.Write;
        Func<PdfContentDocument, ICollection<string>?, byte[]> bytes = SkiaPdfWriter.WriteToBytes;
        Func<PdfContentDocument, int, IReadOnlyList<byte[]>> png = SkiaPdfWriter.RenderPagesToPng;
        Func<PdfContentDocument, Stream, int, bool, ICollection<string>?, int> raster = SkiaPdfWriter.WriteRasterized;
        Func<PdfContentDocument, string, byte[]> fallback = SkiaPdfWriter.WriteToBytesWithPortableFallback;
        var document = Document(null, PdfFontFace.Regular);
        using var stream = new MemoryStream();

        write(document, stream, null).Should().Be(1);
        bytes(document, null).Should().NotBeEmpty();
        png(document, 72).Should().ContainSingle();
        raster(document, stream, 72, true, null).Should().Be(1);
        fallback(document, "Example PDF").Should().NotBeEmpty();
        // A null third argument remains unambiguous at old call sites.
        SkiaPdfWriter.Write(document, stream, null).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidSuppliedDataFailsWithFontContextAndDisposesTheStream(bool empty)
    {
        var source = new TrackingStream(new MemoryStream(empty ? [] : Encoding.UTF8.GetBytes("not a font")));
        Action write = () => SkiaPdfWriter.WriteToBytes(Document("Requested face", PdfFontFace.Italic), null, (_, _) => source);

        var exception = write.Should().Throw<InvalidDataException>()
            .WithMessage("*'Requested face' (Italic)*").Which;

        exception.InnerException.Should().NotBeNull();
        SkiaPdfAvailabilityHelper.IsSkiaUnavailable(exception).Should().BeFalse();
        source.DisposeCount.Should().Be(1);
    }

    [Theory]
    [InlineData("unreadable")]
    [InlineData("read")]
    [InlineData("dispose")]
    public void StreamFailuresAreOwnedAndReportedAsFontFailures(string failure)
    {
        var source = Font(PdfFontFace.Regular);
        source.Unreadable = failure == "unreadable";
        source.FailRead = failure == "read";
        source.FailDispose = failure == "dispose";
        Action write = () => SkiaPdfWriter.WriteToBytes(Document("Requested face", PdfFontFace.Regular), null, (_, _) => source);

        write.Should().Throw<InvalidDataException>().WithMessage("*'Requested face' (Regular)*");
        source.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void ResolverFailureDoesNotSilentlySelectThePortableBackend()
    {
        var fallback = false;
        using var output = new MemoryStream();
        var error = new PlatformNotSupportedException("font provider failed");
        Action write = () => PdfBackendFallbackExecutor.Execute(output,
            target => SkiaPdfWriter.Write(Document("Requested face", PdfFontFace.Bold), target, null, (_, _) => throw error),
            _ => { fallback = true; return 0; });

        write.Should().Throw<InvalidDataException>().WithMessage("*'Requested face' (Bold)*")
            .Which.InnerException.Should().BeSameAs(error);
        fallback.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CatalogUpdatesPreserveTheSourceForSeekableAndBufferedOutput(bool nonSeekable)
    {
        var source = Font(PdfFontFace.Regular);
        var document = Document("Requested face", PdfFontFace.Regular) with
        {
            Properties = new PdfDocumentProperties(Title: "Font source") { Language = "en-US" },
        };
        using var storage = new MemoryStream();
        using Stream output = nonSeekable ? new WriteOnlyStream(storage) : storage;

        SkiaPdfWriter.Write(document, output, null, (_, _) => source).Should().Be(1);

        source.DisposeCount.Should().Be(1);
        var bytes = storage.ToArray();
        AssertEmbedded(bytes, "Carlito-Regular");
        ReadCatalogLanguage(bytes).Should().Be("en-US");
    }

    [Fact]
    public void PngRenderingUsesAndDisposesTheSuppliedFont()
    {
        var source = Font(PdfFontFace.BoldItalic);
        var requests = 0;
        var pages = SkiaPdfWriter.RenderPagesToPng(Document("Requested face", PdfFontFace.BoldItalic), 144, (_, face) =>
        {
            face.Should().Be(PdfFontFace.BoldItalic);
            requests++;
            return source;
        });

        using var bitmap = SKBitmap.Decode(pages.Single());
        bitmap.Width.Should().Be(480);
        bitmap.Height.Should().Be(240);
        bitmap.Pixels.Should().Contain(pixel => pixel.Red < 128 && pixel.Green < 128 && pixel.Blue < 128);
        requests.Should().Be(1);
        source.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void InvalidPngSourceCannotFallBackToASystemFont()
    {
        var source = new TrackingStream(new MemoryStream([1, 2, 3]));
        Action render = () => SkiaPdfWriter.RenderPagesToPng(Document("Requested face", PdfFontFace.Regular), 96, (_, _) => source);

        render.Should().Throw<InvalidDataException>().WithMessage("*'Requested face' (Regular)*");
        source.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void RasterAndSelectableOverlayShareOneSuppliedTypeface()
    {
        var source = Font(PdfFontFace.Italic);
        var requests = 0;
        using var output = new MemoryStream();
        SkiaPdfWriter.WriteRasterized(Document("Requested face", PdfFontFace.Italic), output, 72, true, null,
            (_, _) => { requests++; return source; }).Should().Be(1);

        AssertEmbedded(output.ToArray(), "Carlito-Italic");
        Encoding.Latin1.GetString(output.ToArray()).Should().Contain("/Subtype /Image");
        requests.Should().Be(1);
        source.DisposeCount.Should().Be(1);
    }

    [Theory]
    [InlineData("Regular", "f6418f708baede9789daef5d458c0f53d2a888af9820e8062934e504fedc6595")]
    [InlineData("Bold", "bb5d20f79b82599ec72983597437373a80f2d2085fa91fc144fd74e876a594db")]
    [InlineData("Italic", "0b019225e58d702bfedcbd35c21696769f8ee115cb6343f84c2f240312450d1c")]
    [InlineData("BoldItalic", "b32928186c119599e03ca6a1ffc680fdcb7fac95772f4b95d989cf6cd3861517")]
    public void LinkedFontFixturesKeepTheProductBytesAndLicense(string variant, string sha256)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts");
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, $"Carlito-{variant}.ttf"))))
            .Should().Be(sha256);
        File.ReadAllText(Path.Combine(root, "Carlito-OFL.txt")).Should().Contain("SIL OPEN FONT LICENSE");
        File.Exists(Path.Combine(root, "Carlito-UPSTREAM.json")).Should().BeTrue();
    }

    private static PdfContentDocument Document(string? family, PdfFontFace face) =>
        new([new PdfContentPage(240, 120, [new PdfText(10, 80, 12, face, PdfColor.Black, "office AVATAR", family)])]);

    private static TrackingStream Font(PdfFontFace face) => new(File.OpenRead(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "Fonts", $"Carlito-{Variant(face)}.ttf")));

    private static string Variant(PdfFontFace face) => face switch
    {
        PdfFontFace.Bold => "Bold", PdfFontFace.Italic => "Italic", PdfFontFace.BoldItalic => "BoldItalic", _ => "Regular",
    };

    private static void AssertEmbedded(byte[] bytes, string postScriptName)
    {
        var pdf = Encoding.Latin1.GetString(bytes);
        Regex.IsMatch(pdf, @"/BaseFont\s*/(?:[A-Z]{6}\+)?" + Regex.Escape(postScriptName) + @"\b").Should().BeTrue();
        pdf.Should().Contain("/FontFile2").And.Contain("/ToUnicode");
    }

    private static string[] FontNames(byte[] bytes) => Regex.Matches(Encoding.Latin1.GetString(bytes),
        @"/BaseFont\s*/(?:[A-Z]{6}\+)?([A-Za-z0-9_-]+)").Cast<Match>()
        .Select(match => match.Groups[1].Value).Distinct().Order().ToArray();

    private static string ReadCatalogLanguage(byte[] bytes)
    {
        var language = Regex.Matches(Encoding.Latin1.GetString(bytes),
            @"/Lang\s*(?:\((?<literal>[^()]*)\)|<(?<hex>[0-9A-Fa-f\s]+)>)").Cast<Match>().Last();
        if (language.Groups["literal"].Success)
            return language.Groups["literal"].Value;
        var text = Convert.FromHexString(Regex.Replace(language.Groups["hex"].Value, @"\s+", ""));
        if (text.Length >= 2 && text[0] == 0xFE && text[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(text.AsSpan(2));
        return Encoding.Latin1.GetString(text);
    }

    private sealed class TrackingStream(Stream inner) : Stream
    {
        public int DisposeCount { get; private set; }
        public bool Unreadable { get; set; }
        public bool FailRead { get; set; }
        public bool FailDispose { get; set; }
        public override bool CanRead => !Unreadable && inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) =>
            FailRead ? throw new IOException("font read failed") : inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => FailRead ? throw new IOException("font read failed") : inner.Read(buffer);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
                inner.Dispose();
                if (FailDispose) throw new IOException("font dispose failed");
            }
            base.Dispose(disposing);
        }
    }

    private sealed class WriteOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
