using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Covers the page-setup DPI handling in <see cref="XlsxWorksheetPageLayoutNormalizer.NormalizePageSetup"/>:
/// non-positive <c>horizontalDpi</c>/<c>verticalDpi</c> values (which violate the SpreadsheetML
/// MinInclusive=1 facet) are dropped, while valid positive values survive.
/// </summary>
public sealed class XlsxWorksheetPageLayoutNormalizerDpiTests
{
    private static XElement PageSetup(params (string Name, string Value)[] attributes)
    {
        var element = new XElement("pageSetup");
        foreach (var (name, value) in attributes)
            element.SetAttributeValue(name, value);
        return element;
    }

    [Fact]
    public void NormalizePageSetup_DropsZeroDpi()
    {
        var pageSetup = PageSetup(("orientation", "portrait"), ("horizontalDpi", "0"), ("verticalDpi", "0"));

        var changed = XlsxWorksheetPageLayoutNormalizer.NormalizePageSetup(pageSetup);

        changed.Should().BeTrue();
        pageSetup.Attribute("horizontalDpi").Should().BeNull();
        pageSetup.Attribute("verticalDpi").Should().BeNull();
        pageSetup.Attribute("orientation")!.Value.Should().Be("portrait");
    }

    [Fact]
    public void NormalizePageSetup_KeepsPositiveDpi()
    {
        var pageSetup = PageSetup(("horizontalDpi", "600"), ("verticalDpi", "1200"));

        XlsxWorksheetPageLayoutNormalizer.NormalizePageSetup(pageSetup);

        pageSetup.Attribute("horizontalDpi")!.Value.Should().Be("600");
        pageSetup.Attribute("verticalDpi")!.Value.Should().Be("1200");
    }
}
