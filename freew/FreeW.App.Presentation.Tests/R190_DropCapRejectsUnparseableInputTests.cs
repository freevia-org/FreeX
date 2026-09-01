using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// r190 (backlog item 16): the Drop Cap dialog discarded the result of both TryParse calls
/// (<c>_ = int.TryParse(...)</c>), so text that is not a number left the out-parameter at 0 and
/// <c>Math.Clamp</c> turned that into LinesToDrop=1 / DistanceFromTextPt=0 -- values the user never
/// typed, applied with no error. Every sibling dialog in the same file already rejects a bad value
/// and says so.
/// </summary>
public class R190_DropCapRejectsUnparseableInputTests
{
    private static DropCapOptionsDialogInput Input(string lines, string distance) =>
        new((int)DropCapDialogPosition.Dropped, "Georgia", lines, distance);

    [Theory]
    [InlineData("abc", "0")]
    [InlineData("3", "abc")]
    [InlineData("", "0")]
    [InlineData("3", "")]
    [InlineData("   ", "0")]
    [InlineData("3.5.1", "0")]
    public void TryBuildResult_WithUnparseableText_RejectsInsteadOfSubstitutingADefault(
        string lines,
        string distance)
    {
        DropCapOptionsDialogPlanner.TryBuildResult(
                Input(lines, distance),
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeFalse();

        result.Should().BeNull("no value may be applied from input the dialog could not read");
        error.Should().Be(DropCapOptionsDialogPlanner.ValidationMessage);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void TryBuildResult_WithANonFiniteDistance_Rejects(string distance)
    {
        // double.TryParse accepts these, and Math.Clamp(NaN, 0, 100) returns NaN -- which would be
        // written into the document as a drop-cap distance and propagate through layout.
        DropCapOptionsDialogPlanner.TryBuildResult(
                Input("3", distance),
                CultureInfo.InvariantCulture,
                out var result,
                out _)
            .Should().BeFalse();

        result.Should().BeNull();
    }

    [Fact]
    public void TryBuildResult_WithValuesTheUserTyped_StillClampsThemRatherThanRejecting()
    {
        // The distinction the fix rests on: 99 lines is a real request out of range, not a typo,
        // and the sibling dialogs clamp those too. Only unreadable input is rejected.
        DropCapOptionsDialogPlanner.TryBuildResult(
                Input("99", "-4"),
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        result!.LinesToDrop.Should().Be(10);
        result.DistanceFromTextPt.Should().Be(0);
    }

    [Fact]
    public void TryBuildResult_ReadsNumbersInTheDialogCulture()
    {
        // The comma is the decimal separator in de-DE; parsing it invariantly would fail and the
        // user would be told their correctly-typed value was unreadable.
        DropCapOptionsDialogPlanner.TryBuildResult(
                Input("3", "12,5"),
                new CultureInfo("de-DE"),
                out var result,
                out _)
            .Should().BeTrue();

        result!.DistanceFromTextPt.Should().Be(12.5);
    }
}
