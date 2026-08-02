using Avalonia;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewPictureRenderingTests
{
    [Fact]
    public void Picture_transform_applies_both_flips_before_clockwise_rotation()
    {
        var rect = new Rect(10, 20, 80, 60);
        var image = new InlineImage([], 60, 45)
        {
            FlipH = true,
            FlipV = true,
            RotationAngle = 90,
        };

        var transform = DocumentView.BuildPictureTransform(rect, image);

        AssertPoint(transform.Transform(new Point(30, 35)), new Point(35, 70));
        AssertPoint(transform.Transform(new Point(70, 35)), new Point(35, 30));
        AssertPoint(transform.Transform(new Point(30, 65)), new Point(65, 70));
        AssertPoint(transform.Transform(new Point(70, 65)), new Point(65, 30));
    }

    [Fact]
    public void Neutral_picture_keeps_identity_transform_and_no_border()
    {
        var view = new DocumentView();
        var image = new InlineImage([], 48, 48);

        DocumentView.BuildPictureTransform(new Rect(10, 20, 64, 64), image)
            .Should().Be(Matrix.Identity);
        view.BuildPictureBorderPen(image).Should().BeNull();
        DocumentView.BuildPictureDashStyle(null).Should().BeNull();
        DocumentView.BuildPictureDashStyle("solid").Should().BeNull();
    }

    [Fact]
    public void Picture_border_preserves_authored_color_width_and_minimum()
    {
        var view = new DocumentView();
        var authored = new InlineImage([], 48, 48)
        {
            BorderColorHex = "C02040",
            BorderWidthPt = 2.25,
            BorderDash = "lgDashDot",
        };
        var minimum = new InlineImage([], 48, 48)
        {
            BorderColorHex = "#102030",
            BorderWidthPt = 0.1,
        };

        var authoredPen = view.BuildPictureBorderPen(authored);
        authoredPen.Should().NotBeNull();
        authoredPen!.Thickness.Should().BeApproximately(3, 0.001, "2.25 points is 3 DIP at 96 DPI");
        authoredPen.Brush.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Color.Parse("#C02040"));
        authoredPen.DashStyle!.Dashes.Should().Equal(8, 2, 1, 2);

        view.BuildPictureBorderPen(minimum)!.Thickness.Should().BeApproximately(1, 0.001,
            "an active Word picture border has a 0.75-point minimum");
    }

    [Theory]
    [InlineData("dash", new double[] { 4, 3 })]
    [InlineData("sysDash", new double[] { 4, 3 })]
    [InlineData("dot", new double[] { 1, 2 })]
    [InlineData("sysDot", new double[] { 1, 2 })]
    [InlineData("dashDot", new double[] { 4, 2, 1, 2 })]
    [InlineData("sysDashDot", new double[] { 4, 2, 1, 2 })]
    [InlineData("lgDash", new double[] { 8, 3 })]
    [InlineData("lgDashDot", new double[] { 8, 2, 1, 2 })]
    [InlineData("lgDashDotDot", new double[] { 8, 2, 1, 2, 1, 2 })]
    public void Picture_dash_tokens_map_to_live_pen_contract(string token, double[] expected)
    {
        DocumentView.BuildPictureDashStyle(token)!.Dashes.Should().Equal(expected);
    }

    [Fact]
    public void All_live_picture_hosts_pass_the_exact_model_to_the_shared_draw_owner()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        source.Should().Contain("foreach (var (rect, bitmap, model, reflectionPreset) in _images)");
        source.Should().Contain("DrawFloatingImage(context, rect, bitmap, model, reflectionPreset);");
        source.Should().Contain("DrawFloatingImage(context, image.Rect, image.Image, image.Model, image.ReflectionPreset);");
        source.Should().Contain("DrawFloatingImage(context, rect, DecodeRenderedImage(image), image, image.ReflectionPreset);");
        source.Should().Contain("childData.ImageModel = img;");
        source.Should().Contain("DrawFloatingImage(context, child.Rect, child.Bitmap, image, image.ReflectionPreset);");
    }

    private static void AssertPoint(Point actual, Point expected)
    {
        actual.X.Should().BeApproximately(expected.X, 0.001);
        actual.Y.Should().BeApproximately(expected.Y, 0.001);
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeSegments]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(relativeSegments)} from test output.");
    }
}
