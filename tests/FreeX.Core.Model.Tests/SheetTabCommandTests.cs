using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public class SheetTabCommandTests
{
    [Fact]
    public void AddSheetCommand_InitializesNewSheetViewStateAtA1()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Sheet1");
        source.ActiveRow = 12;
        source.ActiveCol = 5;
        source.ViewTopRow = 10;
        source.ViewLeftCol = 4;
        var ctx = new TestCommandContext(wb);

        var outcome = new AddSheetCommand("Sheet2").Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.Sheets.Should().HaveCount(2);
        var created = wb.Sheets[1];
        created.ActiveRow.Should().Be(1);
        created.ActiveCol.Should().Be(1);
        created.ViewTopRow.Should().Be(1);
        created.ViewLeftCol.Should().Be(1);
    }

    [Fact]
    public void DuplicateSheetCommand_CopiesSheetContentAndUndoRemovesCopy()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("hello"));
        sheet.ColumnWidths[1] = 18;
        sheet.RowHeights[1] = 24;
        sheet.Comments[a1] = "note";
        sheet.TabColor = new CellColor(255, 192, 0);
        sheet.ViewMode = WorksheetViewMode.PageBreakPreview;
        sheet.ActiveRow = 8;
        sheet.ActiveCol = 4;
        sheet.ViewTopRow = 6;
        sheet.ViewLeftCol = 3;
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
            IsLinkedToSourceRange = true,
            LinkedSourceRange = new GridRange(a1, a1),
            LinkedSourceSheetName = "Sheet1",
            Width = 80,
            Height = 20,
            Kind = PictureKind.CellRangeSnapshot,
            Title = "Snapshot title",
            AltText = "Copied range",
            IsSourceLoaded = true,
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
            LockAspectRatio = false,
            RotationDegrees = 45,
            Title = "Logo title",
            AltText = "Embedded image",
            CropLeft = 0.1,
            CropTop = 0.2,
            CropRight = 0.3,
            CropBottom = 0.4,
            IsSourceLoaded = true
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
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25),
            Title = "Narrative title",
            AltText = "Text box note",
            IsSourceLoaded = true
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
            GradientFillEndColor = new CellColor(220, 230, 240),
            GradientFillDirection = DrawingShapeGradientDirection.Vertical,
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.1),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.1),
            HasShadowEffect = true,
            EffectPreset = DrawingShapeEffectPreset.Glow,
            Title = "Process title",
            AltText = "Process box",
            IsSourceLoaded = true
        });
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, sheet.Pictures[1].Id));
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, sheet.DrawingShapes[0].Id));
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, sheet.TextBoxes[0].Id));
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, sheet.Pictures[0].Id));

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
        copy.ActiveRow.Should().Be(1);
        copy.ActiveCol.Should().Be(1);
        copy.ViewTopRow.Should().Be(1);
        copy.ViewLeftCol.Should().Be(1);
        copy.SplitRow.Should().Be(5);
        copy.SplitColumn.Should().Be(3);
        copy.PageHeader.Should().Be(new WorksheetHeaderFooter("Left header", "Center header", "Right header"));
        copy.PageFooter.Should().Be(new WorksheetHeaderFooter("Left footer", "Center footer", "Right footer"));
        copy.Pictures.Should().HaveCount(2);
        var copiedPicture = copy.Pictures[0];
        copiedPicture.Name.Should().Be("Range Snapshot");
        copiedPicture.Anchor.Should().Be(new CellAddress(copy.Id, 1, 1));
        copiedPicture.IsLinkedToSourceRange.Should().BeTrue();
        copiedPicture.LinkedSourceRange.Should().Be(new GridRange(
            new CellAddress(copy.Id, 1, 1),
            new CellAddress(copy.Id, 1, 1)));
        copiedPicture.LinkedSourceSheetName.Should().Be(copy.Name);
        copiedPicture.Title.Should().Be("Snapshot title");
        copiedPicture.AltText.Should().Be("Copied range");
        copiedPicture.IsSourceLoaded.Should().BeTrue();
        copiedPicture.Cells.Should().ContainSingle().Which.Text.Should().Be("hello");
        var copiedImage = copy.Pictures[1];
        copiedImage.Name.Should().Be("Logo");
        copiedImage.Anchor.Should().Be(new CellAddress(copy.Id, 2, 2));
        copiedImage.Kind.Should().Be(PictureKind.Image);
        copiedImage.ImageBytes.Should().Equal(1, 2, 3);
        copiedImage.LockAspectRatio.Should().BeFalse();
        copiedImage.RotationDegrees.Should().Be(45);
        copiedImage.Title.Should().Be("Logo title");
        copiedImage.AltText.Should().Be("Embedded image");
        copiedImage.CropLeft.Should().Be(0.1);
        copiedImage.CropTop.Should().Be(0.2);
        copiedImage.CropRight.Should().Be(0.3);
        copiedImage.CropBottom.Should().Be(0.4);
        copiedImage.IsSourceLoaded.Should().BeTrue();
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
        copiedTextBox.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25));
        copiedTextBox.OutlineThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25));
        copiedTextBox.Title.Should().Be("Narrative title");
        copiedTextBox.AltText.Should().Be("Text box note");
        copiedTextBox.IsSourceLoaded.Should().BeTrue();
        var copiedShape = copy.DrawingShapes.Should().ContainSingle().Subject;
        copiedShape.Name.Should().Be("Process Step");
        copiedShape.Anchor.Should().Be(new CellAddress(copy.Id, 4, 2));
        copiedShape.Kind.Should().Be(DrawingShapeKind.Rectangle);
        copiedShape.RotationDegrees.Should().Be(35);
        copiedShape.Title.Should().Be("Process title");
        copiedShape.AltText.Should().Be("Process box");
        copiedShape.FillColor.Should().Be(new CellColor(200, 210, 220));
        copiedShape.OutlineColor.Should().Be(new CellColor(30, 40, 50));
        copiedShape.GradientFillEndColor.Should().Be(new CellColor(220, 230, 240));
        copiedShape.GradientFillDirection.Should().Be(DrawingShapeGradientDirection.Vertical);
        copiedShape.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.1));
        copiedShape.OutlineThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.1));
        copiedShape.HasShadowEffect.Should().BeTrue();
        copiedShape.EffectPreset.Should().Be(DrawingShapeEffectPreset.Glow);
        copiedShape.IsSourceLoaded.Should().BeTrue();
        copy.DrawingObjectZOrder.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, copiedImage.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, copiedShape.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, copiedTextBox.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, copiedPicture.Id));

        command.Revert(ctx);

        wb.Sheets.Should().ContainSingle().Which.Id.Should().Be(sheet.Id);
    }

    // ── F14/F23 regression: Sheet.Clone must copy comment authors/shown-comments and the
    // ignored-errors/cell-watches metadata dictionaries, not just the plain Comments dictionary. ──
    [Fact]
    public void DuplicateSheetCommand_CopiesCommentAuthorsShownCommentsIgnoredErrorsAndCellWatches()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var a1 = new CellAddress(sheet.Id, 1, 1);

        sheet.Comments[a1] = "note";
        sheet.CommentAuthors[a1] = "Jane Doe";
        sheet.ShownComments.Add(a1);
        sheet.CellWatchesMetadata = new WorksheetCellWatchesMetadataModel();
        sheet.CellWatchesMetadata.WatchNativeAttributes["C5"] = new Dictionary<string, string> { ["xr:uid"] = "C5" };
        sheet.IgnoredErrorsMetadata = new WorksheetIgnoredErrorsMetadataModel();
        sheet.IgnoredErrorsMetadata.ErrorNativeAttributes["B5:C6"] = new Dictionary<string, string> { ["numberStoredAsText"] = "1" };

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();
        var copy = wb.Sheets[1];
        var copiedA1 = new CellAddress(copy.Id, 1, 1);

        copy.Comments[copiedA1].Should().Be("note");
        copy.CommentAuthors[copiedA1].Should().Be("Jane Doe");
        copy.ShownComments.Should().Contain(copiedA1);

        copy.CellWatchesMetadata.Should().NotBeNull();
        copy.CellWatchesMetadata!.WatchNativeAttributes.Should().ContainKey("C5");
        copy.CellWatchesMetadata.WatchNativeAttributes["C5"]["xr:uid"].Should().Be("C5");
        // Mutating the clone must not affect the original (deep copy, not a shared reference).
        copy.CellWatchesMetadata.WatchNativeAttributes["C5"]["xr:uid"] = "changed";
        sheet.CellWatchesMetadata!.WatchNativeAttributes["C5"]["xr:uid"].Should().Be("C5");

        copy.IgnoredErrorsMetadata.Should().NotBeNull();
        copy.IgnoredErrorsMetadata!.ErrorNativeAttributes.Should().ContainKey("B5:C6");
        copy.IgnoredErrorsMetadata.ErrorNativeAttributes["B5:C6"]["numberStoredAsText"].Should().Be("1");
        copy.IgnoredErrorsMetadata.ErrorNativeAttributes["B5:C6"]["numberStoredAsText"] = "changed";
        sheet.IgnoredErrorsMetadata!.ErrorNativeAttributes["B5:C6"]["numberStoredAsText"].Should().Be("1");
    }

    [Fact]
    public void DuplicateSheetCommand_CopiesChartDataTableFormatting()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var sourceDataTable = new ChartDataTableModel
        {
            ShowHorizontalBorder = false,
            ShowVerticalBorder = true,
            ShowOutline = false,
            ShowLegendKeys = true,
            FillColor = new CellColor(10, 20, 30),
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25),
            BorderColor = new CellColor(40, 50, 60),
            BorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25),
            BorderThickness = 2.5,
            TextColor = new CellColor(70, 80, 90),
            TextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            FontSize = 13.5
        };
        sheet.Charts.Add(new ChartModel
        {
            Name = "Sales Trend",
            Type = ChartType.Line,
            DataRange = new GridRange(a1, a1),
            DataTable = sourceDataTable
        });

        var command = new DuplicateSheetCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        var copiedChart = wb.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.DataTable.Should().NotBeSameAs(sourceDataTable);
        copiedChart.DataTable.Should().BeEquivalentTo(sourceDataTable);
    }

    [Fact]
    public void MoveSheetCommand_NoOpMove_DoesNotCreateUndoEntry()
    {
        var wb = new Workbook("test");
        wb.AddSheet("Jan");
        wb.AddSheet("Feb");
        var bus = new CommandBus(_ => new TestCommandContext(wb));

        var outcome = bus.Execute(wb.Id, new MoveSheetCommand(fromIndex: 0, toIndex: 0));

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeTrue();
        wb.Sheets.Select(sheet => sheet.Name).Should().Equal("Jan", "Feb");
        bus.CanUndo(wb.Id).Should().BeFalse();
        bus.GetUndoStackDepth(wb.Id).Should().Be(0);
    }

    [Fact]
    public void MoveSheetsCommand_DefaultSingleSheetTarget_DoesNotCreateUndoEntry()
    {
        var wb = new Workbook("test");
        var jan = wb.AddSheet("Jan");
        wb.AddSheet("Feb");
        wb.AddSheet("Mar");
        var bus = new CommandBus(_ => new TestCommandContext(wb));

        var outcome = bus.Execute(wb.Id, new MoveSheetsCommand([jan.Id], insertBeforeIndex: 0));

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeTrue();
        wb.Sheets.Select(sheet => sheet.Name).Should().Equal("Jan", "Feb", "Mar");
        bus.CanUndo(wb.Id).Should().BeFalse();
        bus.GetUndoStackDepth(wb.Id).Should().Be(0);
    }

    [Fact]
    public void MoveOrCopyCompositeCommand_UndoRemovesCopiedSheetInOneStep()
    {
        var wb = new Workbook("test");
        var jan = wb.AddSheet("Jan");
        wb.AddSheet("Feb");
        wb.AddSheet("Mar");
        var bus = new CommandBus(_ => new TestCommandContext(wb));
        var command = new CompositeWorkbookCommand(
            "Move or Copy Sheet",
            [
                new DuplicateSheetCommand(jan.Id),
                new MoveSheetCommand(fromIndex: 1, toIndex: 3)
            ]);

        var outcome = bus.Execute(wb.Id, command);

        outcome.Success.Should().BeTrue();
        wb.Sheets.Select(sheet => sheet.Name).Should().Equal("Jan", "Feb", "Mar", "Jan (2)");
        bus.GetUndoStackDepth(wb.Id).Should().Be(1);

        bus.Undo(wb.Id).Success.Should().BeTrue();

        wb.Sheets.Select(sheet => sheet.Name).Should().Equal("Jan", "Feb", "Mar");
        bus.GetUndoStackDepth(wb.Id).Should().Be(0);
    }

    [Fact]
    public void SetSheetHiddenCommand_HidesSheetAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);

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
        var ctx = new TestCommandContext(wb);

        var outcome = new SetSheetHiddenCommand(sheet1.Id, hidden: true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("visible");
        sheet1.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void SetSheetHiddenCommand_TreatsVeryHiddenSheetsAsNotVisible()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        sheet2.IsVeryHidden = true;
        var ctx = new TestCommandContext(wb);

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
        var ctx = new TestCommandContext(wb);
        sheet.TabColor = new CellColor(255, 0, 0);

        var command = new SetSheetTabColorCommand(sheet.Id, new CellColor(0, 176, 80));

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.TabColor.Should().Be(new CellColor(0, 176, 80));

        command.Revert(ctx);

        sheet.TabColor.Should().Be(new CellColor(255, 0, 0));
    }

    // ── X3 regression: Delete Sheet rewrites cross-sheet CF/DV formulas to #REF! ──

    [Fact]
    public void RemoveSheetCommand_RewritesCrossSeetCfAndDvFormulasToRefErrorAndUndoRestores()
    {
        // Sheet2 has a CF with FormulaText referencing Sheet1, and a DV Formula1/Formula2
        // referencing Sheet1.  Deleting Sheet1 should rewrite those to #REF!.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);

        var cfRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 5, 5)),
            RuleType = CfRuleType.Formula,
            FormulaText = "Sheet1!A1>0"
        };
        sheet2.ConditionalFormats.Add(cfRule);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 5, 5)),
            Type = DvType.Custom,
            Formula1 = "Sheet1!A1<>\"\"",
            AlertStyle = DvAlertStyle.Stop
        };
        sheet2.DataValidations.Add(dvRule);

        var cmd = new RemoveSheetCommand(sheet1.Id);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // Deleted sheet refs must become #REF!.
        cfRule.FormulaText.Should().Contain("#REF!", "CF FormulaText referencing deleted sheet becomes #REF!");
        dvRule.Formula1.Should().Contain("#REF!", "DV Formula1 referencing deleted sheet becomes #REF!");

        // Undo must restore the original formula strings.
        cmd.Revert(ctx);
        cfRule.FormulaText.Should().Be("Sheet1!A1>0", "undo restores CF formula after sheet delete");
        dvRule.Formula1.Should().Be("Sheet1!A1<>\"\"", "undo restores DV formula after sheet delete");
    }

    // ── F24 regression: Delete Sheet must clear/rewrite chart DataRange refs on OTHER sheets
    // that source from the deleted sheet, mirroring the existing pivot/slicer/picture handling. ──

    [Fact]
    public void RemoveSheetCommand_ClearsChartDataRangeReferencingDeletedSheetAndUndoRestores()
    {
        // Sheet2 hosts a chart whose DataRange sources data from Sheet1. Deleting Sheet1 must not
        // leave the chart pointing at the now-nonexistent sheet.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);

        var originalRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 5, 2));
        var chart = new ChartModel
        {
            Name = "Cross-Sheet Chart",
            Type = ChartType.Line,
            DataRange = originalRange
        };
        sheet2.Charts.Add(chart);

        var cmd = new RemoveSheetCommand(sheet1.Id);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Start.Sheet.Should().Be(sheet2.Id, "chart DataRange must no longer reference the deleted sheet");
        chart.DataRange.End.Sheet.Should().Be(sheet2.Id);

        // Undo must restore the original cross-sheet DataRange.
        cmd.Revert(ctx);
        chart.DataRange.Should().Be(originalRange, "undo restores the chart's original DataRange after sheet delete");
    }
}
