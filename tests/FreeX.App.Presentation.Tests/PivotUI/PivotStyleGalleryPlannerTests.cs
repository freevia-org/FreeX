using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotStyleGalleryPlannerTests
{
    [Fact]
    public void BuiltInStyleNames_Has84LightMediumDarkNames()
    {
        PivotStyleGalleryPlanner.BuiltInStyleNames.Should().HaveCount(84);
        PivotStyleGalleryPlanner.BuiltInStyleNames.Take(28)
            .Should().Equal(Enumerable.Range(1, 28).Select(index => $"PivotStyleLight{index}"));
        PivotStyleGalleryPlanner.BuiltInStyleNames.Skip(28).Take(28)
            .Should().Equal(Enumerable.Range(1, 28).Select(index => $"PivotStyleMedium{index}"));
        PivotStyleGalleryPlanner.BuiltInStyleNames.Skip(56).Take(28)
            .Should().Equal(Enumerable.Range(1, 28).Select(index => $"PivotStyleDark{index}"));
    }

    [Theory]
    [InlineData(null, "PivotStyleLight16")]
    [InlineData("", "PivotStyleLight16")]
    [InlineData("   ", "PivotStyleLight16")]
    [InlineData("  PivotStyleMedium2  ", "PivotStyleMedium2")]
    public void NormalizeStyleName_DefaultsAndTrims(string? input, string expected)
    {
        PivotStyleGalleryPlanner.NormalizeStyleName(input).Should().Be(expected);
    }

    [Fact]
    public void GetStyleNames_AppendsCustomStyleWhenNotBuiltIn()
    {
        var names = PivotStyleGalleryPlanner.GetStyleNames("MyCustomStyle");
        names.Should().HaveCount(85);
        names[^1].Should().Be("MyCustomStyle");
    }

    [Fact]
    public void GetStyleNames_KeepsCatalogWhenStyleIsBuiltIn()
    {
        PivotStyleGalleryPlanner.GetStyleNames("PivotStyleDark5").Should().HaveCount(84);
    }

    [Fact]
    public void Capture_ReadsCurrentStyleAndDefaultsBlank()
    {
        PivotStyleGalleryPlanner.Capture(new PivotTableModel { Name = "P", StyleName = "PivotStyleDark3" })
            .StyleName.Should().Be("PivotStyleDark3");
        PivotStyleGalleryPlanner.Capture(new PivotTableModel { Name = "P", StyleName = "" })
            .StyleName.Should().Be(PivotStyleGalleryPlanner.DefaultStyleName);
    }

    [Fact]
    public void FindStyleIndex_FindsCaseInsensitivelyAndFallsBackToZero()
    {
        var names = PivotStyleGalleryPlanner.GetStyleNames();
        PivotStyleGalleryPlanner.FindStyleIndex(names, "pivotstylemedium1")
            .Should().Be(names.ToList().FindIndex(n => n == "PivotStyleMedium1"));
        PivotStyleGalleryPlanner.FindStyleIndex(names, "NotInList").Should().Be(0);
    }

    [Fact]
    public void CreateResult_NormalizesSelection()
    {
        PivotStyleGalleryPlanner.CreateResult("  PivotStyleLight2  ").StyleName.Should().Be("PivotStyleLight2");
        PivotStyleGalleryPlanner.CreateResult(null).StyleName.Should().Be(PivotStyleGalleryPlanner.DefaultStyleName);
    }
}
