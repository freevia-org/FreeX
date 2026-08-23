using System.Globalization;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class PivotSharedItemCaptionResolverTests
{
    [Theory]
    [InlineData(PivotFieldGrouping.Year, "2026")]
    [InlineData(PivotFieldGrouping.Quarter, "2026-Q2")]
    [InlineData(PivotFieldGrouping.Month, "2026-05")]
    [InlineData(PivotFieldGrouping.Day, "2026-05-17")]
    public void Resolve_DateKind_AppliesInvariantGroupingCaption(
        PivotFieldGrouping grouping,
        string expected)
    {
        var field = new PivotCacheFieldModel(
            "Date",
            ContainsString: true,
            ContainsDate: true,
            ContainsMixedTypes: true,
            Grouping: grouping);

        PivotSharedItemCaptionResolver.Resolve("2026-05-17T13:45:00", 'd', field)
            .Should().Be(expected);
    }

    [Fact]
    public void Resolve_UngroupedDateAndNumber_UseCurrentCultureCaptions()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var mixedField = new PivotCacheFieldModel(
                "Mixed",
                ContainsString: true,
                ContainsNumber: true,
                ContainsDate: true,
                ContainsMixedTypes: true);

            PivotSharedItemCaptionResolver.Resolve("2026-05-17T00:00:00", 'd', mixedField)
                .Should().Be(new DateTime(2026, 5, 17).ToShortDateString());
            PivotSharedItemCaptionResolver.Resolve("1234.5", 'n', mixedField)
                .Should().Be(1234.5.ToString(CultureInfo.CurrentCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Resolve_AbsentKind_UsesOnlyExclusiveFieldTypeFlags()
    {
        var dateField = new PivotCacheFieldModel("Date", ContainsDate: true, Grouping: PivotFieldGrouping.Month);
        var numberField = new PivotCacheFieldModel("Number", ContainsNumber: true);
        var mixedField = new PivotCacheFieldModel("Mixed", ContainsString: true, ContainsDate: true);

        PivotSharedItemCaptionResolver.Resolve("2026-05-17", null, dateField).Should().Be("2026-05");
        PivotSharedItemCaptionResolver.Resolve("12.5", null, numberField)
            .Should().Be(12.5.ToString(CultureInfo.CurrentCulture));
        PivotSharedItemCaptionResolver.Resolve("2026-05-17", null, mixedField).Should().Be("2026-05-17");
    }

    [Theory]
    [InlineData("not-a-date", 'd')]
    [InlineData("not-a-number", 'n')]
    [InlineData("plain text", 's')]
    public void Resolve_UnparseableOrTextItem_PreservesRawCaption(string raw, char kind)
    {
        var field = new PivotCacheFieldModel("Mixed", ContainsMixedTypes: true);

        PivotSharedItemCaptionResolver.Resolve(raw, kind, field).Should().Be(raw);
    }

    [Fact]
    public void Resolve_MissingField_PreservesRawCaption()
    {
        PivotSharedItemCaptionResolver.Resolve("2026-05-17", 'd', field: null)
            .Should().Be("2026-05-17");
    }
}
