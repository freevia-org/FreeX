using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class DrawingObjectActionPlannerTests
{
    [Fact]
    public void CommandTitles_AreSingleSourcedForDrawingObjectAdapters()
    {
        DrawingObjectActionPlanner.InsertTextBoxCommandTitle.Should().Be("Insert Text Box");
        DrawingObjectActionPlanner.InsertShapeCommandTitle.Should().Be("Insert Shape");
        DrawingObjectActionPlanner.ZOrderCommandTitle(forward: true).Should().Be("Bring Forward");
        DrawingObjectActionPlanner.ZOrderCommandTitle(forward: false).Should().Be("Send Backward");
        DrawingObjectActionPlanner.FillCommandTitle(hasFill: true).Should().Be("Object Fill");
        DrawingObjectActionPlanner.FillCommandTitle(hasFill: false).Should().Be("Object No Fill");
        DrawingObjectActionPlanner.ObjectOutlineCommandTitle.Should().Be("Object Outline");
        DrawingObjectActionPlanner.ObjectSizeCommandTitle.Should().Be("Object Size");
        DrawingObjectActionPlanner.RotateObjectCommandTitle.Should().Be("Rotate Object");
        DrawingObjectActionPlanner.ResizeObjectCommandTitle.Should().Be("Resize Object");
    }

    [Fact]
    public void InsertStatus_DescribesShapeAndTextBoxResourceText()
    {
        AssertResourceText(
            DrawingObjectActionPlanner.InsertShapeSuccess(DrawingShapeKind.Diamond, "C7"),
            "InsertLoc_InsertedShapeAt",
            DrawingShapeKind.Diamond,
            "C7");

        AssertResourceText(
            DrawingObjectActionPlanner.InsertTextBoxSuccess("D9"),
            "InsertLoc_InsertedTextBoxAt",
            "D9");
    }

    [Theory]
    [InlineData(DrawingObjectTargetKind.Picture, true, "Drawing_PictureBroughtForward")]
    [InlineData(DrawingObjectTargetKind.Picture, false, "Drawing_PictureSentBackward")]
    [InlineData(DrawingObjectTargetKind.Shape, true, "InsertLoc_BroughtShapeForward")]
    [InlineData(DrawingObjectTargetKind.TextBox, false, "InsertLoc_SentShapeBackward")]
    public void ZOrderSuccess_UsesKindAndDirectionSpecificResourceKey(
        DrawingObjectTargetKind kind,
        bool forward,
        string expectedResourceKey)
    {
        var status = DrawingObjectActionPlanner.ZOrderSuccess(kind, forward);

        status.ResourceKey.Should().Be(expectedResourceKey);
        status.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void FormatStatusDescriptors_CarryResourceKeysAndArguments()
    {
        AssertResourceText(
            DrawingObjectActionPlanner.ShapeFillSuccess("#102030"),
            "InsertLoc_ShapeFillSet",
            "#102030");
        AssertResourceText(
            DrawingObjectActionPlanner.ShapeOutlineSuccess("#405060"),
            "InsertLoc_ShapeOutlineSet",
            "#405060");
        AssertResourceText(
            DrawingObjectActionPlanner.ShapeGradientSuccess("#102030", "#405060"),
            "ShapeGradient_Applied",
            "#102030",
            "#405060");
        AssertResourceText(
            DrawingObjectActionPlanner.RotationSuccess(new FormatPicturePlanner.RotationResult(45)),
            "InsertLoc_RotatedObject",
            45d);
        AssertResourceText(
            DrawingObjectActionPlanner.ResizeSuccess(new ObjectSizeDialogSize(320, 180)),
            "InsertLoc_ResizedObject",
            320d,
            180d);
    }

    [Fact]
    public void ShapeEffectAndAltTextStatus_ChooseResourceKeyFromResult()
    {
        AssertResourceText(
            DrawingObjectActionPlanner.ShapeEffectSuccess(DrawingShapeEffectPreset.None, "No Effect"),
            "ShapeEffects_Cleared");
        AssertResourceText(
            DrawingObjectActionPlanner.ShapeEffectSuccess(DrawingShapeEffectPreset.Glow, "Glow"),
            "ShapeEffects_Applied",
            "Glow");

        AssertResourceText(
            DrawingObjectActionPlanner.AltTextSuccess("  "),
            "InsertLoc_AltTextCleared");
        AssertResourceText(
            DrawingObjectActionPlanner.AltTextSuccess("Diagram"),
            "InsertLoc_AltTextUpdated");
    }

    private static void AssertResourceText(
        DrawingObjectResourceText text,
        string expectedResourceKey,
        params object?[] expectedArguments)
    {
        text.ResourceKey.Should().Be(expectedResourceKey);
        text.Arguments.Should().Equal(expectedArguments);
    }
}
