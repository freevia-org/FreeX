using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Dialogs;

public sealed class MultilevelListDialogPlannerTests
{
    [Fact]
    public void InitialStateAndResult_PreserveChoicesStartsAndFormats()
    {
        var initial = MultilevelListDialogPlanner.BuildInitialState(
            [ListNumberFormat.UpperRoman, ListNumberFormat.LowerLetter, ListNumberFormat.LowerRoman],
            CultureInfo.InvariantCulture);
        initial.LevelsIndex.Should().Be(8);
        initial.Level0FormatIndex.Should().Be(4);
        initial.Level1FormatIndex.Should().Be(1);
        initial.Level2FormatIndex.Should().Be(3);

        MultilevelListDialogPlanner.TryBuildResult(
            new MultilevelListDialogInput(2, "4", "7", 4, 1, 3),
            CultureInfo.InvariantCulture,
            out var result,
            out var validation).Should().BeTrue();
        validation.Should().BeNull();
        result!.Levels.Should().Be(3);
        result.Level0StartAt.Should().Be(4);
        result.Level1StartAt.Should().Be(7);
        result.NumberFormats.Take(3).Should().Equal(
            ListNumberFormat.UpperRoman,
            ListNumberFormat.LowerLetter,
            ListNumberFormat.LowerRoman);
    }

    [Fact]
    public void BlankStartsMeanContinueAndInvalidStartsIdentifyField()
    {
        var input = new MultilevelListDialogInput(8, "", "  ", 0, 0, 0);
        MultilevelListDialogPlanner.TryBuildResult(
            input,
            CultureInfo.InvariantCulture,
            out var result,
            out _).Should().BeTrue();
        result!.Level0StartAt.Should().BeNull();
        result.Level1StartAt.Should().BeNull();

        MultilevelListDialogPlanner.TryBuildResult(
            input with { Level1StartAtText = "0" },
            CultureInfo.InvariantCulture,
            out _,
            out var validation).Should().BeFalse();
        validation!.Field.Should().Be(MultilevelListDialogField.Level1StartAt);
    }

    [Theory]
    [InlineData(0, 0, 4)]
    [InlineData(1, 1, 7)]
    [InlineData(4, 2, null)]
    public void ApplyDefinition_UsesStartsAndClampsToConfiguredLevels(
        int sourceLevel,
        int expectedLevel,
        int? expectedStart)
    {
        var definition = new MultilevelListDefinition(
            3,
            Level0StartAt: 4,
            Level1StartAt: 7,
            MultiLevelListFormat.DecimalNumberFormats);
        var source = ParagraphFormatting.Default with
        {
            ListLevel = sourceLevel,
            ListStartOverride = sourceLevel > 1 ? null : 99,
        };

        var result = MultilevelListDialogPlanner.ApplyDefinition(source, definition);

        result.ListKind.Should().Be(ListKind.MultiLevel);
        result.ListLevel.Should().Be(expectedLevel);
        result.ListStartOverride.Should().Be(expectedStart);
    }

    [Theory]
    [InlineData(0, "Heading1")]
    [InlineData(1, "Heading2")]
    [InlineData(8, "Heading3")]
    public void ResolveLinkedHeadingStyleId_MapsConfiguredLevels(int level, string expectedStyleId)
    {
        var definition = new MultilevelListDefinition(
            9,
            null,
            null,
            MultiLevelListFormat.DecimalNumberFormats,
            LinkToHeadingStyles: true);

        MultilevelListDialogPlanner.ResolveLinkedHeadingStyleId(level, definition)
            .Should().Be(expectedStyleId);
        MultilevelListDialogPlanner.ResolveLinkedHeadingStyleId(
                level,
                definition with { LinkToHeadingStyles = false })
            .Should().BeNull();
    }
}
