using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public class CustomViewPrintSettingsTests
{
    [Fact]
    public void SavedViewRestoresPrintSettingsAndUndoRestoresPreviouslyNullFields()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        sheet.PageHeader = new("Left", "Saved", "Right");
        sheet.FirstPageFooter = new("First", "", "");
        sheet.DifferentFirstPageHeaderFooter = true;
        sheet.CenterHorizontallyOnPage = true;
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 2);
        sheet.FitToPage = null;
        sheet.LegacyPrintSize = 2;
        sheet.PageSetupMetadata = new NativeXmlPreserveBag();
        sheet.PageSetupMetadata.Set("pageSetup", "<e custom=\"saved\" />");
        sheet.RowPageBreaks.Add(10);
        sheet.RowPageBreaksMetadata = new WorksheetPageBreaksMetadataModel();
        sheet.RowPageBreaksMetadata.BreakNativeAttributes[10] = new() { ["max"] = "3" };
        sheet.PageHeaderPictures = new(new WorksheetHeaderFooterPicture([1, 2, 3], "image/png"), null, null);
        new SaveCustomViewCommand("Saved").Apply(context).Success.Should().BeTrue();

        sheet.PageHeaderPictures.Left!.ImageBytes[0] = 99;
        sheet.PageHeader = new("", "Changed", "");
        sheet.FirstPageFooter = default;
        sheet.DifferentFirstPageHeaderFooter = false;
        sheet.CenterHorizontallyOnPage = false;
        sheet.PrintTitleRows = null;
        sheet.FitToPage = true;
        sheet.LegacyPrintSize = null;
        sheet.PageSetupMetadata!.Set("pageSetup", "<e custom=\"changed\" />");
        sheet.RowPageBreaks.Clear();
        sheet.RowPageBreaksMetadata!.BreakNativeAttributes[10]["max"] = "7";
        var apply = new ApplyCustomViewCommand("Saved");
        apply.Apply(context).Success.Should().BeTrue();
        sheet.PageHeader.Center.Should().Be("Saved");
        sheet.FirstPageFooter.Left.Should().Be("First");
        sheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
        sheet.CenterHorizontallyOnPage.Should().BeTrue();
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 2));
        sheet.FitToPage.Should().BeNull();
        sheet.LegacyPrintSize.Should().Be(2);
        sheet.PageSetupMetadata!.Get("pageSetup").Should().Contain("saved");
        sheet.RowPageBreaks.Should().Equal(10u);
        sheet.RowPageBreaksMetadata!.BreakNativeAttributes[10]["max"].Should().Be("3");
        sheet.PageHeaderPictures.Left!.ImageBytes.Should().Equal(99, 2, 3);
        apply.Revert(context);
        sheet.PageHeader.Center.Should().Be("Changed");
        sheet.PrintTitleRows.Should().BeNull();
        sheet.FitToPage.Should().BeTrue();
        sheet.LegacyPrintSize.Should().BeNull();
        sheet.PageSetupMetadata!.Get("pageSetup").Should().Contain("changed");
        sheet.RowPageBreaks.Should().BeEmpty();
        sheet.RowPageBreaksMetadata!.BreakNativeAttributes[10]["max"].Should().Be("7");
        sheet.PageHeaderPictures.Left!.ImageBytes.Should().Equal(99, 2, 3);
        apply.Apply(context).Success.Should().BeTrue();
        sheet.PageHeader.Center.Should().Be("Saved");
    }

    [Fact]
    public void CapturedNullValuesAreRestoredAndUndoRestoresPreviousValues()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var ctx = new TestCommandContext(workbook);
        sheet.AutoPageBreaks = null;
        sheet.LegacyPrintSize = null;
        new SaveCustomViewCommand("View").Apply(ctx).Success.Should().BeTrue();
        sheet.AutoPageBreaks = true;
        sheet.LegacyPrintSize = 3;
        var command = new ApplyCustomViewCommand("View");
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.AutoPageBreaks.Should().BeNull();
        sheet.LegacyPrintSize.Should().BeNull();
        command.Revert(ctx);
        sheet.AutoPageBreaks.Should().BeTrue();
        sheet.LegacyPrintSize.Should().Be(3);
    }

    [Fact]
    public void OpaquePrintTitlesLeaveLiveTitlesUntilEditedAndUndoRestoresThem()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var ctx = new TestCommandContext(workbook);
        var originalRows = new WorksheetRepeatRange(2, 3);
        var originalColumns = new WorksheetRepeatRange(1, 2);
        sheet.PrintTitleRows = originalRows;
        sheet.PrintTitleColumns = originalColumns;
        sheet.PageHeader = new("", "Live", "");
        var state = new WorksheetCustomViewState(
            sheet.Name,
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null)
        {
            PrintSettings = new WorksheetCustomViewPrintSettings { PageHeader = new("", "Saved", "") },
            OpaquePrintTitlesExpression = "FILTER(Data!$A$1:$A$2,Data!$B$1:$B$2)"
        };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        var command = new ApplyCustomViewCommand("View");
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintTitleRows.Should().Be(originalRows);
        sheet.PrintTitleColumns.Should().Be(originalColumns);
        sheet.PageHeader.Center.Should().Be("Saved");
        command.Revert(ctx);
        sheet.PrintTitleRows.Should().Be(originalRows);
        sheet.PrintTitleColumns.Should().Be(originalColumns);
        sheet.PageHeader.Center.Should().Be("Live");

        // Supplying a modeled range invalidates the opaque expression for application.
        var replacement = new WorksheetRepeatRange(7, 8);
        workbook.CustomViews[0] = workbook.CustomViews[0] with
        {
            Sheets = [state with { PrintSettings = state.PrintSettings! with { PrintTitleRows = replacement } }]
        };
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintTitleRows.Should().Be(replacement);
        sheet.PrintTitleColumns.Should().BeNull();
        command.Revert(ctx);
        sheet.PrintTitleRows.Should().Be(originalRows);
        sheet.PrintTitleColumns.Should().Be(originalColumns);

        // Explicitly clearing the opaque expression applies captured empty title ranges.
        workbook.CustomViews[0] = workbook.CustomViews[0] with
        {
            Sheets = [state with { OpaquePrintTitlesExpression = null }]
        };
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintTitleRows.Should().BeNull();
        sheet.PrintTitleColumns.Should().BeNull();
        command.Revert(ctx);
        sheet.PrintTitleRows.Should().Be(originalRows);
        sheet.PrintTitleColumns.Should().Be(originalColumns);
    }

    [Fact]
    public void DisabledPrintFlagIgnoresLoadedPrintSettingsEvenWhenHiddenSettingsEnabled()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        sheet.PageHeader = new("", "Live", "");
        sheet.PrintGridlines = true;
        var state = new WorksheetCustomViewState(
            "Sheet1",
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null,
            HiddenRows: [5],
            PrintGridlines: false)
        {
            PrintSettings = new WorksheetCustomViewPrintSettings { PageHeader = new("", "Saved", "") }
        };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state], IncludePrintSettings: false));
        new ApplyCustomViewCommand("View").Apply(ctx).Success.Should().BeTrue();
        sheet.HiddenRows.Should().Contain(5);
        sheet.PageHeader.Center.Should().Be("Live");
        sheet.PrintGridlines.Should().BeTrue();
    }

    [Fact]
    public void DisabledHiddenFlagIgnoresLoadedHiddenSettingsWhenPrintEnabled()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        sheet.HiddenRows.Add(7);
        var state = new WorksheetCustomViewState("Sheet1", WorksheetViewMode.Normal, 0, 0, null, null,
            HiddenRows: [5], PrintGridlines: true);
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state], IncludeHiddenRowsColumnsAndFilterSettings: false));
        new ApplyCustomViewCommand("View").Apply(ctx).Success.Should().BeTrue();
        sheet.HiddenRows.Should().Equal(7u);
        sheet.PrintGridlines.Should().BeTrue();
    }

    [Fact]
    public void ViewWithoutCapturedPicturesDoesNotClearLiveHeaderFooterPictures()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageHeaderPictures = new(new WorksheetHeaderFooterPicture([1, 2, 3], "image/png"), null, null);
        var state = new WorksheetCustomViewState(
            "Sheet1",
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null)
        {
            PrintSettings = new WorksheetCustomViewPrintSettings { PageHeader = new("", "Saved", "") }
        };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        new ApplyCustomViewCommand("View").Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.PageHeader.Center.Should().Be("Saved");
        sheet.PageHeaderPictures.Left!.ImageBytes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void ResavingUnchangedPrintSnapshotIsNoOp()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.RowPageBreaks.Add(10);
        sheet.PageHeaderPictures = new(new WorksheetHeaderFooterPicture([1, 2, 3], "image/png"), null, null);
        var ctx = new TestCommandContext(workbook);
        new SaveCustomViewCommand("View").Apply(ctx).Success.Should().BeTrue();
        new SaveCustomViewCommand("View").Apply(ctx).IsNoOp.Should().BeTrue();
        sheet.PageHeader = new("", "Changed", "");
        new SaveCustomViewCommand("View").Apply(ctx).IsNoOp.Should().BeFalse();
    }
}
