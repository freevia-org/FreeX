using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for round-14 bucket T2 findings — all three concern
/// <see cref="DuplicateSheetDrawingCloner"/> silently dropping or corrupting sheet content when a
/// sheet is duplicated:
/// R14-chart-editing-3 (SeriesColumnMappings omitted from cloned charts),
/// R14-image-media-2 (source-loaded pictures dropped from the duplicate on save), and
/// R14-workbook-structure-1 (form controls omitted entirely from the duplicate).
/// </summary>
public sealed class FreeXR14T2Tests
{
    [Fact]
    public void DuplicateSheetCommand_CopiesChartSeriesColumnMappings()
    {
        // Source chart plots B1:E10 but SeriesColumnMappings restricts rendering to columns
        // B, D, E — deliberately skipping column C (a skip-column/combo chart authoritative-column
        // scenario). Duplicating the sheet must carry SeriesColumnMappings onto the clone, or
        // ChartRenderer's HasAuthoritativeSeriesColumns sees an empty list and falls back to the
        // legacy positional scan, rendering column C as a phantom extra series Excel would not show.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 10, 5));
        var mappings = new List<ChartSeriesColumnMapping>
        {
            new(0, 2), // column B
            new(1, 4), // column D (column C = 3 is deliberately skipped)
            new(2, 5), // column E
        };
        sheet.Charts.Add(new ChartModel
        {
            Name = "Combo",
            Type = ChartType.Column,
            DataRange = dataRange,
            SeriesColumnMappings = mappings
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copiedChart = wb.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.SeriesColumnMappings.Should().BeEquivalentTo(mappings,
            because: "SeriesColumnMappings must travel with the duplicate so the clone keeps " +
                "skipping column C instead of falling back to the legacy all-columns scan");
    }

    [Fact]
    public void DuplicateSheetCommand_MarksClonedSourceLoadedPictureAsAuthored()
    {
        // A source-loaded picture's on-disk part is preserved only by matching the ORIGINAL sheet
        // name (XlsxFileAdapter.SavePostProcessing.GetSourceDrawingPathsBySheet); the duplicate
        // always gets a brand-new name absent from the source package, so nothing preserves it and
        // the writer's IsSupportedPicture (which requires !IsSourceLoaded) also skips it. The clone
        // must therefore be marked "authored" (IsSourceLoaded=false) so it round-trips through the
        // normal picture writer using its already-copied ImageBytes, matching Excel — the duplicated
        // picture must still be visible after a save/reload.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.Pictures.Add(new PictureModel
        {
            Name = "Logo",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3, 4],
            ContentType = "image/png",
            Width = 100,
            Height = 50,
            IsSourceLoaded = true
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copiedPicture = wb.Sheets[1].Pictures.Should().ContainSingle().Subject;
        copiedPicture.IsSourceLoaded.Should().BeFalse(
            because: "the duplicate's new sheet name is never matched by the source-package " +
                "preservation path, so the clone must be authored or the picture is silently dropped");
        copiedPicture.ImageBytes.Should().Equal(1, 2, 3, 4);
        copiedPicture.ContentType.Should().Be("image/png");
    }

    [Fact]
    public void DuplicateSheetCommand_CopiesFormControls()
    {
        // Sheet.Clone never touches FormControls and DuplicateSheetDrawingCloner.CopyDrawingCollections
        // only cloned Charts/TextBoxes/DrawingShapes/Pictures/Sparklines — a checkbox/list-box/spin
        // button on the source sheet must survive Duplicate Sheet, with its Anchor remapped onto the
        // copy sheet and its state (LinkedCell, IsChecked, etc.) carried over verbatim.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var anchor = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 3));
        sheet.FormControls.Add(new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            Name = "Check Box 1",
            Caption = "Enable feature",
            ShapeId = 7,
            Anchor = anchor,
            LinkedCell = "$D$3",
            IsChecked = true
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        var copiedControl = copy.FormControls.Should().ContainSingle().Subject;
        copiedControl.Kind.Should().Be(FormControlKind.CheckBox);
        copiedControl.Name.Should().Be("Check Box 1");
        copiedControl.Caption.Should().Be("Enable feature");
        copiedControl.ShapeId.Should().Be(7u);
        copiedControl.LinkedCell.Should().Be("$D$3");
        copiedControl.IsChecked.Should().BeTrue();
        copiedControl.Anchor.Should().Be(new GridRange(
            new CellAddress(copy.Id, 2, 2), new CellAddress(copy.Id, 2, 3)));

        command.Revert(ctx);
        wb.Sheets.Should().ContainSingle().Which.FormControls.Should().ContainSingle()
            .Which.Should().BeSameAs(sheet.FormControls[0]);
    }
}
