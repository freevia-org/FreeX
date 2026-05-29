using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxHeaderFooterPicturePackagePlannerTests
{
    [Fact]
    public void Slots_MapAllExcelHeaderFooterPictureShapeIds()
    {
        XlsxHeaderFooterPicturePackagePlanner.Slots.Select(slot => slot.ShapeId)
            .Should()
            .Equal(
                "LH", "CH", "RH",
                "LF", "CF", "RF",
                "LFH", "CFH", "RFH",
                "LFF", "CFF", "RFF",
                "LEH", "CEH", "REH",
                "LEF", "CEF", "REF");
    }

    [Fact]
    public void GetPicture_ReadsTheRequestedSetAndPosition()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var firstPageCenter = Picture("first-center.png", width: 120, height: 48);
        var evenFooterRight = Picture("even-right.png", width: 96, height: 32);
        sheet.FirstPageHeaderPictures = new WorksheetHeaderFooterPictureSet(null, firstPageCenter, null);
        sheet.EvenPageFooterPictures = new WorksheetHeaderFooterPictureSet(null, null, evenFooterRight);

        XlsxHeaderFooterPicturePackagePlanner.GetPicture(
                sheet,
                XlsxHeaderFooterPictureSetKind.FirstPageHeader,
                XlsxHeaderFooterPicturePosition.Center)
            .Should()
            .BeSameAs(firstPageCenter);
        XlsxHeaderFooterPicturePackagePlanner.GetPicture(
                sheet,
                XlsxHeaderFooterPictureSetKind.EvenPageFooter,
                XlsxHeaderFooterPicturePosition.Right)
            .Should()
            .BeSameAs(evenFooterRight);
        XlsxHeaderFooterPicturePackagePlanner.GetPicture(
                sheet,
                XlsxHeaderFooterPictureSetKind.PageHeader,
                XlsxHeaderFooterPicturePosition.Left)
            .Should()
            .BeNull();
    }

    [Fact]
    public void ToSet_GroupsPicturesByPosition()
    {
        var left = Picture("left.png", width: 10, height: 20);
        var center = Picture("center.png", width: 30, height: 40);
        var pictures = new Dictionary<(XlsxHeaderFooterPictureSetKind Kind, XlsxHeaderFooterPicturePosition Position), WorksheetHeaderFooterPicture>
        {
            [(XlsxHeaderFooterPictureSetKind.PageHeader, XlsxHeaderFooterPicturePosition.Left)] = left,
            [(XlsxHeaderFooterPictureSetKind.PageHeader, XlsxHeaderFooterPicturePosition.Center)] = center,
            [(XlsxHeaderFooterPictureSetKind.PageFooter, XlsxHeaderFooterPicturePosition.Left)] = Picture("footer.png", width: 50, height: 60)
        };

        var set = XlsxHeaderFooterPicturePackagePlanner.ToSet(pictures, XlsxHeaderFooterPictureSetKind.PageHeader);

        set.Left.Should().BeSameAs(left);
        set.Center.Should().BeSameAs(center);
        set.Right.Should().BeNull();
    }

    [Theory]
    [InlineData("width:120px;height:48px", "width", 120)]
    [InlineData("WIDTH : 90.5px ; HEIGHT : 72px", "height", 72)]
    [InlineData("width:72pt;height:36pt", "width", 96)]
    [InlineData("position:absolute;width:12.25;height:5", "width", 12.25)]
    public void ParseStyleDimension_ReadsPixelsAndConvertsPoints(string style, string name, double expected)
    {
        XlsxHeaderFooterPicturePackagePlanner.ParseStyleDimension(style, name)
            .Should()
            .BeApproximately(expected, 0.0001);
    }

    [Theory]
    [InlineData("", "width")]
    [InlineData("height:abc", "height")]
    [InlineData("left:10px", "width")]
    public void ParseStyleDimension_ReturnsNullWhenMissingOrInvalid(string style, string name)
    {
        XlsxHeaderFooterPicturePackagePlanner.ParseStyleDimension(style, name)
            .Should()
            .BeNull();
    }

    [Theory]
    [InlineData("logo.png", ".jpg", "logo.png")]
    [InlineData("logo", ".png", "logo.png")]
    [InlineData("", ".jpeg", "freexHeaderFooter3_7.jpeg")]
    [InlineData("   ", ".gif", "freexHeaderFooter3_7.gif")]
    public void GetMediaFileName_PreservesValidNamesAndFallsBackWhenMissing(
        string? fileName,
        string extension,
        string expected)
    {
        XlsxHeaderFooterPicturePackagePlanner.GetMediaFileName(fileName, sheetIndex: 3, pictureIndex: 7, extension)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void PictureSetsEqual_UsesImageMetadataAndBytes()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1")
        {
            PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
                Picture("logo.png", width: 120, height: 48, bytes: [1, 2, 3]),
                null,
                null)
        };
        var source = new XlsxHeaderFooterPictureSets(
            sheet.PageHeaderPictures,
            WorksheetHeaderFooterPictureSet.Empty,
            WorksheetHeaderFooterPictureSet.Empty,
            WorksheetHeaderFooterPictureSet.Empty,
            WorksheetHeaderFooterPictureSet.Empty,
            WorksheetHeaderFooterPictureSet.Empty);

        XlsxHeaderFooterPicturePackagePlanner.PictureSetsEqual(source, sheet)
            .Should()
            .BeTrue();

        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            Picture("logo.png", width: 120, height: 48, bytes: [1, 2, 4]),
            null,
            null);

        XlsxHeaderFooterPicturePackagePlanner.PictureSetsEqual(source, sheet)
            .Should()
            .BeFalse();
    }

    private static WorksheetHeaderFooterPicture Picture(
        string fileName,
        double width,
        double height,
        byte[]? bytes = null) =>
        new(bytes ?? [1, 2, 3], "image/png", fileName, width, height);
}
