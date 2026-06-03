using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class DataToolDialogTests
{
    [Fact]
    public void TextToColumnsPreview_UsesSelectedTextRows()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East,42,Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West;7;Closed"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue(""));

        TextToColumnsDialog.BuildPreviewRows(sheet, range).Should().Equal("East,42,Open", "West;7;Closed");
    }

    [Fact]
    public void TextToColumnsDialog_AllowsOnlySingleColumnSelections()
    {
        var sheetId = SheetId.New();

        TextToColumnsDialog.CanConvertRange(new GridRange(
                new CellAddress(sheetId, 2, 1),
                new CellAddress(sheetId, 8, 1)))
            .Should()
            .BeTrue();

        TextToColumnsDialog.CanConvertRange(new GridRange(
                new CellAddress(sheetId, 2, 1),
                new CellAddress(sheetId, 8, 2)))
            .Should()
            .BeFalse();
    }
}
