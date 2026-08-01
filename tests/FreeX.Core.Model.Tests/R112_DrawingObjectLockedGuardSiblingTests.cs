using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R112-model-drawing-object-lock-1-1: R111 layered the per-object <c>Locked</c> override on top of
/// the sheet-level "Edit objects" protection guard, but only for the Reposition/Resize (and
/// SetChartBounds) command of each object family -- see
/// <see cref="R111_DrawingObjectLockedGuardTests"/>. Every sibling command in the same families
/// (rotate, crop, aspect-ratio-lock, colors/gradient/effect, z-order, and the chart type/source/
/// style/layout/move commands) still called the sheet-only guard overload, so an author-unlocked
/// object was incorrectly rejected for every operation except move/resize while its sheet blocked
/// "Edit objects" -- Excel's Format Object &gt; Properties &gt; Locked checkbox governs ALL
/// manipulations of an object, not just move/resize.
///
/// This class covers the remaining sibling commands: Picture (Rotate, LockAspectRatio, Crop),
/// TextBox (SetText, Rotate, SetColors), DrawingShape (SetColors, SetGradient, SetEffect,
/// BringForward, SendBackward), and Chart (SetStyle, ChangeType, ChangeSource, SetLayout, Move,
/// MoveToNewSheet, ChangePivotChartType). Each command gets an "unlocked object on a protected
/// sheet is allowed" case plus a "locked (default) object on the same protected sheet is still
/// rejected" no-regression case, mirroring R111_DrawingObjectLockedGuardTests's pattern.
/// </summary>
public sealed class R112_DrawingObjectLockedGuardSiblingTests
{
    // ── Picture ──────────────────────────────────────────────────────────

    [Fact]
    public void RotatePictureCommand_UnlockedPictureOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        sheet.Pictures.Add(picture);

        var outcome = new RotatePictureCommand(sheet.Id, picture.Id, 45).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked picture must stay rotatable even while the sheet blocks Edit objects, matching Excel");
        picture.RotationDegrees.Should().Be(45);
    }

    [Fact]
    public void RotatePictureCommand_LockedPictureOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1) }; // Locked defaults to true
        sheet.Pictures.Add(picture);

        var outcome = new RotatePictureCommand(sheet.Id, picture.Id, 45).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        picture.RotationDegrees.Should().Be(0);
    }

    [Fact]
    public void SetPictureLockAspectRatioCommand_UnlockedPictureOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        sheet.Pictures.Add(picture);

        var outcome = new SetPictureLockAspectRatioCommand(sheet.Id, picture.Id, true).Apply(ctx);

        outcome.Success.Should().BeTrue();
        picture.LockAspectRatio.Should().BeTrue();
    }

    [Fact]
    public void SetPictureLockAspectRatioCommand_LockedPictureOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1), LockAspectRatio = false };
        sheet.Pictures.Add(picture);

        var outcome = new SetPictureLockAspectRatioCommand(sheet.Id, picture.Id, true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        picture.LockAspectRatio.Should().BeFalse();
    }

    [Fact]
    public void SetPictureCropCommand_UnlockedPictureOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            Locked = false
        };
        sheet.Pictures.Add(picture);

        var outcome = new SetPictureCropCommand(sheet.Id, picture.Id, 0.1, 0.1, 0.1, 0.1).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked picture must stay croppable even while the sheet blocks Edit objects, matching Excel");
        picture.CropLeft.Should().Be(0.1);
    }

    [Fact]
    public void SetPictureCropCommand_LockedPictureOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image
            // Locked defaults to true.
        };
        sheet.Pictures.Add(picture);

        var outcome = new SetPictureCropCommand(sheet.Id, picture.Id, 0.1, 0.1, 0.1, 0.1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        picture.CropLeft.Should().Be(0);
    }

    // ── TextBox ──────────────────────────────────────────────────────────

    [Fact]
    public void SetTextBoxTextCommand_UnlockedTextBoxOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Text = "Old", Locked = false };
        sheet.TextBoxes.Add(textBox);

        var outcome = new SetTextBoxTextCommand(sheet.Id, textBox.Id, "New").Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked text box must stay text-editable even while the sheet blocks Edit objects, matching Excel");
        textBox.Text.Should().Be("New");
    }

    [Fact]
    public void SetTextBoxTextCommand_LockedTextBoxOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Text = "Old" }; // Locked defaults to true
        sheet.TextBoxes.Add(textBox);

        var outcome = new SetTextBoxTextCommand(sheet.Id, textBox.Id, "New").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        textBox.Text.Should().Be("Old");
    }

    [Fact]
    public void RotateTextBoxCommand_UnlockedTextBoxOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Text = "Note", Locked = false };
        sheet.TextBoxes.Add(textBox);

        var outcome = new RotateTextBoxCommand(sheet.Id, textBox.Id, 30).Apply(ctx);

        outcome.Success.Should().BeTrue();
        textBox.RotationDegrees.Should().Be(30);
    }

    [Fact]
    public void RotateTextBoxCommand_LockedTextBoxOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Text = "Note" };
        sheet.TextBoxes.Add(textBox);

        var outcome = new RotateTextBoxCommand(sheet.Id, textBox.Id, 30).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        textBox.RotationDegrees.Should().Be(0);
    }

    [Fact]
    public void SetTextBoxColorsCommand_UnlockedTextBoxOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Text = "Note", Locked = false };
        sheet.TextBoxes.Add(textBox);

        var outcome = new SetTextBoxColorsCommand(sheet.Id, textBox.Id, new CellColor(255, 0, 0), null).Apply(ctx);

        outcome.Success.Should().BeTrue();
        textBox.FillColor.Should().Be(new CellColor(255, 0, 0));
    }

    [Fact]
    public void SetTextBoxColorsCommand_LockedTextBoxOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Text = "Note" };
        sheet.TextBoxes.Add(textBox);

        var outcome = new SetTextBoxColorsCommand(sheet.Id, textBox.Id, new CellColor(255, 0, 0), null).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        textBox.FillColor.Should().BeNull();
    }

    // ── DrawingShape ─────────────────────────────────────────────────────

    [Fact]
    public void SetDrawingShapeColorsCommand_UnlockedShapeOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        sheet.DrawingShapes.Add(shape);

        var outcome = new SetDrawingShapeColorsCommand(sheet.Id, shape.Id, new CellColor(0, 255, 0), null).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked shape must stay editable even while the sheet blocks Edit objects, matching Excel");
        shape.FillColor.Should().Be(new CellColor(0, 255, 0));
    }

    [Fact]
    public void SetDrawingShapeColorsCommand_LockedShapeOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        sheet.DrawingShapes.Add(shape);

        var outcome = new SetDrawingShapeColorsCommand(sheet.Id, shape.Id, new CellColor(0, 255, 0), null).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        shape.FillColor.Should().BeNull();
    }

    [Fact]
    public void SetDrawingShapeGradientCommand_UnlockedShapeOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        sheet.DrawingShapes.Add(shape);

        var outcome = new SetDrawingShapeGradientCommand(
            sheet.Id, shape.Id, new CellColor(255, 0, 0), new CellColor(0, 0, 255)).Apply(ctx);

        outcome.Success.Should().BeTrue();
        shape.GradientFillEndColor.Should().Be(new CellColor(0, 0, 255));
    }

    [Fact]
    public void SetDrawingShapeGradientCommand_LockedShapeOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        sheet.DrawingShapes.Add(shape);

        var outcome = new SetDrawingShapeGradientCommand(
            sheet.Id, shape.Id, new CellColor(255, 0, 0), new CellColor(0, 0, 255)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        shape.GradientFillEndColor.Should().BeNull();
    }

    [Fact]
    public void SetDrawingShapeEffectCommand_UnlockedShapeOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        sheet.DrawingShapes.Add(shape);

        var outcome = new SetDrawingShapeEffectCommand(sheet.Id, shape.Id, hasShadowEffect: true).Apply(ctx);

        outcome.Success.Should().BeTrue();
        shape.HasShadowEffect.Should().BeTrue();
    }

    [Fact]
    public void SetDrawingShapeEffectCommand_LockedShapeOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        sheet.DrawingShapes.Add(shape);

        var outcome = new SetDrawingShapeEffectCommand(sheet.Id, shape.Id, hasShadowEffect: true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        shape.HasShadowEffect.Should().BeFalse();
    }

    [Fact]
    public void BringDrawingShapeForwardCommand_UnlockedShapeOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        sheet.DrawingShapes.Add(shape);
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 2, 2) });

        var outcome = new BringDrawingShapeForwardCommand(sheet.Id, shape.Id).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked shape must stay reorderable even while the sheet blocks Edit objects, matching Excel");
    }

    [Fact]
    public void BringDrawingShapeForwardCommand_LockedShapeOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        sheet.DrawingShapes.Add(shape);
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 2, 2) });

        var outcome = new BringDrawingShapeForwardCommand(sheet.Id, shape.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
    }

    [Fact]
    public void SendDrawingShapeBackwardCommand_UnlockedShapeOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 2, 2) });
        sheet.DrawingShapes.Add(shape);

        var outcome = new SendDrawingShapeBackwardCommand(sheet.Id, shape.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
    }

    [Fact]
    public void SendDrawingShapeBackwardCommand_LockedShapeOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 2, 2) });
        sheet.DrawingShapes.Add(shape);

        var outcome = new SendDrawingShapeBackwardCommand(sheet.Id, shape.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
    }

    // ── Chart ────────────────────────────────────────────────────────────

    private static GridRange ChartRange(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));

    [Fact]
    public void SetChartStyleCommand_UnlockedChartOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, ChartRange(sheet), ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.Locked = false;
        sheet.IsProtected = true;

        var outcome = new SetChartStyleCommand(sheet.Id, chart.Id, 5).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked chart must stay editable even while the sheet blocks Edit objects, matching Excel");
        chart.ChartStyleId.Should().Be(5);
    }

    [Fact]
    public void SetChartStyleCommand_LockedChartOnProtectedSheet_IsRejected()
    {
        // Sibling no-regression case: SetChartStyleCommand_RejectsProtectedSheetWithoutEditObjectsPermission
        // (a default-locked chart) must remain rejected exactly as before this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, ChartRange(sheet), ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        sheet.IsProtected = true;

        var outcome = new SetChartStyleCommand(sheet.Id, chart.Id, 5).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        chart.ChartStyleId.Should().BeNull();
    }

    [Fact]
    public void ChangeChartTypeCommand_UnlockedChartOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, ChartRange(sheet), ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.Locked = false;
        sheet.IsProtected = true;

        var outcome = new ChangeChartTypeCommand(sheet.Id, chart.Id, ChartType.Line).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Line);
    }

    [Fact]
    public void ChangeChartTypeCommand_LockedChartOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, ChartRange(sheet), ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        sheet.IsProtected = true;

        var outcome = new ChangeChartTypeCommand(sheet.Id, chart.Id, ChartType.Line).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        chart.Type.Should().Be(ChartType.Column);
    }

    [Fact]
    public void ChangeChartSourceCommand_UnlockedChartOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var originalRange = ChartRange(sheet);
        var newRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 6, 5));
        new AddChartCommand(sheet.Id, originalRange, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.Locked = false;
        sheet.IsProtected = true;

        var outcome = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.DataRange.Should().Be(newRange);
    }

    [Fact]
    public void ChangeChartSourceCommand_LockedChartOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var originalRange = ChartRange(sheet);
        var newRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 6, 5));
        new AddChartCommand(sheet.Id, originalRange, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        sheet.IsProtected = true;

        var outcome = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        chart.DataRange.Should().Be(originalRange);
    }

    [Fact]
    public void SetChartLayoutCommand_UnlockedChartOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, ChartRange(sheet), ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.Locked = false;
        sheet.IsProtected = true;

        var outcome = new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(Title: "Allowed")).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked chart's layout must stay editable even while the sheet blocks Edit objects, matching Excel");
        chart.Title.Should().Be("Allowed");
    }

    [Fact]
    public void SetChartLayoutCommand_LockedChartOnProtectedSheet_IsRejected()
    {
        // Sibling no-regression case: SetChartLayoutCommand_RejectsProtectedSheetWithoutEditObjectsPermission
        // (a default-locked chart) must remain rejected exactly as before this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, ChartRange(sheet), ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        sheet.IsProtected = true;

        var outcome = new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(Title: "Blocked")).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        chart.Title.Should().Be("Sales");
    }

    [Fact]
    public void MoveChartCommand_UnlockedChartOnProtectedSourceSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Source");
        var target = wb.AddSheet("Dashboard");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(source.Id, ChartRange(source), ChartType.Column, "Sales").Apply(ctx);
        var chart = source.Charts[0];
        chart.Locked = false;
        source.IsProtected = true;

        var outcome = new MoveChartCommand(source.Id, chart.Id, target.Id).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked chart must stay movable even while the source sheet blocks Edit objects, matching Excel");
        target.Charts.Should().ContainSingle().Which.Id.Should().Be(chart.Id);
    }

    [Fact]
    public void MoveChartCommand_LockedChartOnProtectedSourceSheet_IsRejected()
    {
        // Sibling no-regression case: MoveChartCommand_RejectsProtectedSourceWithoutEditObjectsPermission
        // (a default-locked chart) must remain rejected exactly as before this fix.
        var wb = new Workbook("test");
        var source = wb.AddSheet("Source");
        var target = wb.AddSheet("Dashboard");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(source.Id, ChartRange(source), ChartType.Column, "Sales").Apply(ctx);
        var chart = source.Charts[0];
        source.IsProtected = true;

        var outcome = new MoveChartCommand(source.Id, chart.Id, target.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        source.Charts.Should().Contain(chart);
        target.Charts.Should().BeEmpty();
    }

    [Fact]
    public void MoveChartToNewSheetCommand_UnlockedChartOnProtectedSourceSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Source");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(source.Id, ChartRange(source), ChartType.Line, "Sales").Apply(ctx);
        var chart = source.Charts[0];
        chart.Locked = false;
        source.IsProtected = true;

        var outcome = new MoveChartToNewSheetCommand(source.Id, chart.Id, "Sales Chart").Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.Sheets.Single(sheet => sheet.Name == "Sales Chart").Charts.Should().ContainSingle().Which.Id.Should().Be(chart.Id);
    }

    [Fact]
    public void MoveChartToNewSheetCommand_LockedChartOnProtectedSourceSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Source");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(source.Id, ChartRange(source), ChartType.Line, "Sales").Apply(ctx);
        var chart = source.Charts[0];
        source.IsProtected = true;

        var outcome = new MoveChartToNewSheetCommand(source.Id, chart.Id, "Sales Chart").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        wb.Sheets.Should().NotContain(sheet => sheet.Name == "Sales Chart");
        source.Charts.Should().Contain(chart);
    }

    [Fact]
    public void ChangePivotChartTypeCommand_UnlockedPivotChartOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        // Grant the pivot-reports permission so only the "Edit objects" guard is under test.
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = ChartRange(sheet),
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            Locked = false
        };
        sheet.Charts.Add(chart);

        var outcome = new ChangePivotChartTypeCommand(sheet.Id, chart.Id, ChartType.Bar).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked PivotChart must stay editable even while the sheet blocks Edit objects, matching Excel");
        chart.Type.Should().Be(ChartType.Bar);
    }

    [Fact]
    public void ChangePivotChartTypeCommand_LockedPivotChartOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = ChartRange(sheet),
            IsPivotChart = true,
            PivotTableName = "PivotTable1"
            // Locked defaults to true.
        };
        sheet.Charts.Add(chart);

        var outcome = new ChangePivotChartTypeCommand(sheet.Id, chart.Id, ChartType.Bar).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        chart.Type.Should().Be(ChartType.Column);
    }

    // ── R112 fix-agent follow-up: ConfigurePivotChartOptions / ConfigureChartHiddenEmptyCells /
    // RemoveChartSeries / PasteCharts, plus AltText (Picture/Shape/TextBox) and
    // SetWaterfallTotalPoint found by the FAMILY RULE sweep ──────────────────────────

    [Fact]
    public void ConfigurePivotChartOptionsCommand_UnlockedPivotChartOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = ChartRange(sheet),
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            Locked = false
        };
        sheet.Charts.Add(chart);

        var outcome = new ConfigurePivotChartOptionsCommand(sheet.Id, chart.Id, 7, showFieldButtons: true).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked PivotChart's options must stay editable even while the sheet blocks Edit objects, matching Excel");
        chart.ChartStyleId.Should().Be(7);
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_LockedPivotChartOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = ChartRange(sheet),
            IsPivotChart = true,
            PivotTableName = "PivotTable1"
            // Locked defaults to true.
        };
        sheet.Charts.Add(chart);

        var outcome = new ConfigurePivotChartOptionsCommand(sheet.Id, chart.Id, 7, showFieldButtons: true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        chart.ChartStyleId.Should().BeNull();
    }

    [Fact]
    public void ConfigureChartHiddenEmptyCellsCommand_UnlockedChartOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, ChartRange(sheet), ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.Locked = false;
        sheet.IsProtected = true;

        var outcome = new ConfigureChartHiddenEmptyCellsCommand(
            sheet.Id, chart.Id, ChartBlankDisplayMode.Zero, showDataInHiddenRowsAndColumns: true).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked chart's hidden/empty-cell settings must stay editable even while the sheet blocks Edit objects, matching Excel");
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Zero);
        chart.ShowDataInHiddenRowsAndColumns.Should().BeTrue();
    }

    [Fact]
    public void ConfigureChartHiddenEmptyCellsCommand_LockedChartOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, ChartRange(sheet), ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        sheet.IsProtected = true;

        var outcome = new ConfigureChartHiddenEmptyCellsCommand(
            sheet.Id, chart.Id, ChartBlankDisplayMode.Zero, showDataInHiddenRowsAndColumns: true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Gap);
        chart.ShowDataInHiddenRowsAndColumns.Should().BeFalse();
    }

    [Fact]
    public void RemoveChartSeriesCommand_UnlockedChartOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // 3 columns, first is categories -> 2 series.
        new AddChartCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            ChartType.Column,
            "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.Locked = false;
        sheet.IsProtected = true;

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, seriesIndex: 0).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked chart's series list must stay editable even while the sheet blocks Edit objects, matching Excel");
    }

    [Fact]
    public void RemoveChartSeriesCommand_LockedChartOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            ChartType.Column,
            "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        sheet.IsProtected = true;

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, seriesIndex: 0).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
    }

    [Fact]
    public void SetWaterfallTotalPointCommand_UnlockedChartOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, ChartRange(sheet), ChartType.Waterfall, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.Locked = false;
        sheet.IsProtected = true;

        var outcome = new SetWaterfallTotalPointCommand(sheet.Id, chart.Id, pointIndex: 0, setAsTotal: true).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked waterfall chart's total-point flags must stay editable even while the sheet blocks Edit objects, matching Excel");
        chart.WaterfallTotalPointIndices.Should().Contain(0);
    }

    [Fact]
    public void SetWaterfallTotalPointCommand_LockedChartOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, ChartRange(sheet), ChartType.Waterfall, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        sheet.IsProtected = true;

        var outcome = new SetWaterfallTotalPointCommand(sheet.Id, chart.Id, pointIndex: 0, setAsTotal: true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        chart.WaterfallTotalPointIndices.Should().BeNull();
    }

    [Fact]
    public void SetPictureAltTextCommand_UnlockedPictureOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        sheet.Pictures.Add(picture);

        var outcome = new SetPictureAltTextCommand(sheet.Id, picture.Id, "Description").Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked picture's alt text must stay editable even while the sheet blocks Edit objects, matching Excel");
        picture.AltText.Should().Be("Description");
    }

    [Fact]
    public void SetPictureAltTextCommand_LockedPictureOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1) }; // Locked defaults to true
        sheet.Pictures.Add(picture);

        var outcome = new SetPictureAltTextCommand(sheet.Id, picture.Id, "Description").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        picture.AltText.Should().BeNull();
    }

    [Fact]
    public void SetDrawingShapeAltTextCommand_UnlockedShapeOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Locked = false };
        sheet.DrawingShapes.Add(shape);

        var outcome = new SetDrawingShapeAltTextCommand(sheet.Id, shape.Id, "Description").Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked shape's alt text must stay editable even while the sheet blocks Edit objects, matching Excel");
        shape.AltText.Should().Be("Description");
    }

    [Fact]
    public void SetDrawingShapeAltTextCommand_LockedShapeOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        sheet.DrawingShapes.Add(shape);

        var outcome = new SetDrawingShapeAltTextCommand(sheet.Id, shape.Id, "Description").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        shape.AltText.Should().BeNull();
    }

    [Fact]
    public void SetTextBoxAltTextCommand_UnlockedTextBoxOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Text = "Note", Locked = false };
        sheet.TextBoxes.Add(textBox);

        var outcome = new SetTextBoxAltTextCommand(sheet.Id, textBox.Id, "Description").Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked text box's alt text must stay editable even while the sheet blocks Edit objects, matching Excel");
        textBox.AltText.Should().Be("Description");
    }

    [Fact]
    public void SetTextBoxAltTextCommand_LockedTextBoxOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Text = "Note" };
        sheet.TextBoxes.Add(textBox);

        var outcome = new SetTextBoxAltTextCommand(sheet.Id, textBox.Id, "Description").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        textBox.AltText.Should().BeNull();
    }

    // ── PasteChartsCommand: NOT fixed -- decision documented on the class, verified here.
    // Unlike the object-editing commands above, PasteChartsCommand's guard runs on the
    // DESTINATION sheet before any chart is placed there (it inserts NEW cloned chart objects,
    // it does not mutate an existing one), so there is no pre-existing chart whose Locked flag
    // could apply -- this mirrors PastePicturesCommand/PasteShapesCommand/PasteTextBoxesCommand,
    // which are also sheet-only by design. An unlocked chart elsewhere on the sheet must NOT
    // bypass this guard (there is nothing for its Locked flag to attach to).
    [Fact]
    public void PasteChartsCommand_ProtectedDestinationSheet_IsRejected_RegardlessOfUnlockedChartsElsewhere()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, ChartRange(sheet), ChartType.Column, "Existing").Apply(ctx);
        sheet.Charts[0].Locked = false;
        sheet.IsProtected = true;

        var sourceChart = new ChartModel { Type = ChartType.Line, DataRange = ChartRange(sheet) };
        var outcome = new PasteChartsCommand(
            sheet.Id,
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            new CellAddress(sheet.Id, 8, 8),
            [sourceChart],
            transpose: false).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.Charts.Should().ContainSingle(); // nothing pasted
    }
}
