using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests;

public sealed class ChartDataPresetCatalogTests
{
    [Fact]
    public void NamedReplacementRecipesPreserveExistingCommandPayloads()
    {
        ChartDataPresetCatalog.TryCreateNamedReplacement("  Quarterly Sales  ", out var quarterly)
            .Should().BeTrue();
        quarterly.Kind.Should().Be(ChartKind.Column);
        quarterly.Title.Should().Be("Quarterly Sales");
        quarterly.Categories.Should().Equal("Q1", "Q2", "Q3", "Q4");
        quarterly.Series.Should().ContainSingle();
        quarterly.Series[0].Name.Should().Be("Sales");
        quarterly.Series[0].Values.Should().Equal(12d, 18d, 16d, 24d);

        ChartDataPresetCatalog.TryCreateNamedReplacement("Monthly Revenue", out var monthly)
            .Should().BeTrue();
        monthly.Kind.Should().Be(ChartKind.Line);
        monthly.Title.Should().Be("Monthly Revenue");
        monthly.Categories.Should().Equal("Jan", "Feb", "Mar");
        monthly.Series.Should().ContainSingle();
        monthly.Series[0].Name.Should().Be("Revenue");
        monthly.Series[0].Values.Should().Equal(5d, 6d, 7d);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("monthly revenue")]
    public void UnknownOrNonCanonicalReplacementNamesAreRejected(string? name)
    {
        ChartDataPresetCatalog.TryCreateNamedReplacement(name, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void EveryMaterializationOwnsItsMutableCollections()
    {
        var first = ChartDataPresetCatalog.CreateDefaultInsertion();
        var second = ChartDataPresetCatalog.CreateDefaultInsertion();
        ChartDataPresetCatalog.TryCreateNamedReplacement("Quarterly Sales", out var replacementOne);
        ChartDataPresetCatalog.TryCreateNamedReplacement("Quarterly Sales", out var replacementTwo);

        first.Should().NotBeSameAs(second);
        first.Series[0].Should().NotBeSameAs(second.Series[0]);
        replacementOne.Should().NotBeSameAs(replacementTwo);
        replacementOne.Series[0].Should().NotBeSameAs(replacementTwo.Series[0]);

        first.Categories[0] = "Changed";
        first.Series[0].Values[0] = 99d;
        replacementOne.Categories[0] = "Changed";
        replacementOne.Series[0].Values[0] = 99d;

        second.Categories[0].Should().Be("Q1");
        second.Series[0].Values[0].Should().Be(8d);
        replacementTwo.Categories[0].Should().Be("Q1");
        replacementTwo.Series[0].Values[0].Should().Be(12d);
    }
}
