using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class NativeJsonMalformedNestedCollectionTests
{
    [Fact]
    public void Load_TreatsNullGradientStopsAsNoGradient()
    {
        const string json = """
            {
              "Name": "Null gradient stops",
              "DefaultStyle": {
                "GradientFill": { "Stops": null }
              },
              "Sheets": [{ "Name": "Sheet1" }]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.GetStyle(StyleId.Default).GradientFill.Should().BeNull();
    }

    [Fact]
    public void Load_TreatsNullNestedFilterValueListsAsEmpty()
    {
        const string json = """
            {
              "Name": "Null filter values",
              "Sheets": [{
                "Name": "Sheet1",
                "ActiveValueFilterColumns": [{ "Index": 1, "Values": null }],
                "ColumnFilterOwnedRows": [{ "Index": 1, "Values": null }]
              }]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var sheet = new NativeJsonAdapter().Load(stream).GetSheetAt(0);

        sheet.ActiveValueFilterColumns[1].Should().BeEmpty();
        sheet.ColumnFilterOwnedRows[1].Should().BeEmpty();
    }

    [Fact]
    public void Load_TreatsNullGradientStopsInInlineCellStyleAsNoGradient()
    {
        const string json = """
            {
              "Name": "Null inline gradient stops",
              "Sheets": [{
                "Name": "Sheet1",
                "Cells": [{
                  "Address": "A1",
                  "ValueType": "t",
                  "Value": "value",
                  "Style": { "GradientFill": { "Stops": null } }
                }]
              }]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);
        var sheet = workbook.GetSheetAt(0);
        var cell = sheet.GetCell(new CellAddress(sheet.Id, 1, 1));

        cell.Should().NotBeNull();
        workbook.GetStyle(cell!.StyleId).GradientFill
            .Should().BeNull();
    }
}
