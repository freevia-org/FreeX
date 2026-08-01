using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R111-model-drawing-object-lock-1-1: <see cref="PictureModel"/>, <see cref="ChartModel"/>, and
/// <see cref="TextBoxModel"/> now carry a per-object <c>Locked</c> flag (default
/// <see langword="true"/>, matching Excel's default-locked object / OOXML
/// <c>&lt;a:picLocks&gt;</c>/<c>&lt;a:graphicFrameLocks&gt;</c>/<c>&lt;a:spLocks&gt;</c>), mirroring
/// <see cref="DrawingShapeModel.Locked"/> (see <c>R35_ShapeLockedGuardTests</c>). The real move/resize
/// commands for each object kind now layer that flag on top of the sheet-level "Edit objects"
/// protection permission check: an author-unlocked object stays movable/resizable on a protected
/// sheet with "Edit objects" blocked, while a (default) locked object of the same kind on the same
/// sheet is still rejected -- matching Excel's Format Object &gt; Properties &gt; Locked checkbox.
///
/// Family covered here: Picture (Reposition/Resize), Chart (SetBounds), TextBox (Reposition/Resize).
/// DrawingShapeModel itself is already covered by R35_ShapeLockedGuardTests and is unchanged by this
/// fix. Reading/writing the OOXML per-object lock attribute on load/save remains deferred follow-up
/// work for all four object kinds, exactly as already documented for DrawingShapeModel since R35 --
/// this test class covers the in-memory model + command-guard enforcement only.
/// </summary>
public sealed class R111_DrawingObjectLockedGuardTests
{
    // ── Picture ──────────────────────────────────────────────────────────

    [Fact]
    public void RepositionPictureCommand_UnlockedPictureOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var originalAnchor = new CellAddress(sheet.Id, 2, 2);
        var picture = new PictureModel { Anchor = originalAnchor, Locked = false };
        sheet.Pictures.Add(picture);

        var newAnchor = new CellAddress(sheet.Id, 5, 5);
        var outcome = new RepositionPictureCommand(sheet.Id, picture.Id, newAnchor).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked picture must stay movable even while the sheet blocks Edit objects, matching Excel");
        picture.Anchor.Should().Be(newAnchor);
    }

    [Fact]
    public void RepositionPictureCommand_LockedPictureOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var originalAnchor = new CellAddress(sheet.Id, 2, 2);
        var picture = new PictureModel { Anchor = originalAnchor }; // Locked defaults to true
        sheet.Pictures.Add(picture);

        var newAnchor = new CellAddress(sheet.Id, 5, 5);
        var outcome = new RepositionPictureCommand(sheet.Id, picture.Id, newAnchor).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        picture.Anchor.Should().Be(originalAnchor);
    }

    [Fact]
    public void ResizePictureCommand_UnlockedPictureOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 100,
            Height = 60,
            Locked = false
        };
        sheet.Pictures.Add(picture);

        var outcome = new ResizePictureCommand(sheet.Id, picture.Id, 200, 120).Apply(ctx);

        outcome.Success.Should().BeTrue();
        picture.Width.Should().Be(200);
        picture.Height.Should().Be(120);
    }

    [Fact]
    public void ResizePictureCommand_LockedPictureOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 100,
            Height = 60
        };
        sheet.Pictures.Add(picture);

        var outcome = new ResizePictureCommand(sheet.Id, picture.Id, 200, 120).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        picture.Width.Should().Be(100);
        picture.Height.Should().Be(60);
    }

    // ── Chart ────────────────────────────────────────────────────────────

    [Fact]
    public void SetChartBoundsCommand_UnlockedChartOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 0, 0), new CellAddress(sheet.Id, 2, 1)),
            Left = 20,
            Top = 20,
            Width = 400,
            Height = 300,
            Locked = false
        };
        sheet.Charts.Add(chart);

        var outcome = new SetChartBoundsCommand(sheet.Id, chart.Id, 64, 48, 320, 180).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked chart must stay movable/resizable even while the sheet blocks Edit objects, matching Excel");
        chart.Left.Should().Be(64);
        chart.Top.Should().Be(48);
        chart.Width.Should().Be(320);
        chart.Height.Should().Be(180);
    }

    [Fact]
    public void SetChartBoundsCommand_LockedChartOnProtectedSheet_IsRejected()
    {
        // Sibling no-regression case: the pre-existing behaviour (default-locked chart still
        // rejected) covered by ChartCommandTests.Bounds's
        // SetChartBoundsCommand_RejectsProtectedSheetWithoutEditObjectsPermission must be unaffected.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 0, 0), new CellAddress(sheet.Id, 2, 1)),
            Left = 20,
            Top = 20,
            Width = 400,
            Height = 300
            // Locked defaults to true.
        };
        sheet.Charts.Add(chart);

        var outcome = new SetChartBoundsCommand(sheet.Id, chart.Id, 64, 48, 320, 180).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        chart.Left.Should().Be(20);
        chart.Top.Should().Be(20);
        chart.Width.Should().Be(400);
        chart.Height.Should().Be(300);
    }

    // ── TextBox ──────────────────────────────────────────────────────────

    [Fact]
    public void RepositionTextBoxCommand_UnlockedTextBoxOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var originalAnchor = new CellAddress(sheet.Id, 2, 2);
        var textBox = new TextBoxModel { Anchor = originalAnchor, Text = "Note", Locked = false };
        sheet.TextBoxes.Add(textBox);

        var newAnchor = new CellAddress(sheet.Id, 5, 5);
        var outcome = new RepositionTextBoxCommand(sheet.Id, textBox.Id, newAnchor).Apply(ctx);

        outcome.Success.Should().BeTrue(
            "an author-unlocked text box must stay movable even while the sheet blocks Edit objects, matching Excel");
        textBox.Anchor.Should().Be(newAnchor);
    }

    [Fact]
    public void RepositionTextBoxCommand_LockedTextBoxOnProtectedSheet_IsRejected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var originalAnchor = new CellAddress(sheet.Id, 2, 2);
        var textBox = new TextBoxModel { Anchor = originalAnchor, Text = "Note" }; // Locked defaults to true
        sheet.TextBoxes.Add(textBox);

        var newAnchor = new CellAddress(sheet.Id, 5, 5);
        var outcome = new RepositionTextBoxCommand(sheet.Id, textBox.Id, newAnchor).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        textBox.Anchor.Should().Be(originalAnchor);
    }

    [Fact]
    public void ResizeTextBoxCommand_UnlockedTextBoxOnProtectedSheet_IsAllowed()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new TestCommandContext(wb);

        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "Note",
            Width = 180,
            Height = 80,
            Locked = false
        };
        sheet.TextBoxes.Add(textBox);

        var outcome = new ResizeTextBoxCommand(sheet.Id, textBox.Id, 220, 120).Apply(ctx);

        outcome.Success.Should().BeTrue();
        textBox.Width.Should().Be(220);
        textBox.Height.Should().Be(120);
    }
}
