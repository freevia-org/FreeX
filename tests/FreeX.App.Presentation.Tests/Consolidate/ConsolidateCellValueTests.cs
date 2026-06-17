using FreeX.App.Presentation.Consolidate;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Consolidate;

public sealed class ConsolidateCellValueTests
{
    [Fact]
    public void Blank_IsBlankAndEmpty()
    {
        var cell = ConsolidateCellValue.Blank;

        cell.IsBlank.Should().BeTrue();
        cell.IsNumber.Should().BeFalse();
        cell.IsNonEmpty.Should().BeFalse();
    }

    [Fact]
    public void FromNumber_IsNumericAndNonEmpty()
    {
        var cell = ConsolidateCellValue.FromNumber(3.5);

        cell.IsNumber.Should().BeTrue();
        cell.IsNonEmpty.Should().BeTrue();
        cell.Number.Should().Be(3.5);
    }

    [Fact]
    public void FromLabel_WithText_IsLabelAndNonEmpty()
    {
        var cell = ConsolidateCellValue.FromLabel("  Region ");

        cell.Kind.Should().Be(ConsolidateCellKind.Label);
        cell.IsNonEmpty.Should().BeTrue();
        cell.LabelText().Should().Be("Region"); // trimmed
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromLabel_WithBlankText_IsTreatedAsBlank(string? text)
    {
        var cell = ConsolidateCellValue.FromLabel(text);

        cell.IsBlank.Should().BeTrue();
        cell.IsNonEmpty.Should().BeFalse();
    }

    [Fact]
    public void LabelText_OfNumber_PrefersDisplayTextThenFallsBackToInvariantRender()
    {
        ConsolidateCellValue.FromNumber(2024, "FY2024").LabelText().Should().Be("FY2024");
        ConsolidateCellValue.FromNumber(2024).LabelText().Should().Be("2024");
    }

    [Fact]
    public void Equality_DistinguishesKindAndValue()
    {
        ConsolidateCellValue.FromNumber(1).Should().Be(ConsolidateCellValue.FromNumber(1));
        ConsolidateCellValue.FromNumber(1).Should().NotBe(ConsolidateCellValue.FromNumber(2));
        ConsolidateCellValue.FromLabel("a").Should().NotBe(ConsolidateCellValue.Blank);
        (ConsolidateCellValue.FromNumber(1) == ConsolidateCellValue.FromNumber(1)).Should().BeTrue();
        (ConsolidateCellValue.FromNumber(1) != ConsolidateCellValue.Blank).Should().BeTrue();
    }
}
