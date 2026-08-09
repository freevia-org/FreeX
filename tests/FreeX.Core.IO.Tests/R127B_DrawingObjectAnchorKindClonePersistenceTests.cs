using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R127B (ScopeAudit follow-up to R127-editas-shift-gate): the landed R127 fix taught
/// <c>RowColumnShiftHelpers.ShiftPictures</c>/<c>ShiftDrawingShapes</c>/<c>ShiftTextBoxes</c> to
/// respect a captured <see cref="ChartModel.DrawingAnchorKind"/>-equivalent field on
/// <see cref="PictureModel"/>/<see cref="DrawingShapeModel"/>/<see cref="TextBoxModel"/> (see
/// R127_DrawingObjectAnchorKindShiftTests), but two OTHER paths that already carried the analogous
/// field for charts were never given parity for these three sibling types:
///
///   1. <see cref="DuplicateSheetDrawingCloner"/>.CloneTextBox/CloneDrawingShape/ClonePicture omitted
///      the DrawingAnchorKind copy entirely, so Duplicate Sheet (and, by extension, Ctrl+C/Ctrl+V and
///      paste-carry, which reuse the same clone methods) silently reverted a duplicated
///      oneCellAnchor/absoluteAnchor object back to the TwoCell default -- reintroducing the original
///      r127 move/resize defect for the COPY even though the source object stayed protected.
///
///   2. <see cref="NativeJsonVisualDtoMapper"/>'s PictureDto/TextBoxDto/DrawingShapeDto never carried
///      DrawingAnchorKind at all (unlike NativeJsonAdapter's ChartDto, which already round-trips
///      ChartModel.DrawingAnchorKind), so loading an .xlsx with a captured oneCellAnchor/absoluteAnchor
///      object, Save As FreeX-native .fxl, then reopening silently reverted the kind to TwoCell too.
///
/// This class pins both paths fixed and closed, for all three object types, plus a no-regression
/// sibling proving the ordinary (unset/TwoCell) case is unaffected by either fix.
/// </summary>
public sealed class R127B_DrawingObjectAnchorKindClonePersistenceTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    // ── Path 1: DuplicateSheetDrawingCloner (Duplicate Sheet / Ctrl+C-Ctrl+V / paste-carry) ────────

    [Fact]
    public void DuplicateSheet_OneCellAnchorTextBox_PreservesDrawingAnchorKindOnCopy()
    {
        var workbook = new Workbook("TextBoxCloneEditAs");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Name = "Note",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Text = "hello",
            Width = 180,
            Height = 80,
            DrawingAnchorKind = ChartDrawingAnchorKind.OneCell
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1].TextBoxes.Should().ContainSingle().Subject;
        copy.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.OneCell,
            "a duplicated oneCellAnchor text box must keep its \"move but don't size\" kind, " +
            "not silently revert to the TwoCell default");
    }

    [Fact]
    public void DuplicateSheet_AbsoluteAnchorShape_PreservesDrawingAnchorKindOnCopy()
    {
        var workbook = new Workbook("ShapeCloneEditAs");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Box",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            Width = 120,
            Height = 70,
            DrawingAnchorKind = ChartDrawingAnchorKind.Absolute
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1].DrawingShapes.Should().ContainSingle().Subject;
        copy.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.Absolute,
            "a duplicated absoluteAnchor shape must keep its \"don't move or size\" kind, " +
            "not silently revert to the TwoCell default");
    }

    [Fact]
    public void DuplicateSheet_OneCellAnchorPicture_PreservesDrawingAnchorKindOnCopy()
    {
        var workbook = new Workbook("PictureCloneEditAs");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Pic",
            Anchor = new CellAddress(sheet.Id, 3, 3),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
            Width = 100,
            Height = 60,
            DrawingAnchorKind = ChartDrawingAnchorKind.OneCell
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1].Pictures.Should().ContainSingle().Subject;
        copy.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.OneCell,
            "a duplicated oneCellAnchor picture must keep its \"move but don't size\" kind, " +
            "not silently revert to the TwoCell default");
    }

    // Sibling no-regression: the ordinary freshly-inserted (unset/TwoCell) case must still clone as
    // TwoCell -- proves the fix did not accidentally break the common path.
    [Fact]
    public void DuplicateSheet_DefaultTwoCellAnchorObjects_StayTwoCellOnCopy_NoRegression()
    {
        var workbook = new Workbook("DefaultCloneEditAs");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Width = 180, Height = 80 });
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Width = 120, Height = 70 });
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            ImageBytes = [1],
            ContentType = "image/png",
            Width = 100,
            Height = 60
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copySheet = workbook.Sheets[1];
        copySheet.TextBoxes.Should().ContainSingle().Subject.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.TwoCell);
        copySheet.DrawingShapes.Should().ContainSingle().Subject.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.TwoCell);
        copySheet.Pictures.Should().ContainSingle().Subject.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.TwoCell);
    }

    // ── Path 2: NativeJsonAdapter (.fxl) save/reload round trip ─────────────────────────────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrips_OneCellAnchorTextBox()
    {
        var workbook = new Workbook("TextBoxFxlEditAs");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Text = "note",
            Width = 180,
            Height = 80,
            DrawingAnchorKind = ChartDrawingAnchorKind.OneCell
        });

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0).TextBoxes.Should().ContainSingle().Subject;
        loaded.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.OneCell,
            "a oneCellAnchor text box's kind must survive a native .fxl save/reload cycle");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_AbsoluteAnchorShape()
    {
        var workbook = new Workbook("ShapeFxlEditAs");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            Width = 120,
            Height = 70,
            DrawingAnchorKind = ChartDrawingAnchorKind.Absolute
        });

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.Absolute,
            "an absoluteAnchor shape's kind must survive a native .fxl save/reload cycle");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_OneCellAnchorPicture()
    {
        var workbook = new Workbook("PictureFxlEditAs");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 3),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
            Width = 100,
            Height = 60,
            DrawingAnchorKind = ChartDrawingAnchorKind.OneCell
        });

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        loaded.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.OneCell,
            "a oneCellAnchor picture's kind must survive a native .fxl save/reload cycle");
    }

    // Sibling no-regression: pre-existing .fxl behavior for the ordinary (unset/TwoCell) case is
    // unaffected -- proves the new field's default keeps old semantics for objects that never set it.
    [Fact]
    public void NativeJsonAdapter_RoundTrips_DefaultTwoCellAnchorObjects_NoRegression()
    {
        var workbook = new Workbook("DefaultFxlEditAs");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Width = 180, Height = 80 });
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Width = 120, Height = 70 });
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            ImageBytes = [1],
            ContentType = "image/png",
            Width = 100,
            Height = 60
        });

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loadedSheet = adapter.Load(stream).GetSheetAt(0);
        loadedSheet.TextBoxes.Should().ContainSingle().Subject.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.TwoCell);
        loadedSheet.DrawingShapes.Should().ContainSingle().Subject.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.TwoCell);
        loadedSheet.Pictures.Should().ContainSingle().Subject.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.TwoCell);
    }
}
