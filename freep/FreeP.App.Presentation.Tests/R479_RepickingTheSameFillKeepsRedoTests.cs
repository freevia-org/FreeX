using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r479: re-applying a shape's own fill must not destroy the redo stack.
///
/// <para>`SetShapeFillCommand` did not override <c>HasEffect</c>, which defaults to true, so the bus
/// pushed an undo entry for a command that changed nothing - and <c>UndoRedoStack.Push</c> clears
/// redo, so an equal-value fill silently discarded a pending redo.</para>
///
/// <para>NO PRODUCTION PATH REACHES THIS TODAY, and saying so matters: r202 deliberately left this
/// command inheriting the default with the reason "callers supply a freshly built fill, never the
/// shape's own", and that reason is correct - <c>SetSelectedFill</c> currently has no production
/// caller at all, because FreeP's shape-fill UI is not wired yet. The guard is therefore
/// PRE-EMPTIVE. It is justified not by a live defect but by the shape of the recorded reason: it is
/// a claim about callers, so it expires silently the moment a fill picker that pre-selects the
/// current colour is added, which is exactly how this dialog behaves in PowerPoint. One comparison
/// that cannot expire was preferred to a premise that must be re-checked whenever a caller appears.</para>
///
/// <para>The same defect class was fixed for a sibling command in r202, whose comment states the
/// consequence exactly ("that push clears redo"), and FreeX drove it across its whole command
/// surface in r208-r211. This command was left behind - the recurring shape of this review.</para>
///
/// <para>The guard is deliberately conservative: only fills comparable exactly (both null, both
/// None, or two Solids with the same resolved colour, alpha and theme reference) count as
/// equivalent. Gradients and pictures still report an effect, because a redundant undo entry is a
/// nuisance while a swallowed edit is data loss.</para>
/// </summary>
public sealed class R479_RepickingTheSameFillKeepsRedoTests
{
    private static (EditingSession Session, Presentation Presentation, SlideShape Shape) Make()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id = 7,
            Name = "Box",
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 685800,
        };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        presentation.Slides.Add(new Slide());

        return (new EditingSession(presentation, new PresentationCommandBus(presentation)), presentation, shape);
    }

    private static string Describe(Presentation p)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("slides=").Append(p.Slides.Count).Append('|');
        foreach (var slide in p.Slides)
            foreach (var shape in slide.Shapes)
                sb.Append(shape.Id).Append(':').Append(shape.Fill?.GetType().Name ?? "~").Append(';');
        return sb.ToString();
    }

    /// <summary>Leaves a redo pending, the state the defect destroyed.</summary>
    private static void ArmRedo(EditingSession session)
    {
        session.DuplicateCurrentSlide();
        session.CanUndo.Should().BeTrue("the setup edit must be undoable");
        session.Undo();
        session.CanRedo.Should().BeTrue("undoing the setup edit must leave a redo pending");

        // Duplicate+undo moves the current slide; without this the fill command targets a slide
        // that does not hold the shape and every assertion below passes vacuously.
        session.SelectSlide(0);
    }

    [Fact]
    public void RepickingTheSameSolidColourKeepsRedoAvailable()
    {
        var (session, presentation, shape) = Make();
        shape.Fill = new ShapeFill.Solid(new SrgbColor(0x40, 0x80, 0xC0));

        ArmRedo(session);
        session.Select(shape.Id);
        var before = Describe(presentation);

        // A NEW instance carrying the same colour: what the dialog builds when the user presses OK
        // without touching the colour. Reference equality would not catch this.
        session.SetSelectedFill(new ShapeFill.Solid(new SrgbColor(0x40, 0x80, 0xC0)));

        Describe(presentation).Should().Be(before, "re-picking the same colour changes nothing");
        session.CanRedo.Should().BeTrue(
            "a command that changed nothing must not be pushed, because pushing clears redo");
    }

    [Fact]
    public void ReapplyingAnAbsentFillKeepsRedoAvailable()
    {
        var (session, presentation, shape) = Make();

        ArmRedo(session);
        session.Select(shape.Id);
        var before = Describe(presentation);

        session.SetSelectedFill(shape.Fill);

        Describe(presentation).Should().Be(before);
        session.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void ChangingTheFillStillTakesEffectAndStillClearsRedo()
    {
        // The narrowness check. Suppressing a real edit would be far worse than the defect fixed
        // here, so a genuine change must still apply -- and must still clear redo, as any real edit does.
        var (session, presentation, shape) = Make();
        shape.Fill = new ShapeFill.Solid(new SrgbColor(0x40, 0x80, 0xC0));

        ArmRedo(session);
        session.Select(shape.Id);

        session.SetSelectedFill(new ShapeFill.Solid(new SrgbColor(0xFF, 0x00, 0x00)));

        var applied = presentation.Slides[0].Shapes[0].Fill.Should().BeOfType<ShapeFill.Solid>().Subject;
        applied.Color.Resolved.Should().Be(new SrgbColor(0xFF, 0x00, 0x00), "a real change must be applied");
        session.CanRedo.Should().BeFalse("a real edit legitimately clears the redo stack");
    }

    [Fact]
    public void AThemeReferenceIsNotEquivalentToALiteralColourThatResolvesTheSame()
    {
        // Two colours can resolve identically today and diverge the moment the theme changes, so
        // they are not the same fill and the edit must apply.
        var (session, presentation, shape) = Make();
        var literal = new SrgbColor(0x40, 0x80, 0xC0);
        shape.Fill = new ShapeFill.Solid(new ThemeAwareColor(literal));

        ArmRedo(session);
        session.Select(shape.Id);

        var themed = new ThemeAwareColor(literal, new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });
        session.SetSelectedFill(new ShapeFill.Solid(themed));

        var applied = presentation.Slides[0].Shapes[0].Fill.Should().BeOfType<ShapeFill.Solid>().Subject;
        applied.Color.SchemeColor.Should().NotBeNull("switching a literal colour to a theme reference is a real edit");
    }
}
