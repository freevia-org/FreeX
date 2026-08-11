using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.QuickParts;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FinalCommandParityPlannerTests
{
    [Theory]
    [InlineData("4", "7", 4, 7)]
    [InlineData("invalid", "0", 3, 1)]
    [InlineData("100", "999", 63, 63)]
    public void DrawTableDimensions_NormalizeToSharedWpfDefaultsAndLimits(
        string rows,
        string columns,
        int expectedRows,
        int expectedColumns)
    {
        DrawTableCommandPlanner.Normalize(rows, columns)
            .Should().Be((expectedRows, expectedColumns));
    }

    [Fact]
    public void DrawTableDialogs_ShareLocalizedMetadataAndDefaults()
    {
        string? Localize(string key) => $"localized:{key}";

        var draw = DrawTableCommandPlanner.BuildDialog(DrawTableDimensionDialogKind.DrawTable, Localize);
        var split = DrawTableCommandPlanner.BuildDialog(DrawTableDimensionDialogKind.SplitCells, Localize);

        draw.Title.Should().Be("localized:DrawTable_Dialog_Title");
        draw.DefaultRows.Should().Be(DrawTableCommandPlanner.DefaultRows);
        draw.DefaultColumns.Should().Be(DrawTableCommandPlanner.DefaultColumns);
        split.Title.Should().Be("localized:SplitCells_Dialog_Title");
        split.DefaultRows.Should().Be(DrawTableCommandPlanner.SplitDefaultRows);
        split.DefaultColumns.Should().Be(DrawTableCommandPlanner.SplitDefaultColumns);
    }

    [Fact]
    public void AltTextDialog_ResolvesSharedLocalizedPrompts()
    {
        var text = AltTextDialogPlanner.ResolveText(key => $"localized:{key}");

        text.Title.Should().Be("localized:AltText_Dialog_Title");
        text.DescriptionLabel.Should().Be("localized:AltText_Description_Label");
        text.ImageSelectionRequiredMessage.Should().Be("localized:AltText_ImageSelectionRequired_Message");
        text.ShapeSelectionRequiredMessage.Should().Be("localized:AltText_ShapeSelectionRequired_Message");
    }

    [Fact]
    public void QuickPartSelection_PreservesParagraphStructureAndNormalizesName()
    {
        var part = QuickPartCommandPlanner.CreateSelection("First\r\nSecond", "  Greeting  ");

        part.Should().NotBeNull();
        part!.Name.Should().Be("Greeting");
        part.Lines.Should().Equal("First", "Second");
    }

    [Fact]
    public void TableEraserPlanner_MapsGridColumnsAcrossExistingSpans()
    {
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0].GridSpan = 2;

        TableEraserCommandPlanner.PlanByGridColumn(table, 0, 1)
            .Should().Be(new TableEraserMergePlan(0, 0, 1));
        TableEraserCommandPlanner.PlanByGridColumn(table, 0, 2)
            .Should().Be(new TableEraserMergePlan(0, 1, 2));
        TableEraserCommandPlanner.PlanByGridColumn(table, 0, 3)
            .Should().BeNull("the last cell has no right border that can be erased");
    }

    [Fact]
    public void QuickPartLibrary_RoundTripsSharedJsonAcrossShellInstances()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freew-quickparts-");
        var path = Path.Combine(temporaryDirectory.Path, "quickparts.json");
        {
            var writer = QuickPartLibrary.LoadFromPath(path);
            writer.Save(new QuickPart("Signature", ["Regards,", "Ada"], "AutoText", "General", "Closing"));

            var reader = QuickPartLibrary.LoadFromPath(path);
            var part = reader.Get("signature");
            part.Should().NotBeNull();
            part!.Lines.Should().Equal("Regards,", "Ada");
            part.Gallery.Should().Be("AutoText");
            part.Description.Should().Be("Closing");

            reader.Remove("SIGNATURE");
            QuickPartLibrary.LoadFromPath(path).IsEmpty.Should().BeTrue();
        }
    }
}
