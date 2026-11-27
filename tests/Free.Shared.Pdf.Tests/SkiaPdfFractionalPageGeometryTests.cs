using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Free.Shared.Pdf.Skia;
using Xunit.Abstractions;

namespace Free.Shared.Pdf.Tests;

public sealed class SkiaPdfFractionalPageGeometryTests
{
    public SkiaPdfFractionalPageGeometryTests(ITestOutputHelper output)
    {
        var assembly = typeof(SkiaPdfWriter).Assembly;
        output.WriteLine("Loaded {0}; SHA256={1}; version={2}", assembly.Location,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(),
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }

    [Theory]
    [InlineData(false, "readable", false)]
    [InlineData(false, "write-only", false)]
    [InlineData(true, "readable", false)]
    [InlineData(true, "write-only", false)]
    [InlineData(false, "readable", true)]
    [InlineData(false, "write-only", true)]
    [InlineData(false, "non-seekable", true)]
    [InlineData(true, "readable", true)]
    [InlineData(true, "write-only", true)]
    [InlineData(true, "non-seekable", true)]
    public void ExportRetainsFractionalMediaBoxesAlongsideIntegerPages(
        bool rasterized, string streamKind, bool withCatalog)
    {
        var pages = Enumerable.Range(0, 12).Select(index =>
            new PdfContentPage(index == 10 ? 595.3 : 100 + index,
                index == 10 ? 841.9 : index == 1 ? 80.7 : 80, [])).ToArray();
        var properties = withCatalog ? new PdfDocumentProperties(Title: "Fractional pages")
        {
            Language = "en-GB",
            ViewerSettings = new(PdfViewerPageLayout.TwoColumnLeft, PdfViewerOpenMode.Normal),
            Outlines = [new PdfOutlineEntry("Target page", 10)],
        } : null;
        using var output = new OutputStream(streamKind);

        Export(new PdfContentDocument(pages, properties), output, rasterized).Should().Be(12);

        var pdf = Encoding.Latin1.GetString(output.Bytes);
        var references = ReadPages(pdf);
        for (var index = 0; index < pages.Length; index++)
        {
            var page = ReadObject(pdf, references[index]);
            AssertNumbers(ReadNumbers(page, @"/MediaBox\s*\[([^\]]+)\]"),
                0, 0, pages[index].WidthPoints, pages[index].HeightPoints);
            if (index is 1 or 10)
                AssertUpdatedCrossReference(pdf, references[index]);
            else
                ObjectOccurrences(pdf, references[index]).Should().Be(1,
                    "integer pages need no geometry revision");
        }
        Regex.Matches(pdf, @"\ntrailer\n").Should().HaveCount(2,
            "page geometry and optional catalog fields must share one incremental update");
        if (withCatalog)
        {
            var catalog = ReadObject(pdf, Reference(ReadTrailer(pdf), "Root"));
            catalog.Should().Contain("/PageLayout /TwoColumnLeft").And.Contain("/Lang <FEFF0065006E002D00470042>");
            var outlineRoot = ReadObject(pdf, Reference(catalog, "Outlines"));
            var first = ReadObject(pdf, Reference(outlineRoot, "First"));
            first.Should().Contain($"/Dest [{references[10]} 0 R /XYZ null null null]");
            ReadObject(pdf, Reference(ReadTrailer(pdf), "Info")).Should().Contain("/Title (Fractional pages)");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FractionalPagesKeepContentLinksAndCrossPageDestinationsAtAuthoredCoordinates(bool rasterized)
    {
        var document = new PdfContentDocument([
            new PdfContentPage(100.3, 80.7, [new PdfFillRect(13, 17, 7, 11, PdfColor.Black)],
                [new PdfLinkOverlay(11, 9, 29, 13, "https://example.invalid/page"),
                 new PdfLinkOverlay(50, 12, 20, 10, null, DestinationName: "Target")]),
            new PdfContentPage(101.25, 47.2, [], NamedDestinations: [new PdfNamedDestination("Target", 12, 10)]),
        ]);
        using var output = new MemoryStream();
        Export(document, output, rasterized);
        var pdf = Encoding.Latin1.GetString(output.ToArray());
        var pages = ReadPages(pdf);
        var first = ReadObject(pdf, pages[0]);
        var content = ReadContent(pdf, first);
        var marks = ReadPaintedGeometry(content);
        if (rasterized)
            AssertBounds(marks.Single(mark => mark.Kind == "Do"), 0, 0, 100.3, 80.7);
        else
            AssertBounds(marks.Single(mark => mark.Kind == "re" && Math.Abs(mark.Width - 7) < 0.001), 13, 17, 7, 11);
        var annotations = Regex.Matches(Regex.Match(first, @"/Annots\s*\[([^\]]+)\]").Groups[1].Value, @"(\d+) 0 R")
            .Select(match => ReadObject(pdf, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))).ToArray();
        AssertNumbers(ReadNumbers(annotations.Single(item => item.Contains("/URI", StringComparison.Ordinal)), @"/Rect\s*\[([^\]]+)\]"),
            11, 58.7, 40, 71.7);
        AssertNumbers(ReadNumbers(annotations.Single(item => item.Contains("/Dest", StringComparison.Ordinal)), @"/Rect\s*\[([^\]]+)\]"),
            50, 58.7, 70, 68.7);
        var catalog = ReadObject(pdf, Reference(ReadTrailer(pdf), "Root"));
        var destinations = ReadObject(pdf, Reference(catalog, "Dests"));
        var target = Regex.Match(destinations, @"/Target\s*\[(\d+) 0 R /XYZ ([^\]]+)\]");
        target.Success.Should().BeTrue();
        int.Parse(target.Groups[1].Value, CultureInfo.InvariantCulture).Should().Be(pages[1]);
        AssertNumbers(ParseNumbers(target.Groups[2].Value), 12, 37.2, 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExportSupportsNonSeekableOutputWithoutProperties(bool rasterized, bool fractional)
    {
        using var output = new OutputStream("non-seekable");
        var width = fractional ? 100.3 : 100;
        var height = fractional ? 80.7 : 80;
        Export(new PdfContentDocument([new PdfContentPage(width, height, [])]), output, rasterized).Should().Be(1);
        var pdf = Encoding.Latin1.GetString(output.Bytes);
        AssertNumbers(ReadNumbers(ReadObject(pdf, ReadPages(pdf).Single()), @"/MediaBox\s*\[([^\]]+)\]"),
            0, 0, width, height);
    }

    [Fact]
    public void VectorPageRetainsMarksWithinTheFractionalRightEdgeAndClipsToTheAuthoredPage()
    {
        var document = new PdfContentDocument([new PdfContentPage(100.3, 80.7,
            [new PdfFillRect(100.15, 17, 0.1, 11, PdfColor.Black)])]);
        var pdf = Encoding.Latin1.GetString(SkiaPdfWriter.WriteToBytes(document));
        var content = ReadContent(pdf, ReadObject(pdf, ReadPages(pdf).Single()));
        var rectangles = ReadPaintedGeometry(content).Where(mark => mark.Kind == "re").ToArray();
        AssertBounds(rectangles.Single(mark => Math.Abs(mark.Width - 0.1) < 0.001), 100.15, 17, 0.1, 11);
        rectangles.Should().Contain(mark => Math.Abs(mark.X) < 0.0001 && Math.Abs(mark.Y) < 0.0001
            && Math.Abs(mark.Width - 100.3) < 0.0001 && Math.Abs(mark.Height - 80.7) < 0.0001);
        content.Should().MatchRegex(@"\bW\*?\s+n\b");
    }

    [Fact]
    public void GeometryRevisionPreservesTheCompletePageDictionaryWithManyAnnotations()
    {
        var links = Enumerable.Range(0, 512).Select(index =>
            new PdfLinkOverlay(10, 10, 20, 10, $"https://example.invalid/{index}")).ToArray();
        var document = new PdfContentDocument([new PdfContentPage(100.3, 80.7, [], links)]);
        var pdf = Encoding.Latin1.GetString(SkiaPdfWriter.WriteToBytes(document));
        var number = ReadPages(pdf).Single();
        var page = ReadObject(pdf, number);
        AssertNumbers(ReadNumbers(page, @"/MediaBox\s*\[([^\]]+)\]"), 0, 0, 100.3, 80.7);
        var annotations = Regex.Matches(Regex.Match(page, @"/Annots\s*\[([^\]]+)\]").Groups[1].Value, @"(\d+) 0 R");
        annotations.Should().HaveCount(512);
        foreach (Match reference in annotations)
            ReadObject(pdf, int.Parse(reference.Groups[1].Value, CultureInfo.InvariantCulture)).Should().Contain("/Subtype /Link");
        AssertUpdatedCrossReference(pdf, number);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntegerOnlyPagesKeepTheirOriginalPageObjectsAndCoordinateSystem(bool rasterized)
    {
        var document = new PdfContentDocument([new PdfContentPage(100, 80,
            [new PdfFillRect(13, 17, 7, 11, PdfColor.Black)])]);
        using var output = new MemoryStream();
        Export(document, output, rasterized);
        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Regex.Matches(pdf, @"\ntrailer\n").Should().ContainSingle();
        var number = ReadPages(pdf).Single();
        ObjectOccurrences(pdf, number).Should().Be(1);
        var page = ReadObject(pdf, number);
        AssertNumbers(ReadNumbers(page, @"/MediaBox\s*\[([^\]]+)\]"), 0, 0, 100, 80);
        var marks = ReadPaintedGeometry(ReadContent(pdf, page));
        AssertBounds(rasterized ? marks.Single(mark => mark.Kind == "Do")
            : marks.Single(mark => mark.Kind == "re" && Math.Abs(mark.Width - 7) < 0.001),
            rasterized ? 0 : 13, rasterized ? 0 : 17, rasterized ? 100 : 7, rasterized ? 80 : 11);
    }

    private static int Export(PdfContentDocument document, Stream output, bool rasterized) => rasterized
        ? SkiaPdfWriter.WriteRasterized(document, output, dpi: 72, includeSelectableText: false)
        : SkiaPdfWriter.Write(document, output);

    private static string ReadTrailer(string pdf) => Regex.Matches(pdf,
        @"\ntrailer\n(?<dictionary><<.*?)>>\nstartxref\n", RegexOptions.Singleline).Last().Groups["dictionary"].Value;

    private static int Reference(string dictionary, string key)
    {
        var match = Regex.Match(dictionary, $@"/{key}\s+(\d+) 0 R");
        match.Success.Should().BeTrue($"the dictionary should contain /{key}");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static string ReadObject(string pdf, int number) => Regex.Matches(pdf,
        FormattableString.Invariant($@"(?m)^{number} 0 obj\n([\s\S]*?)\nendobj\n")).Last().Groups[1].Value;

    private static int ObjectOccurrences(string pdf, int number) =>
        Regex.Matches(pdf, FormattableString.Invariant($@"(?m)^{number} 0 obj\n")).Count;

    private static void AssertUpdatedCrossReference(string pdf, int number)
    {
        var offset = pdf.LastIndexOf(FormattableString.Invariant($"\n{number} 0 obj\n"), StringComparison.Ordinal) + 1;
        var xref = int.Parse(Regex.Matches(pdf, @"\nstartxref\n(\d+)\n%%EOF").Last().Groups[1].Value, CultureInfo.InvariantCulture);
        pdf[xref..].Should().Contain(FormattableString.Invariant($"\n{offset:D10} 00000 n \n"));
    }

    private static int[] ReadPages(string pdf)
    {
        var catalog = ReadObject(pdf, Reference(ReadTrailer(pdf), "Root"));
        var pages = new List<int>();
        void Visit(int number)
        {
            var item = ReadObject(pdf, number);
            if (Regex.IsMatch(item, @"/Type /Page\b")) { pages.Add(number); return; }
            foreach (Match match in Regex.Matches(Regex.Match(item, @"/Kids\s*\[([^\]]+)\]").Groups[1].Value, @"(\d+) 0 R"))
                Visit(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        Visit(Reference(catalog, "Pages"));
        return [.. pages];
    }

    private static string ReadContent(string pdf, string page)
    {
        var item = ReadObject(pdf, Reference(page, "Contents"));
        var start = item.IndexOf("stream\n", StringComparison.Ordinal) + "stream\n".Length;
        var length = int.Parse(Regex.Match(item, @"/Length (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        var bytes = Encoding.Latin1.GetBytes(item.Substring(start, length));
        if (!item[..start].Contains("/FlateDecode", StringComparison.Ordinal)) return Encoding.ASCII.GetString(bytes);
        using var compressed = new MemoryStream(bytes);
        using var decoded = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        decoded.CopyTo(output);
        return Encoding.ASCII.GetString(output.ToArray());
    }

    private static double[] ReadNumbers(string text, string pattern)
    {
        var match = Regex.Match(text, pattern);
        match.Success.Should().BeTrue();
        return ParseNumbers(match.Groups[1].Value);
    }

    private static double[] ParseNumbers(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();

    private static void AssertNumbers(double[] actual, params double[] expected)
    {
        actual.Should().HaveCount(expected.Length);
        for (var index = 0; index < expected.Length; index++) actual[index].Should().BeApproximately(expected[index], 0.0001);
    }

    private static void AssertBounds(Mark mark, double x, double y, double width, double height) =>
        AssertNumbers([mark.X, mark.Y, mark.Width, mark.Height], x, y, width, height);

    private sealed record Mark(string Kind, double X, double Y, double Width, double Height);
    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static Matrix Identity => new(1, 0, 0, 1, 0, 0);
        public Matrix Concat(double[] n) => new(A * n[0] + C * n[1], B * n[0] + D * n[1],
            A * n[2] + C * n[3], B * n[2] + D * n[3], A * n[4] + C * n[5] + E, B * n[4] + D * n[5] + F);
        public (double X, double Y) Map(double x, double y) => (A * x + C * y + E, B * x + D * y + F);
    }

    // Interpret only the affine/rectangle/image operators in these deliberately text-free pages.
    // Assertions use final physical PDF coordinates, independently of Skia's chosen stream factoring.
    private static Mark[] ReadPaintedGeometry(string content)
    {
        var matrix = Matrix.Identity;
        var saved = new Stack<Matrix>();
        var operands = new List<double>();
        var marks = new List<Mark>();
        foreach (Match token in Regex.Matches(content, @"[-+]?(?:\d*\.\d+|\d+)|/[^\s]+|[A-Za-z*]+"))
        {
            if (double.TryParse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            { operands.Add(number); continue; }
            if (token.Value.StartsWith('/')) continue;
            switch (token.Value)
            {
                case "q": saved.Push(matrix); break;
                case "Q": matrix = saved.Pop(); break;
                case "cm": matrix = matrix.Concat(operands.ToArray()); break;
                case "re": Add("re", operands[0], operands[1], operands[2], operands[3]); break;
                case "Do": Add("Do", 0, 0, 1, 1); break;
            }
            operands.Clear();
        }
        return [.. marks];
        void Add(string kind, double x, double y, double w, double h)
        {
            var points = new[] { matrix.Map(x, y), matrix.Map(x + w, y), matrix.Map(x, y + h), matrix.Map(x + w, y + h) };
            var left = points.Min(point => point.X); var bottom = points.Min(point => point.Y);
            marks.Add(new(kind, left, bottom, points.Max(point => point.X) - left, points.Max(point => point.Y) - bottom));
        }
    }

    private sealed class OutputStream(string kind) : Stream
    {
        private readonly MemoryStream _inner = new();
        public byte[] Bytes => _inner.ToArray();
        public override bool CanRead => kind == "readable";
        public override bool CanSeek => kind != "non-seekable";
        public override bool CanWrite => true;
        public override long Length => CanSeek ? _inner.Length : throw new NotSupportedException();
        public override long Position { get => CanSeek ? _inner.Position : throw new NotSupportedException();
            set { if (!CanSeek) throw new NotSupportedException(); _inner.Position = value; } }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => CanRead
            ? _inner.Read(buffer, offset, count) : throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => CanSeek
            ? _inner.Seek(offset, origin) : throw new NotSupportedException();
        public override void SetLength(long value) { if (!CanSeek) throw new NotSupportedException(); _inner.SetLength(value); }
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
