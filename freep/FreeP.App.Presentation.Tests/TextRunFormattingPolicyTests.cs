namespace FreeP.App.Compositor.Tests;

public sealed class TextRunFormattingPolicyTests
{
    [Theory]
    [InlineData(TableCellTextFormatKind.Bold)]
    [InlineData(TableCellTextFormatKind.Italic)]
    [InlineData(TableCellTextFormatKind.Underline)]
    [InlineData(TableCellTextFormatKind.Strikethrough)]
    [InlineData(TableCellTextFormatKind.Superscript)]
    [InlineData(TableCellTextFormatKind.Subscript)]
    public void BooleanFormats_RoundTripThroughCanonicalRunPolicy(TableCellTextFormatKind kind)
    {
        var run = new Run { Text = "text" };

        TextRunFormattingPolicy.Get(run, kind).Should().BeFalse();
        TextRunFormattingPolicy.Set(run, kind, true);
        TextRunFormattingPolicy.Get(run, kind).Should().BeTrue();

        switch (kind)
        {
            case TableCellTextFormatKind.Bold:
                run.BoldSet.Should().BeTrue();
                break;
            case TableCellTextFormatKind.Italic:
                run.ItalicSet.Should().BeTrue();
                break;
            case TableCellTextFormatKind.Underline:
                run.UnderlineStyleToken.Should().Be("sng");
                break;
            case TableCellTextFormatKind.Strikethrough:
                run.StrikeStyleToken.Should().Be("sngStrike");
                break;
            case TableCellTextFormatKind.Superscript:
                run.BaselineOffset.Should().Be(10000);
                break;
            case TableCellTextFormatKind.Subscript:
                run.BaselineOffset.Should().Be(-10000);
                break;
        }

        TextRunFormattingPolicy.Set(run, kind, false);
        TextRunFormattingPolicy.Get(run, kind).Should().BeFalse();
    }

    [Fact]
    public void ValueFormats_AssignAndClearCanonicalRunValues()
    {
        var run = new Run { Text = "text" };
        var color = new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0x33));

        TextRunFormattingPolicy.SetValue(run, TableCellTextValueFormatKind.FontFamily, "Aptos");
        TextRunFormattingPolicy.SetValue(run, TableCellTextValueFormatKind.FontSize, 18d);
        TextRunFormattingPolicy.SetValue(run, TableCellTextValueFormatKind.Color, color);

        run.FontFamily.Should().Be("Aptos");
        run.FontSizePt.Should().Be(18d);
        run.Color.Should().Be(color);

        TextRunFormattingPolicy.SetValue(run, TableCellTextValueFormatKind.FontFamily, null);
        TextRunFormattingPolicy.SetValue(run, TableCellTextValueFormatKind.FontSize, null);
        TextRunFormattingPolicy.SetValue(run, TableCellTextValueFormatKind.Color, null);

        run.FontFamily.Should().BeNull();
        run.FontSizePt.Should().BeNull();
        run.Color.Should().BeNull();
    }

    [Fact]
    public void ShapeAndTablePlanners_AdoptOneRunFormattingPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var presentationDirectory = Path.Combine(root, "freep", "FreeP.App.Presentation");
        var inCanvasSource = File.ReadAllText(Path.Combine(presentationDirectory, "InCanvasTextEditPlanner.cs"));
        var tableSource = File.ReadAllText(Path.Combine(presentationDirectory, "TableCellEditPlanner.cs"));
        var policySource = File.ReadAllText(Path.Combine(presentationDirectory, "TextRunFormattingPolicy.cs"));

        inCanvasSource.Should().Contain("TextRunFormattingPolicy.Get")
            .And.Contain("TextRunFormattingPolicy.Set")
            .And.Contain("TextRunFormattingPolicy.SetValue");
        tableSource.Should().Contain("TextBodyRunMutationPlanner.ToggleTextFormat")
            .And.Contain("TextBodyRunMutationPlanner.ApplyValueFormat")
            .And.NotContain("GetRunFormat(")
            .And.NotContain("SetRunFormat(")
            .And.NotContain("SetRunValueFormat(");
        policySource.Should().Contain("internal static class TextRunFormattingPolicy");
    }
}
