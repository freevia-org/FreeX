using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class SheetTabCommandTests
{
    [Fact]
    public void DuplicateSheetCommand_CopiesSheetContentAndUndoRemovesCopy()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("hello"));
        sheet.ColumnWidths[1] = 18;
        sheet.RowHeights[1] = 24;
        sheet.Comments[a1] = "note";
        sheet.TabColor = new CellColor(255, 192, 0);
        sheet.ViewMode = WorksheetViewMode.PageBreakPreview;
        sheet.SplitRow = 5;
        sheet.SplitColumn = 3;
        sheet.PageHeader = new WorksheetHeaderFooter("Left header", "Center header", "Right header");
        sheet.PageFooter = new WorksheetHeaderFooter("Left footer", "Center footer", "Right footer");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Range Snapshot",
            Anchor = a1,
            SourceRowCount = 1,
            SourceColumnCount = 1,
            Width = 80,
            Height = 20,
            Kind = PictureKind.CellRangeSnapshot,
            AltText = "Copied range",
            Cells = { new PictureCellSnapshot(0, 0, "hello") }
        });
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Logo",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
            Width = 90,
            Height = 60,
            RotationDegrees = 45,
            AltText = "Embedded image"
        });
        sheet.Charts.Add(new ChartModel
        {
            Name = "Sales Trend",
            Type = ChartType.Line,
            DataRange = new GridRange(a1, a1),
            Title = "Trend",
            XAxisTitle = "Month",
            YAxisTitle = "Sales",
            ChartTitleTextColor = new CellColor(31, 78, 121),
            XAxisLabelAngle = -45,
            YAxisLabelAngle = 90,
            LegendPosition = ChartLegendPosition.Top,
            LegendOverlay = true,
            LegendTextColor = new CellColor(60, 60, 60),
            LegendTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            ShowLegend = false,
            ShowDataLabels = true,
            DataLabelAngle = 45,
            DataLabelTextColor = new CellColor(192, 0, 0),
            DataLabelTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark2),
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    StrokeColor: new CellColor(0, 114, 178),
                    StrokeThickness: 2.5,
                    StrokeThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1))
            ],
            Left = 10,
            Top = 20,
            Width = 300,
            Height = 200
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Name = "Narrative",
            Anchor = new CellAddress(sheet.Id, 3, 2),
            Text = "Box",
            Width = 180,
            Height = 80,
            RotationDegrees = 25,
            FillColor = new CellColor(240, 250, 255),
            OutlineColor = new CellColor(70, 80, 90),
            AltText = "Text box note"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Process Step",
            Anchor = new CellAddress(sheet.Id, 4, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 120,
            Height = 70,
            RotationDegrees = 35,
            FillColor = new CellColor(200, 210, 220),
            OutlineColor = new CellColor(30, 40, 50),
            AltText = "Process box"
        });

        var command = new DuplicateSheetCommand(sheet.Id);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.Sheets.Should().HaveCount(2);
        var copy = wb.Sheets[1];
        copy.Id.Should().NotBe(sheet.Id);
        copy.Name.Should().Be("Sheet1 (2)");
        copy.GetValue(new CellAddress(copy.Id, 1, 1)).Should().Be(new TextValue("hello"));
        copy.ColumnWidths[1].Should().Be(18);
        copy.RowHeights[1].Should().Be(24);
        copy.Comments[new CellAddress(copy.Id, 1, 1)].Should().Be("note");
        copy.TabColor.Should().Be(new CellColor(255, 192, 0));
        copy.ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
        copy.SplitRow.Should().Be(5);
        copy.SplitColumn.Should().Be(3);
        copy.PageHeader.Should().Be(new WorksheetHeaderFooter("Left header", "Center header", "Right header"));
        copy.PageFooter.Should().Be(new WorksheetHeaderFooter("Left footer", "Center footer", "Right footer"));
        copy.Pictures.Should().HaveCount(2);
        var copiedPicture = copy.Pictures[0];
        copiedPicture.Name.Should().Be("Range Snapshot");
        copiedPicture.Anchor.Should().Be(new CellAddress(copy.Id, 1, 1));
        copiedPicture.AltText.Should().Be("Copied range");
        copiedPicture.Cells.Should().ContainSingle().Which.Text.Should().Be("hello");
        var copiedImage = copy.Pictures[1];
        copiedImage.Name.Should().Be("Logo");
        copiedImage.Anchor.Should().Be(new CellAddress(copy.Id, 2, 2));
        copiedImage.Kind.Should().Be(PictureKind.Image);
        copiedImage.ImageBytes.Should().Equal(1, 2, 3);
        copiedImage.RotationDegrees.Should().Be(45);
        copiedImage.AltText.Should().Be("Embedded image");
        var copiedChart = copy.Charts.Should().ContainSingle().Subject;
        copiedChart.Name.Should().Be("Sales Trend");
        copiedChart.Type.Should().Be(ChartType.Line);
        copiedChart.DataRange.Start.Sheet.Should().Be(copy.Id);
        copiedChart.Title.Should().Be("Trend");
        copiedChart.XAxisTitle.Should().Be("Month");
        copiedChart.YAxisTitle.Should().Be("Sales");
        copiedChart.ChartTitleTextColor.Should().Be(new CellColor(31, 78, 121));
        copiedChart.XAxisLabelAngle.Should().Be(-45);
        copiedChart.YAxisLabelAngle.Should().Be(90);
        copiedChart.LegendPosition.Should().Be(ChartLegendPosition.Top);
        copiedChart.LegendOverlay.Should().BeTrue();
        copiedChart.LegendTextColor.Should().Be(new CellColor(60, 60, 60));
        copiedChart.LegendTextThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1));
        copiedChart.ShowLegend.Should().BeFalse();
        copiedChart.ShowDataLabels.Should().BeTrue();
        copiedChart.DataLabelAngle.Should().Be(45);
        copiedChart.DataLabelTextColor.Should().Be(new CellColor(192, 0, 0));
        copiedChart.DataLabelTextThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark2));
        copiedChart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(
                0,
                StrokeColor: new CellColor(0, 114, 178),
                StrokeThickness: 2.5,
                StrokeThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1)));
        copiedChart.Left.Should().Be(10);
        copiedChart.Top.Should().Be(20);
        copiedChart.Width.Should().Be(300);
        copiedChart.Height.Should().Be(200);
        var copiedTextBox = copy.TextBoxes.Should().ContainSingle().Subject;
        copiedTextBox.Name.Should().Be("Narrative");
        copiedTextBox.Anchor.Should().Be(new CellAddress(copy.Id, 3, 2));
        copiedTextBox.Text.Should().Be("Box");
        copiedTextBox.RotationDegrees.Should().Be(25);
        copiedTextBox.FillColor.Should().Be(new CellColor(240, 250, 255));
        copiedTextBox.OutlineColor.Should().Be(new CellColor(70, 80, 90));
        copiedTextBox.AltText.Should().Be("Text box note");
        var copiedShape = copy.DrawingShapes.Should().ContainSingle().Subject;
        copiedShape.Name.Should().Be("Process Step");
        copiedShape.Anchor.Should().Be(new CellAddress(copy.Id, 4, 2));
        copiedShape.Kind.Should().Be(DrawingShapeKind.Rectangle);
        copiedShape.RotationDegrees.Should().Be(35);
        copiedShape.AltText.Should().Be("Process box");
        copiedShape.FillColor.Should().Be(new CellColor(200, 210, 220));
        copiedShape.OutlineColor.Should().Be(new CellColor(30, 40, 50));

        command.Revert(ctx);

        wb.Sheets.Should().ContainSingle().Which.Id.Should().Be(sheet.Id);
    }

    [Fact]
    public void SetSheetHiddenCommand_HidesSheetAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);

        var command = new SetSheetHiddenCommand(sheet1.Id, hidden: true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet1.IsHidden.Should().BeTrue();

        command.Revert(ctx);

        sheet1.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void SetSheetHiddenCommand_RejectsHidingOnlyVisibleSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        sheet2.IsHidden = true;
        var ctx = new SimpleCtx(wb);

        var outcome = new SetSheetHiddenCommand(sheet1.Id, hidden: true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("visible");
        sheet1.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void SetSheetTabColorCommand_SetsColorAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.TabColor = new CellColor(255, 0, 0);

        var command = new SetSheetTabColorCommand(sheet.Id, new CellColor(0, 176, 80));

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.TabColor.Should().Be(new CellColor(0, 176, 80));

        command.Revert(ctx);

        sheet.TabColor.Should().Be(new CellColor(255, 0, 0));
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
