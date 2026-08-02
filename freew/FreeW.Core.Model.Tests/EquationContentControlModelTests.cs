namespace FreeW.Core.Model.Tests;

public sealed class EquationContentControlModelTests
{
    [Fact]
    public void InlineEquationRun_CanCarryExplicitEquationControlKind()
    {
        var run = Run.FromEquation(Equation.FromText("x+1"));
        run.Control = new ContentControl(
            ContentControlKind.Equation,
            Tag: "EquationControl",
            Alias: "Inline equation");

        run.Equation!.LinearText.Should().Be("x+1");
        run.Control.Should().Be(new ContentControl(
            ContentControlKind.Equation,
            Tag: "EquationControl",
            Alias: "Inline equation"));
        Run.RichTextControl("ordinary", tag: "RichText").Control!.Kind
            .Should().Be(ContentControlKind.RichText);
    }
}
