namespace FreeW.Core.Model.Tests;

/// <summary>
/// Change Shape commands against shapes that carry a DrawingML preset identity: applying must not leave
/// a stale preset behind, and undo must restore the exact prior geometry (captured at Apply).
/// </summary>
public class ShapePresetCommandTests
{
    private sealed class TestCtx(TextDocument doc) : IDocumentCommandContext
    {
        public TextDocument Document => doc;
    }

    private static (TestCtx Context, Shape Shape) DocumentWith(Shape shape)
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph { Runs = { Run.FromShape(shape) } });
        return (new TestCtx(document), shape);
    }

    private static (string? Preset, string? Signature, CustomGeometry? Geometry, ShapeKind Kind, int Adjustments) Snapshot(Shape shape) =>
        (shape.PresetName, shape.PresetOutlineSignature, shape.CustomGeometry, shape.Kind, shape.PresetAdjustments.Count);

    [Fact]
    public void ChangingAPresetShapeToANamedKind_DropsThePreset_AndUndoRestoresItExactly()
    {
        var star = PresetShapeFactory.Create("star5", 100, 100);
        star.PresetAdjustments.Add(new ShapeAdjustment("adj", "val 25000"));
        PresetShapeGeometry.ApplyPresetOutline(star);
        var (context, shape) = DocumentWith(star);
        var before = Snapshot(shape);
        var command = new SetShapeKindCommand(0, 0, ShapeKind.Ellipse);

        command.HasEffect(context).Should().BeTrue();
        command.Apply(context);

        shape.Kind.Should().Be(ShapeKind.Ellipse);
        shape.PresetName.Should().BeNull();
        shape.PresetAdjustments.Should().BeEmpty();
        shape.HasCustomGeometry.Should().BeFalse("the star outline must not keep drawing over the new ellipse");

        command.Revert(context);

        Snapshot(shape).Should().Be(before);
        shape.PresetAdjustments.Should().Equal(new ShapeAdjustment("adj", "val 25000"));
        shape.HasIntactPresetGeometry.Should().BeTrue();
    }

    [Fact]
    public void ChangingAPresetShapeToItsUnderlyingRectangleKind_StillHasEffect()
    {
        var (context, shape) = DocumentWith(PresetShapeFactory.Create("hexagon", 100, 80));

        var command = new SetShapeKindCommand(0, 0, ShapeKind.Rectangle);

        command.HasEffect(context).Should().BeTrue();
        command.Apply(context);
        shape.PresetName.Should().BeNull();
        shape.HasCustomGeometry.Should().BeFalse();
    }

    [Fact]
    public void ChangingAnEditedFreeformShape_KeepsItsUserEditedOutline()
    {
        var edited = PresetShapeFactory.Create("hexagon", 100, 80);
        edited.CustomGeometry!.Segments.RemoveAt(1);
        var (context, shape) = DocumentWith(edited);
        var outline = shape.CustomGeometry;

        new SetShapeKindCommand(0, 0, ShapeKind.Ellipse).Apply(context);

        shape.CustomGeometry.Should().BeSameAs(outline);
        shape.PresetName.Should().BeNull();
    }

    [Fact]
    public void PlainKindChange_OnAShapeWithoutAPreset_BehavesAsBefore()
    {
        var (context, shape) = DocumentWith(Shape.Preset(ShapeKind.Rectangle, 100, 80));
        var same = new SetShapeKindCommand(0, 0, ShapeKind.Rectangle);
        var different = new SetShapeKindCommand(0, 0, ShapeKind.RoundedRectangle);

        same.HasEffect(context).Should().BeFalse();
        different.HasEffect(context).Should().BeTrue();
        different.Apply(context);
        shape.Kind.Should().Be(ShapeKind.RoundedRectangle);
        different.Revert(context);
        shape.Kind.Should().Be(ShapeKind.Rectangle);
        shape.CustomGeometry.Should().BeNull();
    }

    [Theory]
    [InlineData("rightArrow")]
    [InlineData("ellipse")]
    [InlineData("rect")]
    [InlineData("gear6")]
    public void SetShapePreset_AppliesThePreset_AndUndoRestoresThePriorGeometry(string preset)
    {
        var (context, shape) = DocumentWith(PresetShapeFactory.Create("star5", 100, 100));
        var before = Snapshot(shape);
        var command = new SetShapePresetCommand(0, 0, preset);

        command.HasEffect(context).Should().BeTrue();
        command.Apply(context);

        if (PresetShapeGeometry.IsNamedKindPreset(preset))
            shape.PresetName.Should().BeNull();
        else
            shape.PresetName.Should().Be(preset);
        shape.HasIntactPresetGeometry.Should().Be(!PresetShapeGeometry.IsNamedKindPreset(preset));

        command.Revert(context);

        Snapshot(shape).Should().Be(before);
    }

    [Fact]
    public void SetShapePreset_ToTheSameIntactPreset_HasNoEffect()
    {
        var (context, _) = DocumentWith(PresetShapeFactory.Create("star5", 100, 100));

        new SetShapePresetCommand(0, 0, "star5").HasEffect(context).Should().BeFalse();
        new SetShapePresetCommand(0, 0, "").HasEffect(context).Should().BeFalse();
    }

    [Fact]
    public void SetShapePreset_OnATextBox_KeepsItATextBox()
    {
        var (context, shape) = DocumentWith(Shape.TextBoxWith("Note", 120, 60));

        new SetShapePresetCommand(0, 0, "wedgeRectCallout").Apply(context);

        shape.Kind.Should().Be(ShapeKind.TextBox);
        shape.PresetName.Should().Be("wedgeRectCallout");
        shape.PlainText.Should().Be("Note");
    }
}
