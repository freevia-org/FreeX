using FreeP.App.Compositor.MathLayout;

namespace FreeP.App.Compositor.Tests;

public sealed class ResolvedRunTextFragmentTests
{
    [Fact]
    public void WithText_ReplacesOnlyTextAndPreservesEveryResolvedProperty()
    {
        var fill = new ResolvedFill.Solid(new SrgbColor(12, 34, 56), 210);
        var outline = new ResolvedOutline.Visible(
            new SrgbColor(65, 43, 21),
            2.5,
            OutlineDash.Dash,
            180);
        var shadow = new ResolvedRunShadow
        {
            Color = new SrgbColor(1, 2, 3),
            Alpha = 170,
            BlurDip = 4.5,
            DistDip = 6.5,
            DirDeg = 45,
        };
        var reflection = new ResolvedRunReflection
        {
            Alpha = 160,
            BlurDip = 3.5,
            DistDip = 5.5,
            DirDeg = 90,
            ScaleY = -0.75,
            EndPos = 0.8,
        };
        var glow = new ResolvedRunGlow
        {
            Color = new SrgbColor(7, 8, 9),
            Alpha = 150,
            RadiusDip = 8.5,
        };
        var softEdge = new ResolvedRunSoftEdge { RadiusDip = 1.5 };
        var mathLayout = new MathBox.Container();
        var source = new ResolvedRun
        {
            Text = "source",
            FontFamily = "Aptos Display",
            FontSizePt = 27.5,
            BaselineOffset = 2500,
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            RightToLeft = true,
            Color = new SrgbColor(10, 20, 30),
            TextFill = fill,
            TextOutline = outline,
            TextShadow = shadow,
            TextReflection = reflection,
            TextGlow = glow,
            TextSoftEdge = softEdge,
            MathLayout = mathLayout,
        };

        var fragment = source.WithText("fragment");

        fragment.Should().NotBeSameAs(source);
        fragment.Text.Should().Be("fragment");
        source.Text.Should().Be("source");
        fragment.Should().BeEquivalentTo(
            source,
            options => options.Excluding(member => member.Path == nameof(ResolvedRun.Text)));
        fragment.TextFill.Should().BeSameAs(fill);
        fragment.TextOutline.Should().BeSameAs(outline);
        fragment.TextShadow.Should().BeSameAs(shadow);
        fragment.TextReflection.Should().BeSameAs(reflection);
        fragment.TextGlow.Should().BeSameAs(glow);
        fragment.TextSoftEdge.Should().BeSameAs(softEdge);
        fragment.MathLayout.Should().BeSameAs(mathLayout);
        fragment.RightToLeft.Should().BeTrue();
    }

    [Fact]
    public void RendererFragmentPaths_DelegateResolvedRunOwnershipToPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var rendererSources = new[]
        {
            File.ReadAllText(Path.Combine(
                root,
                "freep",
                "FreeP.App.Rendering.Wpf",
                "SlideCanvas.cs")),
            File.ReadAllText(Path.Combine(
                root,
                "freep",
                "FreeP.App.Rendering.Avalonia",
                "SlideCanvas.cs")),
        };

        foreach (var source in rendererSources)
        {
            source.Should().Contain("var glyphRun = run.WithText(glyph.Text);");
            source.Should().NotContain("CopyRunWithText");
            source.Should().NotContain("TextFill = run.TextFill");
            source.Should().NotContain("TextOutline = run.TextOutline");
            source.Should().NotContain("TextSoftEdge = run.TextSoftEdge");
        }
    }
}
