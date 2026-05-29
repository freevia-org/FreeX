using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Tests that CellStyle.Equals and GetHashCode correctly include the three NativeDifferential* fields.
/// </summary>
public class CellStyleNativeDifferentialEqualityTests
{
    private static CellStyle BaseStyle() => new()
    {
        FontName = "Calibri",
        FontSize = 11,
        Bold = false,
        NativeDifferentialAttributes = null,
        NativeDifferentialChildXmls = null,
        NativeDifferentialElementXmls = null,
    };

    [Fact]
    public void Equals_ReturnsFalse_WhenNativeDifferentialAttributesDiffer()
    {
        var a = BaseStyle();
        a.NativeDifferentialAttributes = new Dictionary<string, string> { ["dxfId"] = "1" };

        var b = BaseStyle();
        b.NativeDifferentialAttributes = new Dictionary<string, string> { ["dxfId"] = "2" };

        a.Equals(b).Should().BeFalse("styles differ in NativeDifferentialAttributes");
    }

    [Fact]
    public void GetHashCode_DiffersWhenNativeDifferentialAttributesDiffer()
    {
        var a = BaseStyle();
        a.NativeDifferentialAttributes = new Dictionary<string, string> { ["dxfId"] = "1" };

        var b = BaseStyle();
        b.NativeDifferentialAttributes = new Dictionary<string, string> { ["dxfId"] = "2" };

        a.GetHashCode().Should().NotBe(b.GetHashCode(),
            "hash codes should differ when NativeDifferentialAttributes differ");
    }

    [Fact]
    public void Equals_ReturnsFalse_WhenNativeDifferentialChildXmlsDiffer()
    {
        var a = BaseStyle();
        a.NativeDifferentialChildXmls = new List<string> { "<extLst/>" };

        var b = BaseStyle();
        b.NativeDifferentialChildXmls = new List<string> { "<extLst><ext/></extLst>" };

        a.Equals(b).Should().BeFalse("styles differ in NativeDifferentialChildXmls");
    }

    [Fact]
    public void Equals_ReturnsFalse_WhenNativeDifferentialElementXmlsDiffer()
    {
        var a = BaseStyle();
        a.NativeDifferentialElementXmls = new Dictionary<string, string> { ["font"] = "<font><b/></font>" };

        var b = BaseStyle();
        b.NativeDifferentialElementXmls = new Dictionary<string, string> { ["font"] = "<font/>" };

        a.Equals(b).Should().BeFalse("styles differ in NativeDifferentialElementXmls");
    }

    [Fact]
    public void Equals_ReturnsTrue_WhenAllNativeDifferentialFieldsAreEqual()
    {
        var attrs = new Dictionary<string, string> { ["dxfId"] = "5" };
        var childXmls = new List<string> { "<extLst/>" };
        var elementXmls = new Dictionary<string, string> { ["font"] = "<font><b/></font>" };

        var a = BaseStyle();
        a.NativeDifferentialAttributes = attrs;
        a.NativeDifferentialChildXmls = childXmls;
        a.NativeDifferentialElementXmls = elementXmls;

        var b = BaseStyle();
        b.NativeDifferentialAttributes = new Dictionary<string, string> { ["dxfId"] = "5" };
        b.NativeDifferentialChildXmls = new List<string> { "<extLst/>" };
        b.NativeDifferentialElementXmls = new Dictionary<string, string> { ["font"] = "<font><b/></font>" };

        a.Equals(b).Should().BeTrue("all NativeDifferential fields are structurally equal");
        a.GetHashCode().Should().Be(b.GetHashCode(), "equal styles must share the same hash code");
    }

    [Fact]
    public void Equals_ReturnsFalse_WhenNativeDifferentialAttributesIsNullOnOneAndNonNullOnOther()
    {
        var a = BaseStyle();
        a.NativeDifferentialAttributes = null;

        var b = BaseStyle();
        b.NativeDifferentialAttributes = new Dictionary<string, string> { ["dxfId"] = "1" };

        a.Equals(b).Should().BeFalse("null vs non-null NativeDifferentialAttributes must not be equal");
        b.Equals(a).Should().BeFalse("non-null vs null NativeDifferentialAttributes must not be equal");
    }
}
