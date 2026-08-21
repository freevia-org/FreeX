using FluentAssertions;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Host.Tests;

public sealed class BorderMenuIconCatalogTests
{
    [Theory]
    [InlineData("Bottom Border", BorderMenuIconKind.Bottom)]
    [InlineData("Inside Borders", BorderMenuIconKind.Inside)]
    [InlineData("Thick Outside Borders", BorderMenuIconKind.ThickBox)]
    [InlineData("Draw Border Grid", BorderMenuIconKind.DrawBorderGrid)]
    [InlineData("Erase Border", BorderMenuIconKind.EraseBorder)]
    [InlineData("Accent 1", BorderMenuIconKind.ColorAccent1)]
    [InlineData("Double", BorderMenuIconKind.StyleDouble)]
    [InlineData("More Borders", BorderMenuIconKind.More)]
    public void Catalog_MapsEachDistinctBorderMenuFamily(string commandId, BorderMenuIconKind expected)
    {
        BorderMenuIconCatalog.TryGetKind(commandId, out var actual).Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Fact]
    public void Catalog_DoesNotOverrideUnrelatedCommands()
    {
        BorderMenuIconCatalog.TryGetKind("Bold", out _).Should().BeFalse();
    }

    [Fact]
    public void Catalog_CoversEveryPublishedBorderMenuCommand()
    {
        var missing = HomeBorderMenuCatalog.All
            .Where(item => !BorderMenuIconCatalog.TryGetKind(item.CommandId, out _))
            .Select(item => item.CommandId)
            .ToArray();

        missing.Should().BeEmpty();
    }
}
