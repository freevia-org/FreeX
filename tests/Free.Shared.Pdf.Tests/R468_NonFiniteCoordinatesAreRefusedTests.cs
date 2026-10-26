using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Free.Shared.Pdf;
using Xunit;

namespace Free.Shared.Pdf.Tests;

/// <summary>
/// r468: a non-finite coordinate must refuse the export, not produce a file that cannot be opened.
///
/// <para>A NaN or Infinity used to be formatted straight into the content stream, giving operators
/// like <c>NaN NaN 10 10 re f</c>. Neither is a PDF number, so the page is unparseable -- while the
/// file still carries a correct <c>%PDF-</c> header and <c>%%EOF</c> trailer and looks entirely
/// healthy. An export that reports success and writes a file no reader will open is the worst shape
/// this review keeps meeting: damage that looks deliberate.</para>
///
/// <para>No production path to a non-finite coordinate was demonstrated -- FreeX's pagination already
/// rejects non-finite scale fractions. The guard is justified differently: this is a SHARED writer
/// behind three apps whose contract is "hand me laid-out ops, get a valid PDF", and it already
/// refuses two other preconditions (a writable stream, at least one page) with clear messages. A
/// third is consistent, costs one check per number at a single choke point every number passes
/// through, and turns an undiagnosable corrupt file into an actionable error.</para>
/// </summary>
public sealed class R468_NonFiniteCoordinatesAreRefusedTests
{
    private static readonly PdfColor Black = new(0, 0, 0);

    private static PdfContentDocument DocumentWith(PdfDrawOp op) =>
        new([new PdfContentPage(612, 792, [op])]);

    /// <summary>
    /// Decompresses the content streams so the OPERATORS a reader parses are inspected, not the raw
    /// file bytes. A first attempt searched the whole file for "NaN" and reported a hit on a
    /// known-good control, because compressed bytes contain arbitrary sequences -- the control is
    /// what exposed the bad detector, which is why one is kept here.
    /// </summary>
    private static string ContentStreamText(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var builder = new StringBuilder();
        var index = 0;

        while (true)
        {
            var start = text.IndexOf("stream", index, StringComparison.Ordinal);
            if (start < 0) break;

            var end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) break;

            var bodyStart = start + "stream".Length;
            while (bodyStart < end && (text[bodyStart] == '\r' || text[bodyStart] == '\n')) bodyStart++;

            var raw = Encoding.Latin1.GetBytes(text[bodyStart..end]);

            try
            {
                using var input = new MemoryStream(raw);
                using var inflate = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                inflate.CopyTo(output);
                builder.Append(Encoding.Latin1.GetString(output.ToArray()));
            }
            catch
            {
                builder.Append(text[bodyStart..end]);
            }

            index = end + 1;
        }

        return builder.ToString();
    }

    [Theory]
    [InlineData(double.NaN, 10, 10, 10)]
    [InlineData(10, double.NaN, 10, 10)]
    [InlineData(10, 10, double.NaN, 10)]
    [InlineData(10, 10, 10, double.NaN)]
    [InlineData(double.PositiveInfinity, 10, 10, 10)]
    [InlineData(10, double.NegativeInfinity, 10, 10)]
    public void ANonFiniteCoordinateRefusesTheExport(double x, double y, double width, double height)
    {
        var export = () => PortablePdfWriter.WriteToBytes(DocumentWith(new PdfFillRect(x, y, width, height, Black)));

        export.Should().Throw<InvalidOperationException>(
                "writing it produces a file with a valid header and an unparseable page, which is " +
                "worse than a failed export because nothing reports the problem")
            .WithMessage("*non-finite*");
    }

    [Fact]
    public void ANonFinitePageSizeRefusesTheExport()
    {
        // Page dimensions reach the same formatter through the MediaBox, so they are covered by the
        // same choke point -- asserted rather than assumed.
        var export = () => PortablePdfWriter.WriteToBytes(
            new PdfContentDocument([new PdfContentPage(double.NaN, 792, [])]));

        export.Should().Throw<InvalidOperationException>().WithMessage("*non-finite*");
    }

    [Fact]
    public void OrdinaryContentStillExportsAndItsOperatorsAreClean()
    {
        // The control, and the narrowness check: the guard must not disturb a normal export.
        var bytes = PortablePdfWriter.WriteToBytes(DocumentWith(new PdfFillRect(10, 10, 100, 50, Black)));

        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");

        var operators = ContentStreamText(bytes);
        operators.Should().Contain("10 10 100 50 re", "the rectangle is written as ordinary numbers");
        operators.Should().NotContain("NaN");
        operators.Should().NotContain("Infinity");
    }

    [Fact]
    public void AVeryLargeButFiniteCoordinateIsStillAccepted()
    {
        // Narrowness again: only NON-FINITE values are refused. A large finite value is unusual but
        // representable, and rejecting it would break exports that work today.
        var export = () => PortablePdfWriter.WriteToBytes(DocumentWith(new PdfFillRect(1e6, 10, 10, 10, Black)));

        export.Should().NotThrow();
    }
}
