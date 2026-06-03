using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed partial class PageLayoutCommandTests
{
    [Fact]
    public void SetHeaderFooterCommand_SetsHeaderFooterAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PageHeader = new WorksheetHeaderFooter("Old left", "", "Old right");
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &[Page]", "");
        sheet.FirstPageHeader = new WorksheetHeaderFooter("Old first", "", "");
        sheet.FirstPageFooter = new WorksheetHeaderFooter("", "Old first footer", "");
        sheet.EvenPageHeader = new WorksheetHeaderFooter("Old even", "", "");
        sheet.EvenPageFooter = new WorksheetHeaderFooter("", "Old even footer", "");
        sheet.DifferentFirstPageHeaderFooter = true;
        sheet.DifferentOddEvenHeaderFooter = true;
        sheet.HeaderFooterScaleWithDocument = false;
        sheet.HeaderFooterAlignWithMargins = false;

        var command = new SetHeaderFooterCommand(
            sheet.Id,
            new WorksheetHeaderFooter("Left", "Center", "Right"),
            new WorksheetHeaderFooter("Footer left", "Footer center", "Footer right"),
            firstPageHeader: new WorksheetHeaderFooter("First left", "First center", "First right"),
            firstPageFooter: new WorksheetHeaderFooter("First footer left", "First footer center", "First footer right"),
            evenPageHeader: new WorksheetHeaderFooter("Even left", "Even center", "Even right"),
            evenPageFooter: new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right"),
            differentFirstPage: true,
            differentOddEvenPages: true,
            scaleWithDocument: true,
            alignWithMargins: true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PageHeader.Should().Be(new WorksheetHeaderFooter("Left", "Center", "Right"));
        sheet.PageFooter.Should().Be(new WorksheetHeaderFooter("Footer left", "Footer center", "Footer right"));
        sheet.FirstPageHeader.Should().Be(new WorksheetHeaderFooter("First left", "First center", "First right"));
        sheet.FirstPageFooter.Should().Be(new WorksheetHeaderFooter("First footer left", "First footer center", "First footer right"));
        sheet.EvenPageHeader.Should().Be(new WorksheetHeaderFooter("Even left", "Even center", "Even right"));
        sheet.EvenPageFooter.Should().Be(new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right"));
        sheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
        sheet.DifferentOddEvenHeaderFooter.Should().BeTrue();
        sheet.HeaderFooterScaleWithDocument.Should().BeTrue();
        sheet.HeaderFooterAlignWithMargins.Should().BeTrue();

        command.Revert(ctx);

        sheet.PageHeader.Should().Be(new WorksheetHeaderFooter("Old left", "", "Old right"));
        sheet.PageFooter.Should().Be(new WorksheetHeaderFooter("", "Page &[Page]", ""));
        sheet.FirstPageHeader.Should().Be(new WorksheetHeaderFooter("Old first", "", ""));
        sheet.FirstPageFooter.Should().Be(new WorksheetHeaderFooter("", "Old first footer", ""));
        sheet.EvenPageHeader.Should().Be(new WorksheetHeaderFooter("Old even", "", ""));
        sheet.EvenPageFooter.Should().Be(new WorksheetHeaderFooter("", "Old even footer", ""));
        sheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
        sheet.DifferentOddEvenHeaderFooter.Should().BeTrue();
        sheet.HeaderFooterScaleWithDocument.Should().BeFalse();
        sheet.HeaderFooterAlignWithMargins.Should().BeFalse();
    }

    [Fact]
    public void SetHeaderFooterCommand_SetsHeaderFooterPicturesAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var oldPicture = new WorksheetHeaderFooterPicture([1, 2, 3], "image/png", "old.png", 16, 16);
        var newPicture = new WorksheetHeaderFooterPicture([4, 5, 6], "image/png", "logo.png", 120, 40);
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(oldPicture, null, null);

        var command = new SetHeaderFooterCommand(
            sheet.Id,
            new WorksheetHeaderFooter("&[Picture]", "", ""),
            new WorksheetHeaderFooter("", "", ""),
            headerPictures: new WorksheetHeaderFooterPictureSet(null, newPicture, null));

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PageHeaderPictures.Center.Should().NotBeNull();
        sheet.PageHeaderPictures.Center!.FileName.Should().Be("logo.png");
        sheet.PageHeaderPictures.Center.Width.Should().Be(120);

        command.Revert(ctx);

        sheet.PageHeaderPictures.Left.Should().NotBeNull();
        sheet.PageHeaderPictures.Left!.FileName.Should().Be("old.png");
        sheet.PageHeaderPictures.Center.Should().BeNull();
    }
}
