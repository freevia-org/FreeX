using System.Text;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ClipboardHtmlSerializerTests
{
    [Fact]
    public void Serialize_ProducesStyledFragmentAndValidCfHtmlPayload()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var style = new CellStyle { Bold = true, FillColor = new CellColor(170, 187, 204) };
        var viewport = new ViewportModel(
            [new DisplayCell(1, 1, new TextValue("Bold Fill"), "Bold Fill", null, default, null, style)],
            [],
            []);

        var payload = ClipboardHtmlSerializer.Serialize(viewport, sheet, new GridRange(address, address), workbook.Theme);

        payload.Should().NotBeNull();
        payload!.Fragment.Should().Contain("<table");
        payload.Fragment.Should().Contain("font-weight:bold");
        payload.Fragment.Should().Contain("background-color:#AABBCC");
        payload.CfHtml.Should().StartWith("Version:0.9\r\n");

        var bytes = Encoding.UTF8.GetBytes(payload.CfHtml);
        var start = ParseOffset(payload.CfHtml, "StartFragment:");
        var end = ParseOffset(payload.CfHtml, "EndFragment:");
        Encoding.UTF8.GetString(bytes, start, end - start).Should().Be(payload.Fragment);
    }

    [Fact]
    public void Serialize_ClipsMergedRegionWithoutDroppingCopiedColumns()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 2));
        var viewport = new ViewportModel(
            [new DisplayCell(2, 2, new TextValue("B2"), "B2", null, default, null)],
            [],
            []);

        var payload = ClipboardHtmlSerializer.Serialize(viewport, sheet, range, workbook.Theme);

        payload.Should().NotBeNull();
        payload!.Fragment.Should().Contain("rowspan=\"2\"");
        payload.Fragment.Should().Contain("B2");
    }

    private static int ParseOffset(string payload, string field)
    {
        var start = payload.IndexOf(field, StringComparison.Ordinal) + field.Length;
        return int.Parse(payload.AsSpan(start, 10));
    }
}
