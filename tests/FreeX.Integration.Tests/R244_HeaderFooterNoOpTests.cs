using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r244: SetHeaderFooterCommand -- sixteen fields, six of them picture sets, and the picture sets are
/// why this one needed care rather than width.
/// <para>
/// <c>WorksheetHeaderFooterPicture</c> is a record, so <c>==</c> compares its fields -- but
/// <c>ImageBytes</c> is a <c>byte[]</c>, which records compare BY REFERENCE. The snapshot is taken
/// with <c>DeepClone</c>, which copies the array, so a comparison built on <c>Equals</c> would have
/// compiled, read correctly, and never once reported a no-op. The test with an identical picture on
/// both sides is the one that catches that.
/// </para>
/// </summary>
public sealed class R244_HeaderFooterNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static WorksheetHeaderFooter Text(string center) => new("", center, "");

    [Fact]
    public void PressingOkWithoutEditing_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new SetHeaderFooterCommand(sheet.Id, sheet.PageHeader, sheet.PageFooter).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ChangingTheHeaderText_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();

        var outcome = new SetHeaderFooterCommand(sheet.Id, Text("Report"), sheet.PageFooter)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.PageHeader.Center.Should().Be("Report");
    }

    [Fact]
    public void ReSubmittingAnIdenticalHeaderPicture_ReportsNoOp()
    {
        // The case a record-equality comparison gets wrong every time. The sheet holds a picture and
        // the command is handed a separate object with the same bytes -- which is exactly what the
        // dialog does after a round trip through the model's DeepClone.
        var (sheet, ctx) = Fixture();
        var bytes = new byte[] { 1, 2, 3, 4 };
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture(bytes, "image/png"), null, null);

        var identical = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture([1, 2, 3, 4], "image/png"), null, null);

        new SetHeaderFooterCommand(
                sheet.Id, sheet.PageHeader, sheet.PageFooter, headerPictures: identical)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue("the images are byte-for-byte the same picture");
    }

    [Fact]
    public void ChangingAHeaderPicturesBytes_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture([1, 2, 3, 4], "image/png"), null, null);

        var different = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture([9, 9, 9, 9], "image/png"), null, null);

        new SetHeaderFooterCommand(
                sheet.Id, sheet.PageHeader, sheet.PageFooter, headerPictures: different)
            .Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void ChangingOnlyTheAlignWithMarginsFlag_DoesNotReportNoOp()
    {
        // Deliberately the LAST of the sixteen fields: a comparison that transcribed fifteen would
        // pass every other test in this file.
        var (sheet, ctx) = Fixture();
        sheet.HeaderFooterAlignWithMargins.Should().BeTrue();

        new SetHeaderFooterCommand(
                sheet.Id, sheet.PageHeader, sheet.PageFooter, alignWithMargins: false)
            .Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }
}
