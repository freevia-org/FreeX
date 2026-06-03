using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxChartDataLabelFormatTests
{
    // A chart whose data labels carry no explicit number format must not gain one on round-trip. The writer
    // emits the required numFmt with formatCode="General" (the default), so the reader treats that default as
    // "no explicit format" — otherwise an unformatted chart drifts from <none> to "General".
    [Fact]
    public void XlsxAdapter_RoundTrip_DoesNotInventGeneralDataLabelNumberFormat()
    {
        var workbook = new Workbook("ChartDataLabelFormat");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            ShowDataLabels = true,
        });

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.ShowDataLabels.Should().BeTrue();
        chart.DataLabelNumberFormatCode.Should().BeNullOrEmpty(
            "a default (General) data-label format must not be invented on round-trip");
    }
}
