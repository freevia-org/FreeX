using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class ObjectFormatCommandPlannerTests
{
    [Theory]
    [InlineData(ObjectFormatTarget.Picture, "image")]
    [InlineData(ObjectFormatTarget.Shape, "shape")]
    public void PrefixFor_ReturnsRibbonCommandPrefix(ObjectFormatTarget target, string expectedPrefix)
    {
        ObjectFormatCommandPlanner.PrefixFor(target).Should().Be(expectedPrefix);
        ObjectFormatCommandPlanner.WrapDropdownCommandId(target).Should().Be($"freew.{expectedPrefix}-wrap");
        ObjectFormatCommandPlanner.TransformDropdownCommandId(target).Should().Be($"freew.{expectedPrefix}-rotate");
    }

    [Theory]
    [InlineData(ObjectFormatTarget.Picture, "image")]
    [InlineData(ObjectFormatTarget.Shape, "shape")]
    public void WrapCommands_ExposeWordLikeWrapChoices(ObjectFormatTarget target, string prefix)
    {
        ObjectFormatCommandPlanner.WrapCommands(target)
            .Should()
            .Equal(
                new ObjectFormatWrapCommand($"freew.{prefix}-wrap-inline", ImageWrapping.Inline),
                new ObjectFormatWrapCommand($"freew.{prefix}-wrap-square", ImageWrapping.Square),
                new ObjectFormatWrapCommand($"freew.{prefix}-wrap-tight", ImageWrapping.Tight),
                new ObjectFormatWrapCommand($"freew.{prefix}-wrap-top-bottom", ImageWrapping.TopAndBottom),
                new ObjectFormatWrapCommand($"freew.{prefix}-wrap-behind", ImageWrapping.Behind),
                new ObjectFormatWrapCommand($"freew.{prefix}-wrap-front", ImageWrapping.InFront));
    }

    [Theory]
    [InlineData(ObjectFormatTarget.Picture, "image")]
    [InlineData(ObjectFormatTarget.Shape, "shape")]
    public void TransformCommands_ExposeSharedRotateAndFlipChoices(ObjectFormatTarget target, string prefix)
    {
        ObjectFormatCommandPlanner.TransformCommands(target)
            .Should()
            .Equal(
                new ObjectFormatTransformCommand(
                    $"freew.{prefix}-rotate-right90",
                    ObjectFormatTransformKind.Rotate,
                    +90),
                new ObjectFormatTransformCommand(
                    $"freew.{prefix}-rotate-left90",
                    ObjectFormatTransformKind.Rotate,
                    -90),
                new ObjectFormatTransformCommand(
                    $"freew.{prefix}-flip-vertical",
                    ObjectFormatTransformKind.FlipVertical),
                new ObjectFormatTransformCommand(
                    $"freew.{prefix}-flip-horizontal",
                    ObjectFormatTransformKind.FlipHorizontal));
    }

    [Theory]
    [InlineData(ObjectFormatTarget.Picture, "image")]
    [InlineData(ObjectFormatTarget.Shape, "shape")]
    public void ZOrderAndSizeCommands_ExposeSharedArrangeChoices(ObjectFormatTarget target, string prefix)
    {
        ObjectFormatCommandPlanner.ZOrderCommands(target)
            .Should()
            .Equal(
                new ObjectFormatZOrderCommand($"freew.{prefix}-bring-to-front", ZOrderOperation.BringToFront),
                new ObjectFormatZOrderCommand($"freew.{prefix}-send-to-back", ZOrderOperation.SendToBack),
                new ObjectFormatZOrderCommand($"freew.{prefix}-bring-forward", ZOrderOperation.BringForward),
                new ObjectFormatZOrderCommand($"freew.{prefix}-send-backward", ZOrderOperation.SendBackward));

        ObjectFormatCommandPlanner.SizeCommands(target)
            .Should()
            .Equal(
                new ObjectFormatSizeCommand($"freew.{prefix}-width", ObjectFormatSizeDimension.Width),
                new ObjectFormatSizeCommand($"freew.{prefix}-height", ObjectFormatSizeDimension.Height));
    }

    [Theory]
    [InlineData("216", 216)]
    [InlineData(" 72.5 ", 72.5)]
    public void TryParseSizePoints_AcceptsPositiveInvariantDecimals(string text, double expected)
    {
        ObjectFormatCommandPlanner.TryParseSizePoints(text, out var points).Should().BeTrue();
        points.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("12,5")]
    public void TryParseSizePoints_RejectsMissingNonPositiveOrLocaleFormattedValues(string? text)
    {
        ObjectFormatCommandPlanner.TryParseSizePoints(text, out _).Should().BeFalse();
    }
}
