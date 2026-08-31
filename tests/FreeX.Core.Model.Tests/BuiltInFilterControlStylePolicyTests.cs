using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class BuiltInFilterControlStylePolicyTests
{
    [Theory]
    [InlineData("SlicerStyleLight2", "SlicerStyleLight", WorkbookThemeColorSlot.Accent2)]
    [InlineData("SlicerStyleLight3", "SlicerStyleLight", WorkbookThemeColorSlot.Accent3)]
    [InlineData("SlicerStyleLight4", "SlicerStyleLight", WorkbookThemeColorSlot.Accent4)]
    [InlineData("SlicerStyleLight5", "SlicerStyleLight", WorkbookThemeColorSlot.Accent5)]
    [InlineData("SlicerStyleLight6", "SlicerStyleLight", WorkbookThemeColorSlot.Accent6)]
    [InlineData("TimeSlicerStyleLight2", "TimeSlicerStyleLight", WorkbookThemeColorSlot.Accent2)]
    [InlineData("TimeSlicerStyleLight3", "TimeSlicerStyleLight", WorkbookThemeColorSlot.Accent3)]
    [InlineData("TimeSlicerStyleLight4", "TimeSlicerStyleLight", WorkbookThemeColorSlot.Accent4)]
    [InlineData("TimeSlicerStyleLight5", "TimeSlicerStyleLight", WorkbookThemeColorSlot.Accent5)]
    [InlineData("TimeSlicerStyleLight6", "TimeSlicerStyleLight", WorkbookThemeColorSlot.Accent6)]
    [InlineData(" \tSlicerStyleLight2\r\n", "SlicerStyleLight", WorkbookThemeColorSlot.Accent2)]
    public void ResolveLightAccentSlot_RecognizesExactBuiltInFamilyStyles(
        string styleName,
        string familyPrefix,
        WorkbookThemeColorSlot expected)
    {
        BuiltInFilterControlStylePolicy.ResolveLightAccentSlot(styleName, familyPrefix)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "SlicerStyleLight")]
    [InlineData("", "SlicerStyleLight")]
    [InlineData(" \t\r\n", "SlicerStyleLight")]
    [InlineData("SlicerStyleLight1", "SlicerStyleLight")]
    [InlineData("SlicerStyleLight0", "SlicerStyleLight")]
    [InlineData("SlicerStyleLight7", "SlicerStyleLight")]
    [InlineData("SlicerStyleLight22", "SlicerStyleLight")]
    [InlineData("SlicerStyleOther2", "SlicerStyleLight")]
    [InlineData("slicerStyleLight2", "SlicerStyleLight")]
    [InlineData("TimeSlicerStyleLight2", "SlicerStyleLight")]
    [InlineData("SlicerStyleLight2", "TimeSlicerStyleLight")]
    [InlineData("SlicerStyleLight2", "")]
    public void ResolveLightAccentSlot_RejectsDefaultsUnknownsAndWrongFamilies(
        string? styleName,
        string familyPrefix)
    {
        BuiltInFilterControlStylePolicy.ResolveLightAccentSlot(styleName, familyPrefix)
            .Should().BeNull();
    }
}
