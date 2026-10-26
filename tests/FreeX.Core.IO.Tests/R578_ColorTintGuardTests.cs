using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 578: <c>XlsxColorReader.ReadTint</c> is the one place a colour's <c>tint</c> attribute
/// enters, feeding all four of that reader's colour paths (theme, indexed, and two others). A tint
/// is a bounded modulation — -1..1 in the schema — but <c>double.TryParse</c> accepts the literal
/// "NaN" and overflows "1e400" to Infinity.
/// <para>
/// <c>WorkbookThemeTint.Apply</c> short-circuits only on <c>Math.Abs(tint) &lt; threshold</c>, which
/// is FALSE for NaN (every comparison with NaN is), so a non-finite tint went on into the HSL
/// luminance maths and came back as saturated-cast channels: the colour silently changed. That is
/// the same "a bound that looks like a bound but isn't" shape as Math.Clamp passing NaN through.
/// </para>
/// </summary>
public sealed class R578_ColorTintGuardTests
{
    private static double Read(string? tintText)
    {
        var element = tintText is null
            ? new XElement("fgColor")
            : new XElement("fgColor", new XAttribute("tint", tintText));
        return XlsxColorReader.ReadTint(element);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    public void NonFiniteTint_IsReadAsNoTint(string tintText)
    {
        var tint = Read(tintText);

        double.IsFinite(tint).Should().BeTrue($"tint=\"{tintText}\" produced {tint}");
        tint.Should().Be(0, "an unreadable tint must leave the colour alone");
    }

    [Theory]
    [InlineData("0.5", 0.5)]
    [InlineData("-0.25", -0.25)]
    [InlineData("0", 0)]
    public void FiniteTint_StillReadsThrough(string tintText, double expected)
    {
        // Non-vacuity: the guard must not have flattened every tint to zero.
        Read(tintText).Should().Be(expected);
    }

    [Fact]
    public void MissingTint_IsStillNoTint() => Read(null).Should().Be(0);
}
