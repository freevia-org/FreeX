namespace FreeW.Core.Model.Tests;

public sealed class PlainTextContentControlMultiLineModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Factory_PreservesExplicitMultiLineState(bool multiLine)
    {
        var run = Run.PlainTextControl(
            "Editable text",
            tag: "PlainText",
            alias: "Plain text",
            multiLine: multiLine);

        run.Control.Should().Be(new ContentControl(
            ContentControlKind.PlainText,
            Tag: "PlainText",
            Alias: "Plain text",
            PlainTextMultiLine: multiLine));
    }

    [Fact]
    public void Factory_LeavesMultiLineAbsentByDefault()
    {
        Run.PlainTextControl("Editable text").Control!.PlainTextMultiLine.Should().BeNull();
    }
}
