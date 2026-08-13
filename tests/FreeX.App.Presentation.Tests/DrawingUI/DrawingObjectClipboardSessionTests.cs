using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class DrawingObjectClipboardSessionTests
{
    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public void TryCapture_StoresSupportedDrawingObject(SelectionPaneObjectKind kind)
    {
        var session = new DrawingObjectClipboardSession();
        var sheetId = SheetId.New();
        var objectId = Guid.NewGuid();

        session.TryCapture(sheetId, kind, objectId, isCut: true).Should().BeTrue();
        session.Content.Should().Be(new DrawingObjectClipboardSnapshot(sheetId, kind, objectId, IsCut: true));
    }

    [Fact]
    public void TryCapture_RejectsMissingAndUnsupportedSelectionsWithoutReplacingContent()
    {
        var session = new DrawingObjectClipboardSession();
        var originalId = Guid.NewGuid();
        session.TryCapture(SheetId.New(), SelectionPaneObjectKind.Shape, originalId).Should().BeTrue();

        session.TryCapture(SheetId.New(), (SelectionPaneObjectKind)999, Guid.NewGuid()).Should().BeFalse();
        session.TryCapture(SheetId.New(), SelectionPaneObjectKind.Shape, Guid.Empty).Should().BeFalse();

        session.Content!.ObjectId.Should().Be(originalId);
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public void TryCaptureExisting_RequiresObjectOnSourceSheet(SelectionPaneObjectKind kind)
    {
        var sheet = new Workbook("clipboard").AddSheet("Sheet1");
        var (objectId, _) = AddObject(sheet, kind);
        var session = new DrawingObjectClipboardSession();

        session.TryCaptureExisting(sheet, kind, objectId).Should().BeTrue();
        session.Clear();
        session.TryCaptureExisting(sheet, kind, Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void CreatePasteCommand_UsesSnapshotAndCutCompletionClearsOnlyMatchingContent()
    {
        var session = new DrawingObjectClipboardSession();
        var sourceSheetId = SheetId.New();
        var destinationSheetId = SheetId.New();
        var objectId = Guid.NewGuid();
        session.TryCapture(sourceSheetId, SelectionPaneObjectKind.Picture, objectId, isCut: true);
        var snapshot = session.Content!;

        var command = DrawingObjectClipboardSession.CreatePasteCommand(snapshot, destinationSheetId);

        command.Should().BeOfType<DuplicateDrawingObjectCommand>();
        session.CompletePaste(snapshot);
        session.HasContent.Should().BeFalse();
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public void ResolveAnchor_ReturnsPastedObjectAnchor(SelectionPaneObjectKind kind)
    {
        var sheet = new Workbook("clipboard").AddSheet("Sheet1");
        var (objectId, expectedAnchor) = AddObject(sheet, kind);

        DrawingObjectClipboardSession.ResolveAnchor(sheet, sheet.Id, kind, objectId)
            .Should().Be(expectedAnchor);
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Chart)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    public void CreatePasteSelectionPlan_CarriesKindIdentityAndResolvedAnchor(SelectionPaneObjectKind kind)
    {
        var sheet = new Workbook("clipboard").AddSheet("Sheet1");
        var (objectId, expectedAnchor) = AddObject(sheet, kind);

        DrawingObjectClipboardSession.CreatePasteSelectionPlan(sheet, sheet.Id, kind, objectId)
            .Should().Be(new DrawingObjectPasteSelectionPlan(kind, objectId, expectedAnchor));
    }

    [Fact]
    public void WpfAndAvaloniaHosts_UseSharedDrawingObjectClipboardSession()
    {
        var repoRoot = RepositoryFileLocator.FindDirectory("src");
        var hostFiles = new[]
        {
            Path.Combine(repoRoot, "FreeX.App.Host", "MainWindow.ClipboardCommands.cs"),
            Path.Combine(repoRoot, "FreeX.App.Avalonia", "MainWindow.DrawingObjectClipboard.cs"),
            Path.Combine(repoRoot, "FreeX.App.Avalonia", "MainWindow.cs")
        };

        var sources = hostFiles.Select(File.ReadAllText).ToArray();
        sources.Should().OnlyContain(source => !source.Contains("InternalObjectClipboard", StringComparison.Ordinal));
        sources.Should().OnlyContain(source => !source.Contains("new DuplicateDrawingObjectCommand", StringComparison.Ordinal));
        string.Concat(sources).Should().Contain("DrawingObjectClipboardSession");
        sources.Take(2).Should().OnlyContain(source => source.Contains("CreatePasteSelectionPlan(", StringComparison.Ordinal));
    }

    private static (Guid ObjectId, CellAddress Anchor) AddObject(Sheet sheet, SelectionPaneObjectKind kind)
    {
        var anchor = new CellAddress(sheet.Id, 4, 7);
        return kind switch
        {
            SelectionPaneObjectKind.Chart => AddChart(sheet, anchor),
            SelectionPaneObjectKind.Shape => AddShape(sheet, anchor),
            SelectionPaneObjectKind.Picture => AddPicture(sheet, anchor),
            SelectionPaneObjectKind.TextBox => AddTextBox(sheet, anchor),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static (Guid, CellAddress) AddChart(Sheet sheet, CellAddress anchor)
    {
        var chart = new ChartModel { DataRange = new GridRange(anchor, anchor) };
        sheet.Charts.Add(chart);
        return (chart.Id, anchor);
    }

    private static (Guid, CellAddress) AddShape(Sheet sheet, CellAddress anchor)
    {
        var shape = new DrawingShapeModel { Anchor = anchor };
        sheet.DrawingShapes.Add(shape);
        return (shape.Id, anchor);
    }

    private static (Guid, CellAddress) AddPicture(Sheet sheet, CellAddress anchor)
    {
        var picture = new PictureModel { Anchor = anchor };
        sheet.Pictures.Add(picture);
        return (picture.Id, anchor);
    }

    private static (Guid, CellAddress) AddTextBox(Sheet sheet, CellAddress anchor)
    {
        var textBox = new TextBoxModel { Anchor = anchor };
        sheet.TextBoxes.Add(textBox);
        return (textBox.Id, anchor);
    }
}
