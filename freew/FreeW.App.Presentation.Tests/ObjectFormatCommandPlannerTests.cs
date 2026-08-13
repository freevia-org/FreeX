using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class ObjectFormatCommandPlannerTests
{
    [Theory]
    [InlineData(false, "Shape Position")]
    [InlineData(true, "Shape Position in Group")]
    public void ShapePositionDialogTitle_ReflectsCoordinateSpace(bool isGroupLocal, string expected)
    {
        ObjectFormatCommandPlanner.ShapePositionDialogTitle(isGroupLocal).Should().Be(expected);
    }

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

    [Fact]
    public void ShapeFillAndOutlineCommands_ExposeWordLikeShapeStyleChoices()
    {
        ObjectFormatCommandPlanner.ShapeFillCommandId.Should().Be("freew.shape-fill");
        ObjectFormatCommandPlanner.ShapeOutlineCommandId.Should().Be("freew.shape-outline");

        ObjectFormatCommandPlanner.ShapeFillCommands()
            .Should()
            .Equal(
                new ObjectFormatShapeFillCommand("freew.shape-fill-no-fill", ObjectFormatShapeFillKind.NoFill),
                new ObjectFormatShapeFillCommand("freew.shape-fill-gradient-blue", ObjectFormatShapeFillKind.GradientBlue),
                new ObjectFormatShapeFillCommand("freew.shape-fill-gradient-orange", ObjectFormatShapeFillKind.GradientOrange),
                new ObjectFormatShapeFillCommand("freew.shape-fill-pattern-diag", ObjectFormatShapeFillKind.PatternDiagonalCross));

        ObjectFormatCommandPlanner.ShapeOutlineCommands()
            .Should()
            .Equal(
                new ObjectFormatShapeOutlineCommand("freew.shape-outline-no-outline", ObjectFormatShapeOutlineKind.NoOutline),
                new ObjectFormatShapeOutlineCommand("freew.shape-outline-solid", ObjectFormatShapeOutlineKind.Solid),
                new ObjectFormatShapeOutlineCommand("freew.shape-outline-dash", ObjectFormatShapeOutlineKind.Dash),
                new ObjectFormatShapeOutlineCommand("freew.shape-outline-dot", ObjectFormatShapeOutlineKind.Dot));
    }

    [Theory]
    [InlineData(ShapeKind.Rectangle, true)]
    [InlineData(ShapeKind.RoundedRectangle, true)]
    [InlineData(ShapeKind.Ellipse, true)]
    [InlineData(ShapeKind.TextBox, true)]
    public void CanFormatShapeFillOutline_EnablesShapesAndTextBoxes(ShapeKind kind, bool expected)
    {
        ObjectFormatCommandPlanner.CanFormatShapeFillOutline(kind).Should().Be(expected);
    }

    [Fact]
    public void CanFormatShapeFillOutline_DisablesWhenNoShapeIsSelected()
    {
        ObjectFormatCommandPlanner.CanFormatShapeFillOutline(null).Should().BeFalse();
    }

    [Fact]
    public void BuildShapeExtendedFill_ReturnsSharedGradientAndPatternPresets()
    {
        var blue = ObjectFormatCommandPlanner.BuildShapeExtendedFill(ObjectFormatShapeFillKind.GradientBlue);
        blue.Should().NotBeNull();
        blue!.Kind.Should().Be(ShapeFillKind.Gradient);
        blue.GradientAngle.Should().Be(5400000);
        blue.GradientStops.Should().Equal(
            new GradientStop(0, "#4472C4"),
            new GradientStop(100000, "#1F4E79"));

        var pattern = ObjectFormatCommandPlanner.BuildShapeExtendedFill(ObjectFormatShapeFillKind.PatternDiagonalCross);
        pattern.Should().NotBeNull();
        pattern!.Kind.Should().Be(ShapeFillKind.Pattern);
        pattern.PatternPreset.Should().Be("diagCross");
        pattern.PatternFgColorHex.Should().Be("#4472C4");
        pattern.PatternBgColorHex.Should().Be("#FFFFFF");
    }

    [Theory]
    [InlineData(ObjectFormatShapeOutlineKind.NoOutline, "#4472C4", 1.5, null, 0, null)]
    [InlineData(ObjectFormatShapeOutlineKind.Solid, "#4472C4", 1.5, "#4472C4", 1.5, null)]
    [InlineData(ObjectFormatShapeOutlineKind.Dash, null, 0, "000000", 0.75, "dash")]
    [InlineData(ObjectFormatShapeOutlineKind.Dot, "", 0.25, "000000", 0.75, "sysDot")]
    public void PlanShapeOutline_MatchesWpfDefaults(
        ObjectFormatShapeOutlineKind kind,
        string? currentColor,
        double currentWidth,
        string? expectedColor,
        double expectedWidth,
        string? expectedDash)
    {
        ObjectFormatCommandPlanner.PlanShapeOutline(kind, currentColor, currentWidth)
            .Should()
            .Be(new ObjectFormatShapeOutlinePlan(expectedColor, expectedWidth, expectedDash));
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
