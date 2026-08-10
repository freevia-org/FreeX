using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class DataValidationCloneTests
{
    [Fact]
    public void Clone_PreservesIdentityRangesAndPersistedSettings()
    {
        var source = CreateRule();

        var clone = source.Clone();

        clone.Should().NotBeSameAs(source);
        clone.Id.Should().Be(source.Id);
        clone.AppliesTo.Should().Be(source.AppliesTo);
        clone.AdditionalRanges.Should().Equal(source.AdditionalRanges);
        clone.HasSameDefinition(source).Should().BeTrue();
        clone.IsX14.Should().BeTrue();
    }

    [Fact]
    public void CloneWithNewIdentity_ReplacesRangesAndRegeneratesIdentity()
    {
        var source = CreateRule();
        var targetSheetId = SheetId.New();
        var appliesTo = Range(targetSheetId, 5, 5, 6, 6);
        var additional = Range(targetSheetId, 8, 8, 9, 9);

        var clone = source.CloneWithNewIdentity(appliesTo, [additional]);

        clone.Id.Should().NotBe(source.Id);
        clone.AppliesTo.Should().Be(appliesTo);
        clone.AdditionalRanges.Should().Equal(additional);
        clone.HasSameSettings(source, includeNativeMetadata: true).Should().BeTrue();
    }

    [Fact]
    public void HasSameSettings_MakesNativeMetadataPolicyExplicit()
    {
        var source = CreateRule();
        var other = source.Clone();
        other.NativeChildXmls = ["<different />"];

        source.HasSameSettings(other).Should().BeTrue();
        source.HasSameSettings(other, includeNativeMetadata: true).Should().BeFalse();
    }

    [Fact]
    public void HasSameDefinition_IncludesRangesButNotIdentity()
    {
        var source = CreateRule();
        var sameDefinition = source.CloneForRanges(source.AppliesTo, source.AdditionalRanges, Guid.NewGuid());
        var differentRange = source.CloneWithNewIdentity(
            Range(source.AppliesTo.Start.Sheet, 20, 1, 20, 1),
            source.AdditionalRanges);

        source.HasSameDefinition(sameDefinition).Should().BeTrue();
        source.HasSameDefinition(differentRange).Should().BeFalse();
    }

    private static DataValidation CreateRule()
    {
        var sheetId = SheetId.New();
        var rule = new DataValidation
        {
            AppliesTo = Range(sheetId, 1, 1, 2, 2),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = false,
            ShowDropdown = false,
            AlertStyle = DvAlertStyle.Warning,
            ShowInputMessage = false,
            ShowErrorMessage = false,
            ErrorTitle = "Invalid",
            ErrorMessage = "Use a value from 1 to 10.",
            PromptTitle = "Number",
            PromptMessage = "Enter a whole number.",
            NativeAttributes = new Dictionary<string, string> { ["imeMode"] = "disabled" },
            NativeChildXmls = ["<ext />"],
            NativeContainerAttributes = new Dictionary<string, string> { ["disablePrompts"] = "1" },
            NativeContainerChildXmls = ["<containerExt />"],
            IsX14 = true
        };
        rule.AdditionalRanges.Add(Range(sheetId, 3, 3, 4, 4));
        return rule;
    }

    private static GridRange Range(
        SheetId sheetId,
        uint startRow,
        uint startColumn,
        uint endRow,
        uint endColumn) =>
        new(
            new CellAddress(sheetId, startRow, startColumn),
            new CellAddress(sheetId, endRow, endColumn));
}
