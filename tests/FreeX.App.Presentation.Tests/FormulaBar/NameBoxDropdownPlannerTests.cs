using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FormulaBar;

public sealed class NameBoxDropdownPlannerTests
{
    [Fact]
    public void Build_ProjectsDefinedNamesTablesAndVisibleNamedObjectsInDeterministicOrder()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 3));
        workbook.DefineNamedRange("Zebra", range);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "AppleTable",
            DisplayName = "AppleTable",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 3)),
        });
        var shape = new DrawingShapeModel
        {
            Name = "MiddleShape",
            Anchor = new CellAddress(sheet.Id, 7, 2),
        };
        sheet.DrawingShapes.Add(shape);
        var picture = new PictureModel
        {
            Name = "MiddlePicture",
            Anchor = new CellAddress(sheet.Id, 8, 2),
            Kind = PictureKind.Image,
        };
        sheet.Pictures.Add(picture);
        var textBox = new TextBoxModel
        {
            Name = "MiddleTextBox",
            Anchor = new CellAddress(sheet.Id, 9, 2),
        };
        sheet.TextBoxes.Add(textBox);
        var chart = new ChartModel
        {
            Name = "MiddleChart",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 10, 2),
                new CellAddress(sheet.Id, 11, 3)),
        };
        sheet.Charts.Add(chart);

        var items = NameBoxDropdownPlanner.Build(workbook, sheet.Id);

        items.Select(item => (item.Name, item.Kind)).Should().Equal(
            ("AppleTable", NameBoxNavigationItemKind.Table),
            ("MiddleChart", NameBoxNavigationItemKind.Object),
            ("MiddlePicture", NameBoxNavigationItemKind.Object),
            ("MiddleShape", NameBoxNavigationItemKind.Object),
            ("MiddleTextBox", NameBoxNavigationItemKind.Object),
            ("Zebra", NameBoxNavigationItemKind.DefinedName));
        items.Single(item => item.Name == "AppleTable").Range.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 5, 3)));
        items.Single(item => item.Name == "MiddleShape").ObjectId.Should().Be(shape.Id);
        items.Single(item => item.Name == "MiddleChart").ObjectKind.Should().Be(SelectionPaneObjectKind.Chart);
        items.Single(item => item.Name == "MiddlePicture").ObjectKind.Should().Be(SelectionPaneObjectKind.Picture);
        items.Single(item => item.Name == "MiddleTextBox").ObjectKind.Should().Be(SelectionPaneObjectKind.TextBox);
    }

    [Fact]
    public void Build_IncludesOnlyActiveSheetScopedNamesButIncludesNavigableEntriesOnOtherSheets()
    {
        var workbook = new Workbook("Book1");
        var active = workbook.AddSheet("Active");
        var other = workbook.AddSheet("Other");
        workbook.DefineNamedRange(
            "ActiveOnly",
            new GridRange(new CellAddress(active.Id, 2, 2), new CellAddress(active.Id, 2, 2)),
            metadata: null,
            scopeSheetId: active.Id);
        workbook.DefineNamedRange(
            "OtherOnly",
            new GridRange(new CellAddress(other.Id, 3, 3), new CellAddress(other.Id, 3, 3)),
            metadata: null,
            scopeSheetId: other.Id);
        other.Pictures.Add(new PictureModel
        {
            Name = "OtherPicture",
            Anchor = new CellAddress(other.Id, 4, 4),
        });

        var items = NameBoxDropdownPlanner.Build(workbook, active.Id);

        items.Select(item => item.Name).Should().Contain("ActiveOnly");
        items.Select(item => item.Name).Should().NotContain("OtherOnly");
        var picture = items.Single(item => item.Name == "OtherPicture");
        picture.SheetId.Should().Be(other.Id);
        picture.ObjectKind.Should().Be(SelectionPaneObjectKind.Picture);
    }

    [Fact]
    public void Build_PreservesDuplicateLabelsWithStableKindOrdering()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var sameNameRange = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 2, 2));
        workbook.DefineNamedRange("Shared", sameNameRange);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 2,
            Name = "Shared",
            DisplayName = "Shared",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
        });
        var shape = new DrawingShapeModel
        {
            Name = "Shared",
            Anchor = new CellAddress(sheet.Id, 5, 5),
        };
        sheet.DrawingShapes.Add(shape);

        var items = NameBoxDropdownPlanner.Build(workbook, sheet.Id);

        items.Where(item => item.Name == "Shared").Select(item => item.Kind).Should().Equal(
            NameBoxNavigationItemKind.DefinedName,
            NameBoxNavigationItemKind.Table,
            NameBoxNavigationItemKind.Object);
        items[0].Range.Should().Be(sameNameRange);
        items[1].Range.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 2)));
        items[2].ObjectId.Should().Be(shape.Id);
    }
}
