using FluentAssertions;
using DrawingMlCoordinateUnits = Free.Shared.Drawing.DrawingMlCoordinateUnits;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r550: r547-r549 swept the three apps' IO layers and recorded the SHARED tier as unread. It holds
/// the same class, and worse placed -- a parser here is reached by all three apps at once.
///
/// <para><see cref="DrawingMlCoordinateUnits.EmuToPixels(string?)"/> converts a coordinate straight
/// out of drawing XML and already returns 0 for anything it cannot parse; an overflowing literal
/// returned Infinity pixels instead. The tests assert the existing contract now covers a value that
/// parses but cannot be used, and that ordinary coordinates are untouched.</para>
/// </summary>
public class R550_SharedParsersRejectNonFiniteTests
{
    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    public void An_unusable_emu_coordinate_reads_as_absent(string raw)
    {
        // 0 is what this overload already returns for an unparseable or missing attribute, so an
        // unrepresentable one takes the path every other unusable value already took.
        DrawingMlCoordinateUnits.EmuToPixels(raw).Should().Be(0);
    }

    [Theory]
    [InlineData("914400", 96.0)]     // 1 inch
    [InlineData("0", 0.0)]
    [InlineData("-914400", -96.0)]   // negative offsets are legal and must survive
    public void Ordinary_emu_coordinates_still_convert(string raw, double expectedPixels)
    {
        // Non-vacuity: a guard that rejected everything would satisfy the assertions above.
        DrawingMlCoordinateUnits.EmuToPixels(raw).Should().BeApproximately(expectedPixels, 1e-9);
    }
}
