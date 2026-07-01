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
