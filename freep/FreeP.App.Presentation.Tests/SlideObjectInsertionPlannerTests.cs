using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideObjectInsertionPlannerTests
{
    private static EditingSession MakeSession()
    {
        var presentation = Presentation.CreateEmpty();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    [Fact]
    public void BuiltInCommandIds_AreUnique()
    {
        SlideObjectInsertionPlanner.BuiltInCommandIds
            .Should()
            .OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(SlideObjectInsertionPlanner.TextBoxCommandId, SlideObjectInsertionKind.TextBox)]
    [InlineData(SlideObjectInsertionPlanner.RectangleCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.EllipseCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.PictureCommandId, SlideObjectInsertionKind.Picture)]
    [InlineData(SlideObjectInsertionPlanner.VideoCommandId, SlideObjectInsertionKind.Media)]
    [InlineData(SlideObjectInsertionPlanner.AudioCommandId, SlideObjectInsertionKind.Media)]
    [InlineData(SlideObjectInsertionPlanner.Table3x3CommandId, SlideObjectInsertionKind.Table)]
    [InlineData(SlideObjectInsertionPlanner.Table2x2CommandId, SlideObjectInsertionKind.Table)]
    [InlineData(SlideObjectInsertionPlanner.Table4x4CommandId, SlideObjectInsertionKind.Table)]
    [InlineData(SlideObjectInsertionPlanner.ChartColumnCommandId, SlideObjectInsertionKind.Chart)]
    [InlineData(SlideObjectInsertionPlanner.ChartBarCommandId, SlideObjectInsertionKind.Chart)]
    [InlineData(SlideObjectInsertionPlanner.ChartLineCommandId, SlideObjectInsertionKind.Chart)]
    [InlineData(SlideObjectInsertionPlanner.ChartPieCommandId, SlideObjectInsertionKind.Chart)]
    public void TryCreatePlan_MapsKnownObjectCommandIds(
        string commandId,
        SlideObjectInsertionKind expectedKind)
    {
        SlideObjectInsertionPlanner.TryCreatePlan(commandId, out var plan).Should().BeTrue();
        plan.CommandId.Should().Be(commandId);
        plan.Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData(SlideObjectInsertionPlanner.TextBoxCommandId, DrawingShapeKind.Rectangle, true)]
    [InlineData(SlideObjectInsertionPlanner.RectangleCommandId, DrawingShapeKind.Rectangle, false)]
    [InlineData(SlideObjectInsertionPlanner.EllipseCommandId, DrawingShapeKind.Ellipse, false)]
    public void ApplyCommand_InsertsExpectedAutoShape(
        string commandId,
        DrawingShapeKind expectedShape,
        bool expectsTextBody)
    {
        var editor = MakeSession();
        var before = editor.CurrentSlide!.Shapes.Count;

        var added = SlideObjectInsertionPlanner.ApplyCommand(editor, commandId);

        added.Should().NotBeNull();
        editor.CurrentSlide.Shapes.Should().HaveCount(before + 1);
        added!.Kind.Should().Be(SlideShapeKind.AutoShape);
        added.AutoShapeKind.Should().Be(expectedShape);
        (added.TextBody is not null).Should().Be(expectsTextBody);
    }

    [Theory]
    [InlineData(SlideObjectInsertionPlanner.Table3x3CommandId, 3, 3)]
    [InlineData(SlideObjectInsertionPlanner.Table2x2CommandId, 2, 2)]
    [InlineData(SlideObjectInsertionPlanner.Table4x4CommandId, 4, 4)]
    public void ApplyCommand_InsertsExpectedTable(string commandId, int rows, int columns)
    {
        var editor = MakeSession();

        var added = SlideObjectInsertionPlanner.ApplyCommand(editor, commandId);

        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.Table);
        added.Table.Should().NotBeNull();
        added.Table!.Rows.Should().HaveCount(rows);
        added.Table.ColumnWidthsEmu.Should().HaveCount(columns);
    }

    [Theory]
    [InlineData(SlideObjectInsertionPlanner.ChartColumnCommandId, ChartType.ColumnClustered)]
    [InlineData(SlideObjectInsertionPlanner.ChartBarCommandId, ChartType.BarClustered)]
    [InlineData(SlideObjectInsertionPlanner.ChartLineCommandId, ChartType.Line)]
    [InlineData(SlideObjectInsertionPlanner.ChartPieCommandId, ChartType.Pie)]
    [InlineData(SlideObjectInsertionPlanner.ChartColumnStackedCommandId, ChartType.ColumnStacked)]
    [InlineData(SlideObjectInsertionPlanner.ChartColumnStacked100CommandId, ChartType.ColumnStacked100)]
    [InlineData(SlideObjectInsertionPlanner.ChartBarStackedCommandId, ChartType.BarStacked)]
    [InlineData(SlideObjectInsertionPlanner.ChartBarStacked100CommandId, ChartType.BarStacked100)]
    [InlineData(SlideObjectInsertionPlanner.ChartLineMarkersCommandId, ChartType.LineMarkers)]
    [InlineData(SlideObjectInsertionPlanner.ChartAreaCommandId, ChartType.Area)]
    [InlineData(SlideObjectInsertionPlanner.ChartAreaStackedCommandId, ChartType.AreaStacked)]
    [InlineData(SlideObjectInsertionPlanner.ChartScatterCommandId, ChartType.Scatter)]
    [InlineData(SlideObjectInsertionPlanner.ChartDoughnutCommandId, ChartType.Doughnut)]
    [InlineData(SlideObjectInsertionPlanner.ChartRadarCommandId, ChartType.Radar)]
    [InlineData(SlideObjectInsertionPlanner.ChartBubbleCommandId, ChartType.Bubble)]
    [InlineData(SlideObjectInsertionPlanner.ChartStockCommandId, ChartType.Stock)]
    [InlineData(SlideObjectInsertionPlanner.ChartSurfaceCommandId, ChartType.Surface)]
    [InlineData(SlideObjectInsertionPlanner.ChartSurface3DCommandId, ChartType.Surface3D)]
    public void ApplyCommand_InsertsExpectedChart(string commandId, ChartType chartType)
    {
        var editor = MakeSession();

        var added = SlideObjectInsertionPlanner.ApplyCommand(editor, commandId);

        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.Chart);
        added.Chart.Should().NotBeNull();
        added.Chart!.ChartType.Should().Be(chartType);
    }

    [Fact]
    public void ApplyCommand_PictureWithoutPayload_IsNoOp()
    {
        var editor = MakeSession();
        var before = editor.CurrentSlide!.Shapes.Count;

        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.PictureCommandId);

        added.Should().BeNull();
        editor.CurrentSlide.Shapes.Should().HaveCount(before);
    }

    [Fact]
    public void ApplyCommand_PictureWithPayload_InsertsPicture()
    {
        var editor = MakeSession();
        var payload = SlideObjectInsertionPlanner.CreatePicturePayload(new byte[] { 1, 2, 3 }, "sample.jpg");

        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.PictureCommandId,
            payload);

        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.Picture);
        added.Picture.Should().NotBeNull();
        added.Picture!.Bytes.Should().Equal(1, 2, 3);
        added.Picture.ContentType.Should().Be("image/jpeg");
    }

    [Theory]
    [InlineData(SlideObjectInsertionPlanner.VideoCommandId, true, "clip.mp4", "video/mp4")]
    [InlineData(SlideObjectInsertionPlanner.AudioCommandId, false, "narration.wav", "audio/wav")]
    public void ApplyCommand_MediaWithPayload_InsertsEmbeddedMedia(
        string commandId,
        bool isVideo,
        string fileName,
        string expectedContentType)
    {
        var editor = MakeSession();
        var payload = SlideObjectInsertionPlanner.CreateMediaPayload(
            new byte[] { 9, 8, 7 },
            fileName,
            isVideo);

        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            commandId,
            mediaPayload: payload);

        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.Media);
        added.Media.Should().NotBeNull();
        added.Media!.IsVideo.Should().Be(isVideo);
        added.Media.ContentType.Should().Be(expectedContentType);
        added.Media.Bytes.Should().Equal(9, 8, 7);
    }

    [Theory]
    [InlineData("clip.mp4", true, "video/mp4")]
    [InlineData("clip.mov", true, "video/quicktime")]
    [InlineData("narration.mp3", false, "audio/mpeg")]
    [InlineData("narration.m4a", false, "audio/mp4")]
    [InlineData("unknown", true, "video/mp4")]
    [InlineData("unknown", false, "audio/mpeg")]
    public void InferMediaContentType_MapsCommonExtensions(
        string fileName,
        bool isVideo,
        string expectedContentType)
    {
        SlideObjectInsertionPlanner.InferMediaContentType(fileName, isVideo)
            .Should()
            .Be(expectedContentType);
    }

    [Fact]
    public void ApplyCommand_MediaWithoutPayload_IsNoOp()
    {
        var editor = MakeSession();
        var before = editor.CurrentSlide!.Shapes.Count;

        SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.VideoCommandId)
            .Should()
            .BeNull();

        editor.CurrentSlide.Shapes.Should().HaveCount(before);
    }

    [Theory]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("photo.gif", "image/gif")]
    [InlineData("photo.bmp", "image/bmp")]
    [InlineData("photo.svg", "image/svg+xml")]
    [InlineData("photo.unknown", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    public void InferPictureContentType_MapsSupportedImageExtensions(
        string fileNameOrExtension,
        string expectedContentType)
    {
        SlideObjectInsertionPlanner.InferPictureContentType(fileNameOrExtension)
            .Should()
            .Be(expectedContentType);
    }

    [Fact]
    public void ApplyCommand_UnknownCommandId_IsNoOp()
    {
        var editor = MakeSession();

        SlideObjectInsertionPlanner.ApplyCommand(editor, "freep.unknown")
            .Should()
            .BeNull();
    }
}
