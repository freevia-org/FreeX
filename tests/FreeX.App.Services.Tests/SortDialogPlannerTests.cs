using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class SortDialogPlannerTests
{
    [Fact]
    public void BuildSortKeys_ReturnsTypedSortKeysInLevelOrder()
    {
        var levels = new[]
        {
            new SortDialogLevel(2, true) { SortOn = "Cell Values" },
            new SortDialogLevel(1, true) { SortOn = "Cell Color", TargetColor = "#FF0000" },
            new SortDialogLevel(0, false) { SortOn = "Font Color", TargetColor = "#0000FF" }
        };

        var keys = SortDialogPlanner.BuildSortKeys(levels);

        keys.Should().Equal(
            new SortKey(2, true),
            new SortKey(1, true, SortOn.CellColor, new CellColor(255, 0, 0)),
            new SortKey(0, false, SortOn.FontColor, new CellColor(0, 0, 255)));
    }

    [Fact]
    public void BuildSortKeys_MapsLabelsAndIgnoresColorForValueSorts()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true) { SortOn = "Cell Values", TargetColor = "#FF0000" },
            new SortDialogLevel(1, false) { SortOn = "Cell Color", TargetColor = "#00FF00" },
            new SortDialogLevel(2, true) { SortOn = "Font Color", TargetColor = "#0000FF" },
            new SortDialogLevel(3, true) { SortOn = "Unknown", TargetColor = "#FFFFFF" }
        };

        SortDialogPlanner.BuildSortKeys(levels).Should().Equal(
            new SortKey(0, true, SortOn.CellValues, null),
            new SortKey(1, false, SortOn.CellColor, new CellColor(0, 255, 0)),
            new SortKey(2, true, SortOn.FontColor, new CellColor(0, 0, 255)),
            new SortKey(3, true, SortOn.CellValues, null));
    }

    [Fact]
    public void ApplyCustomOrderToFirstKey_OnlyUpdatesPrimaryValueSort()
    {
        CustomSortOrder.TryParse("Sun, Mon, Tue", out var customOrder).Should().BeTrue();
        var keys = new[]
        {
            new SortKey(2, true),
            new SortKey(1, false, SortOn.CellColor, new CellColor(255, 0, 0))
        };

        var updated = SortDialogPlanner.ApplyCustomOrderToFirstKey(keys, customOrder);

        updated[0].CustomOrder.Should().Be(customOrder);
        updated[1].CustomOrder.Should().BeNull();
        SortDialogPlanner.ApplyCustomOrderToFirstKey(
                [new SortKey(1, false, SortOn.CellColor, new CellColor(255, 0, 0))],
                customOrder)[0]
            .CustomOrder
            .Should()
            .BeNull();
    }

    [Fact]
    public void BuildOrderChoices_UsesExcelColorSortLabelsForColorSorts()
    {
        SortDialogPlanner.BuildOrderChoices("Cell Values").Should().Equal(
            new SortDirectionChoice("A to Z", true),
            new SortDirectionChoice("Z to A", false));

        SortDialogPlanner.BuildOrderChoices("Cell Color").Should().Equal(
            new SortDirectionChoice("On Top", true),
            new SortDirectionChoice("On Bottom", false));

        SortDialogPlanner.BuildOrderChoices("Font Color").Should().Equal(
            new SortDirectionChoice("On Top", true),
            new SortDirectionChoice("On Bottom", false));
    }

    [Fact]
    public void TextCatalog_LocalizesDefaultLevelsChoicesAndLabels()
    {
        var text = new SortDialogPlannerText(
            "Valeurs",
            "Couleur de cellule",
            "Couleur de police",
            "Croissant",
            "Decroissant",
            "En haut",
            "En bas",
            "Colonne {0}",
            "Ligne {0}");
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 4, 4));

        var levels = SortDialogPlanner.NormalizeLevels(null, text);

        levels.Should().ContainSingle().Which.SortOn.Should().Be("Valeurs");
        SortDialogPlanner.BuildSortKeys([new SortDialogLevel(0, true, text) { SortOn = "Couleur de cellule", TargetColor = "#FF0000" }], text)
            .Should()
            .Equal(new SortKey(0, true, SortOn.CellColor, new CellColor(255, 0, 0)));
        SortDialogPlanner.BuildOrderChoices("Couleur de police", text).Should().Equal(
            new SortDirectionChoice("En haut", true),
            new SortDirectionChoice("En bas", false));
        SortDialogPlanner.BuildColumnChoices(range, text).Should().Equal(
            new SortColumnChoice("Colonne C", 0),
            new SortColumnChoice("Colonne D", 1));
        SortDialogPlanner.BuildRowChoices(range, text).Should().Equal(
            new SortColumnChoice("Ligne 2", 0),
            new SortColumnChoice("Ligne 3", 1),
            new SortColumnChoice("Ligne 4", 2));
    }

    [Fact]
    public void SortDialogLevel_RefreshesOrderChoicesWhenSortOnChanges()
    {
        var level = new SortDialogLevel(0, true);
        var changed = new List<string?>();
        level.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        level.SortOn = "Cell Color";

        level.OrderChoices.Should().Equal(
            new SortDirectionChoice("On Top", true),
            new SortDirectionChoice("On Bottom", false));
        changed.Should().Contain(nameof(SortDialogLevel.SortOn));
        changed.Should().Contain(nameof(SortDialogLevel.OrderChoices));
    }

    [Fact]
    public void LevelOperations_AddRemoveUpdateCopyAndMoveLevels()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true),
            new SortDialogLevel(1, false) { SortOn = "Font Color", TargetColor = "#FF0000" },
            new SortDialogLevel(2, true)
        };

        SortDialogPlanner.AddLevel([new SortDialogLevel(1, false)]).Should().Equal(
            new SortDialogLevel(1, false),
            new SortDialogLevel(0, true));
        SortDialogPlanner.RemoveLevel(levels, 0).Should().Equal(
            new SortDialogLevel(1, false) { SortOn = "Font Color", TargetColor = "#FF0000" },
            new SortDialogLevel(2, true));
        SortDialogPlanner.RemoveLevel([new SortDialogLevel(3, false)], 0)
            .Should()
            .Equal(new SortDialogLevel(0, true));
        SortDialogPlanner.UpdateLevel(levels, 1, columnOffset: 3, ascending: true)
            .Should()
            .Equal(
                new SortDialogLevel(0, true),
                new SortDialogLevel(3, true) { SortOn = "Font Color", TargetColor = "#FF0000" },
                new SortDialogLevel(2, true));
        SortDialogPlanner.CopyLevel(levels, 1)
            .Should()
            .Equal(
                new SortDialogLevel(0, true),
                new SortDialogLevel(1, false) { SortOn = "Font Color", TargetColor = "#FF0000" },
                new SortDialogLevel(1, false) { SortOn = "Font Color", TargetColor = "#FF0000" },
                new SortDialogLevel(2, true));
        SortDialogPlanner.MoveLevel(levels, 2, -1)
            .Should()
            .Equal(
                new SortDialogLevel(0, true),
                new SortDialogLevel(2, true),
                new SortDialogLevel(1, false) { SortOn = "Font Color", TargetColor = "#FF0000" });
    }

    [Fact]
    public void BuildActiveColumnChoices_UsesRowsForLeftToRightAndHeaderAwareColumnsOtherwise()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sales");
        sheet.SetCell(new CellAddress(sheetId, 4, 2), new TextValue("Region"));
        var range = new GridRange(
            new CellAddress(sheetId, 4, 2),
            new CellAddress(sheetId, 6, 4));
        var headerChoices = SortDialogPlanner.BuildColumnChoices(sheet, range, hasHeaders: true);
        var genericChoices = SortDialogPlanner.BuildColumnChoices(sheet, range, hasHeaders: false);
        var rowChoices = SortDialogPlanner.BuildRowChoices(range);

        SortDialogPlanner.BuildActiveColumnChoices(
                new SortDialogOptions(LeftToRight: false),
                hasHeaders: true,
                headerChoices,
                genericChoices,
                rowChoices)
            .Should()
            .Equal(headerChoices);
        SortDialogPlanner.BuildActiveColumnChoices(
                new SortDialogOptions(LeftToRight: false),
                hasHeaders: false,
                headerChoices,
                genericChoices,
                rowChoices)
            .Should()
            .Equal(genericChoices);
        SortDialogPlanner.BuildActiveColumnChoices(
                new SortDialogOptions(LeftToRight: true),
                hasHeaders: true,
                headerChoices,
                genericChoices,
                rowChoices)
            .Should()
            .Equal(rowChoices);
    }

    [Fact]
    public void BuildColorChoices_ListsDistinctFillAndFontColorsFromSelectedRange()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var red = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });
        var blue = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(0, 0, 255) });
        var redCell = Cell.FromValue(new TextValue("red"));
        redCell.StyleId = red;
        var blueCell = Cell.FromValue(new TextValue("blue"));
        blueCell.StyleId = blue;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), redCell);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), blueCell);

        SortDialogPlanner.BuildColorChoices(workbook, sheet, new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)))
            .Should()
            .Equal(new SortColorChoice(""), new SortColorChoice("#000000"), new SortColorChoice("#0000FF"), new SortColorChoice("#FF0000"));
    }

    [Fact]
    public void BuildColorChoices_ScopesChoicesToRequestedColorSortKind()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var fillStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });
        var fontStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(0, 0, 255) });
        var fillCell = Cell.FromValue(new TextValue("fill"));
        fillCell.StyleId = fillStyle;
        var fontCell = Cell.FromValue(new TextValue("font"));
        fontCell.StyleId = fontStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), fillCell);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), fontCell);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        SortDialogPlanner.BuildColorChoices(workbook, sheet, range, SortOn.CellColor)
            .Should()
            .Equal(new SortColorChoice(""), new SortColorChoice("#FF0000"));
        SortDialogPlanner.BuildColorChoices(workbook, sheet, range, SortOn.FontColor)
            .Should()
            .Equal(new SortColorChoice(""), new SortColorChoice("#000000"), new SortColorChoice("#0000FF"));
    }

    [Fact]
    public void BuildColumnChoices_UsesSelectedRangeColumnsAndHeaderValues()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sales");
        sheet.SetCell(new CellAddress(sheetId, 4, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 4, 3), new TextValue("Revenue"));
        var range = new GridRange(
            new CellAddress(sheetId, 4, 2),
            new CellAddress(sheetId, 12, 4));

        SortDialogPlanner.BuildColumnChoices(range).Should().Equal(
            new SortColumnChoice("Column B", 0),
            new SortColumnChoice("Column C", 1),
            new SortColumnChoice("Column D", 2));
        SortDialogPlanner.BuildColumnChoices(sheet, range, hasHeaders: true).Should().Equal(
            new SortColumnChoice("Region", 0),
            new SortColumnChoice("Revenue", 1),
            new SortColumnChoice("Column D", 2));
    }

    [Fact]
    public void ExcludeHeaderRow_RemovesFirstRowOnlyWhenHeaderRowIsEnabled()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 7, 5));

        SortDialogPlanner.ExcludeHeaderRow(range, hasHeaders: true).Should().Be(new GridRange(
            new CellAddress(sheetId, 3, 3),
            new CellAddress(sheetId, 7, 5)));

        SortDialogPlanner.ExcludeHeaderRow(range, hasHeaders: false).Should().Be(range);
        SortDialogPlanner.ExcludeHeaderRow(new GridRange(range.Start, range.Start), hasHeaders: true)
            .Should()
            .Be(new GridRange(range.Start, range.Start));
    }
}
