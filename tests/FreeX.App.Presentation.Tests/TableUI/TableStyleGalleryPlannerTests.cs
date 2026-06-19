using FluentAssertions;
using FreeX.App.Presentation.TableUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.TableUI;

public sealed class TableStyleGalleryPlannerTests
{
    [Fact]
    public void GetOptions_ExposesLightMediumAndDarkExcelGalleryInOrder()
    {
        var options = TableStyleGalleryPlanner.GetOptions();

        options.Should().HaveCount(60);
        options.Select(option => option.StyleName)
            .Should()
            .ContainInOrder(
                "TableStyleLight1",
                "TableStyleLight21",
                "TableStyleMedium1",
                "TableStyleMedium28",
                "TableStyleDark1",
                "TableStyleDark11");
        options.Select(option => option.StyleName).Should().OnlyHaveUniqueItems();

        options.Take(21).Should().OnlyContain(option => option.Label.StartsWith("Light ", StringComparison.Ordinal));
        options.Skip(21).Take(28).Should().OnlyContain(option => option.Label.StartsWith("Medium ", StringComparison.Ordinal));
        options.Skip(49).Should().OnlyContain(option => option.Label.StartsWith("Dark ", StringComparison.Ordinal));
    }

    [Fact]
    public void GetOption_ClampsOutOfRangeIndexes()
    {
        TableStyleGalleryPlanner.GetOption(-10).StyleName.Should().Be("TableStyleLight1");
        TableStyleGalleryPlanner.GetOption(999).StyleName.Should().Be("TableStyleDark11");
    }

    [Fact]
    public void NormalizeStyleName_DefaultsBlankToMedium2()
    {
        TableStyleGalleryPlanner.NormalizeStyleName(null).Should().Be("TableStyleMedium2");
        TableStyleGalleryPlanner.NormalizeStyleName("  ").Should().Be("TableStyleMedium2");
        TableStyleGalleryPlanner.NormalizeStyleName(" TableStyleLight5 ").Should().Be("TableStyleLight5");
    }

    [Fact]
    public void FindStyleIndex_LocatesCurrentStyleCaseInsensitivelyAndDefaultsToFirst()
    {
        var options = TableStyleGalleryPlanner.GetOptions();
        var medium2 = TableStyleGalleryPlanner.FindStyleIndex(options, "tablestylemedium2");
        options[medium2].StyleName.Should().Be("TableStyleMedium2");

        TableStyleGalleryPlanner.FindStyleIndex(options, "CustomStyle").Should().Be(0);
        TableStyleGalleryPlanner.FindStyleIndex(options, null).Should().Be(0);
    }

    [Fact]
    public void TryGetOption_ResolvesBuiltInStyleNamesAndRejectsCustom()
    {
        TableStyleGalleryPlanner.TryGetOption("tablestylemedium2", out var option).Should().BeTrue();
        option.StyleName.Should().Be("TableStyleMedium2");

        TableStyleGalleryPlanner.TryGetOption("CustomTableStyle", out _).Should().BeFalse();
        TableStyleGalleryPlanner.TryGetOption("  ", out _).Should().BeFalse();
    }

    [Fact]
    public void GetOptions_BandingHeaderContrastsWithFill()
    {
        var options = TableStyleGalleryPlanner.GetOptions();

        // Each gallery option carries a banding whose header font is either white or black (Excel's Light 8-14
        // use a genuine black header paired with white text), so the header always reads with contrast.
        options.Should().OnlyContain(option =>
            option.Banding.HeaderFontColor == CellColor.White || option.Banding.HeaderFontColor == CellColor.Black);
    }
}
