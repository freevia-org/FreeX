using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class DrawingTargetResolverTests
{
    [Fact]
    public void GetTargetPicture_PrefersLastPictureAnchoredAtSelectedCell()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selected = new CellAddress(sheet.Id, 2, 2);
        var first = new PictureModel { Anchor = selected };
        var lastAtSelection = new PictureModel { Anchor = selected };
        var finalPicture = new PictureModel { Anchor = new CellAddress(sheet.Id, 5, 5) };
        sheet.Pictures.AddRange([first, lastAtSelection, finalPicture]);

        DrawingTargetResolver.GetTargetPicture(sheet, selected).Should().BeSameAs(lastAtSelection);
    }

    [Fact]
    public void GetTargetPicture_FallsBackToLastPictureWhenSelectionHasNoAnchorMatch()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var last = new PictureModel { Anchor = new CellAddress(sheet.Id, 5, 5) };
        sheet.Pictures.Add(new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1) });
        sheet.Pictures.Add(last);

        DrawingTargetResolver.GetTargetPicture(sheet, new CellAddress(sheet.Id, 2, 2)).Should().BeSameAs(last);
    }

    [Fact]
    public void GetTargetPicture_CanRequireExactAnchorMatchForContextMenus()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.Pictures.Add(new PictureModel { Anchor = new CellAddress(sheet.Id, 5, 5) });

        DrawingTargetResolver.GetTargetPicture(
                sheet,
                new CellAddress(sheet.Id, 2, 2),
                allowFallback: false)
            .Should()
            .BeNull();
    }

    [Fact]
    public void GetTargetPicture_SkipsHiddenPictures()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var visible = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        sheet.Pictures.Add(visible);
        sheet.Pictures.Add(new PictureModel { Anchor = new CellAddress(sheet.Id, 2, 2), IsVisible = false });

        DrawingTargetResolver.GetTargetPicture(sheet, new CellAddress(sheet.Id, 2, 2)).Should().BeSameAs(visible);
    }

    [Fact]
    public void GetTargetDrawingShape_PrefersLastShapeAnchoredAtSelectedCell()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selected = new CellAddress(sheet.Id, 3, 3);
        var expected = new DrawingShapeModel { Anchor = selected };
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = selected });
        sheet.DrawingShapes.Add(expected);
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 4, 4) });

        DrawingTargetResolver.GetTargetDrawingShape(sheet, selected).Should().BeSameAs(expected);
    }

    [Fact]
    public void GetTargetDrawingShape_SkipsHiddenShapes()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selected = new CellAddress(sheet.Id, 3, 3);
        var visible = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        sheet.DrawingShapes.Add(visible);
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = selected, IsVisible = false });

        DrawingTargetResolver.GetTargetDrawingShape(sheet, selected).Should().BeSameAs(visible);
    }

    [Fact]
    public void GetTargetDrawingObject_DefaultsToShapeBeforeTextboxWhenShapesExist()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 111,
            Height = 55,
            RotationDegrees = 15,
            FillColor = new CellColor(1, 2, 3),
            OutlineColor = new CellColor(4, 5, 6),
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25)
        };
        sheet.DrawingShapes.Add(shape);
        sheet.TextBoxes.Add(new TextBoxModel { Anchor = new CellAddress(sheet.Id, 2, 2) });

        var target = DrawingTargetResolver.GetTargetDrawingObject(sheet, selectedAnchor: new CellAddress(sheet.Id, 2, 2));

        target.Should().NotBeNull();
        target!.Kind.Should().Be(DrawingObjectTargetKind.Shape);
        target.Id.Should().Be(shape.Id);
        target.Width.Should().Be(111);
        target.Height.Should().Be(55);
        target.RotationDegrees.Should().Be(15);
        target.FillColor.Should().Be(new CellColor(1, 2, 3));
        target.OutlineColor.Should().Be(new CellColor(4, 5, 6));
        target.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25));
        target.OutlineThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25));
    }

    [Fact]
    public void GetTargetDrawingObject_HonorsPreferredTextbox()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selected = new CellAddress(sheet.Id, 2, 2);
        var textBox = new TextBoxModel { Anchor = selected, Width = 90, Height = 40 };
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) });
        sheet.TextBoxes.Add(textBox);

        var target = DrawingTargetResolver.GetTargetDrawingObject(
            sheet,
            selected,
            DrawingObjectTargetKind.TextBox);

        target.Should().NotBeNull();
        target!.Kind.Should().Be(DrawingObjectTargetKind.TextBox);
        target.Id.Should().Be(textBox.Id);
        target.Width.Should().Be(90);
        target.Height.Should().Be(40);
    }

    [Fact]
    public void GetTargetDrawingObject_HonorsSelectedPictureWhenTransformsIncludePictures()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selected = new CellAddress(sheet.Id, 2, 2);
        var picture = new PictureModel
        {
            Anchor = selected,
            Width = 320,
            Height = 180,
            RotationDegrees = 30
        };
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = selected });
        sheet.Pictures.Add(picture);

        var target = DrawingTargetResolver.GetTargetDrawingObject(
            sheet,
            selected,
            DrawingObjectTargetKind.Picture,
            picture.Id,
            includePictures: true);

        target.Should().NotBeNull();
        target!.Kind.Should().Be(DrawingObjectTargetKind.Picture);
        target.Id.Should().Be(picture.Id);
        target.Width.Should().Be(320);
        target.Height.Should().Be(180);
        target.RotationDegrees.Should().Be(30);
    }

    [Fact]
    public void GetTargetDrawingObject_DoesNotReturnPicturesForShapeTextFormattingByDefault()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selected = new CellAddress(sheet.Id, 2, 2);
        var picture = new PictureModel { Anchor = selected };
        sheet.Pictures.Add(picture);

        DrawingTargetResolver.GetTargetDrawingObject(
                sheet,
                selected,
                DrawingObjectTargetKind.Picture,
                picture.Id)
            .Should()
            .BeNull();
    }

    [Fact]
    public void GetTargetDrawingObject_CanRequireExactAnchorMatchForShapeTextContextMenus()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 5, 5) });

        DrawingTargetResolver.GetTargetDrawingObject(
                sheet,
                new CellAddress(sheet.Id, 2, 2),
                allowFallback: false)
            .Should()
            .BeNull();
    }

    [Fact]
    public void GetTargetDrawingZOrderObject_PrefersFrontMostSelectedDrawingObject()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selected = new CellAddress(sheet.Id, 2, 2);
        var shape = new DrawingShapeModel { Anchor = selected };
        var picture = new PictureModel { Anchor = selected };
        var textBox = new TextBoxModel { Anchor = selected };
        sheet.DrawingShapes.Add(shape);
        sheet.Pictures.Add(picture);
        sheet.TextBoxes.Add(textBox);
        sheet.DrawingObjectZOrder.AddRange(
        [
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, textBox.Id)
        ]);

        var target = DrawingTargetResolver.GetTargetDrawingZOrderObject(sheet, selected);

        target.Should().NotBeNull();
        target!.Kind.Should().Be(SelectionPaneObjectKind.TextBox);
        target.Id.Should().Be(textBox.Id);
    }

    [Fact]
    public void GetTargetDrawingZOrderObject_HonorsPreferredKindForGroupedArrangeCommands()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selected = new CellAddress(sheet.Id, 2, 2);
        var picture = new PictureModel { Anchor = selected };
        var textBox = new TextBoxModel { Anchor = selected };
        sheet.Pictures.Add(picture);
        sheet.TextBoxes.Add(textBox);
        sheet.DrawingObjectZOrder.AddRange(
        [
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, textBox.Id)
        ]);

        var target = DrawingTargetResolver.GetTargetDrawingZOrderObject(
            sheet,
            selected,
            SelectionPaneObjectKind.Picture);

        target.Should().NotBeNull();
        target!.Kind.Should().Be(SelectionPaneObjectKind.Picture);
        target.Id.Should().Be(picture.Id);
    }

    [Fact]
    public void ResolverScansVisibleItemsWithoutAllocatingFilteredLists()
    {
        var source = DialogSourceTestSupport.ReadHostSources("DrawingTargetResolver.cs");

        source.Should().NotContain(".Where(");
        source.Should().NotContain(".ToList()");
        source.Should().NotContain("LastOrDefault");
    }

    [Fact]
    public void GetTargetPicture_UsesFastReverseScanForLargeDrawingLists()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (var index = 1u; index <= 5_000; index++)
        {
            sheet.Pictures.Add(new PictureModel { Anchor = new CellAddress(sheet.Id, index, 1) });
        }

        var selected = new CellAddress(sheet.Id, 5_000, 1);

        var stopwatch = Stopwatch.StartNew();
        for (var iteration = 0; iteration < 10_000; iteration++)
        {
            DrawingTargetResolver.GetTargetPicture(sheet, selected).Should().BeSameAs(sheet.Pictures[^1]);
        }

        stopwatch.Stop();
        Console.WriteLine($"Drawing target reverse lookup: {stopwatch.ElapsedMilliseconds}ms for 10000 lookups");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(250);
    }
}
