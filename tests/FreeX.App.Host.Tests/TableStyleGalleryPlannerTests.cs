using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class TableStyleGalleryPlannerTests
{
    [Fact]
    public void GetOptions_ExposesLightMediumAndDarkExcelStyleGallery()
    {
        var options = TableStyleGalleryPlanner.GetOptions();

        options.Select(option => option.StyleName)
            .Should()
            .ContainInOrder(
                "TableStyleLight1",
                "TableStyleLight21",
                "TableStyleMedium1",
                "TableStyleMedium28",
                "TableStyleDark1",
                "TableStyleDark11");
        options.Should().HaveCount(60);
        options.Select(option => option.StyleName).Should().OnlyHaveUniqueItems();
        options.Should().OnlyContain(option => option.Banding.HeaderFill != default);
    }

    [Fact]
    public void GetOptions_GroupsBuiltInStylesLikeExcelGallery()
    {
        var options = TableStyleGalleryPlanner.GetOptions();

        options.Take(21).Should().OnlyContain(option => option.Label.StartsWith("Light ", StringComparison.Ordinal));
        options.Skip(21).Take(28).Should().OnlyContain(option => option.Label.StartsWith("Medium ", StringComparison.Ordinal));
        options.Skip(49).Should().OnlyContain(option => option.Label.StartsWith("Dark ", StringComparison.Ordinal));
    }

    [Fact]
    public void MainWindow_PopulatesFormatAsTableMenuFromGalleryPlanner()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

        xaml.Should().Contain("x:Name=\"FormatTableGalleryMenu\"");
        xaml.Should().NotContain("Header=\"Light 1\"  Tag=\"0\"");
        source.Should().Contain("PopulateFormatTableGalleryMenu()");
        source.Should().Contain("TableStyleGalleryPlanner.GetOptions(_workbook.Theme)");
    }

    [Fact]
    public void GetOption_ClampsOutOfRangeIndexes()
    {
        TableStyleGalleryPlanner.GetOption(-10).StyleName.Should().Be("TableStyleLight1");
        TableStyleGalleryPlanner.GetOption(999).StyleName.Should().Be("TableStyleDark11");
    }

    [Fact]
    public void TryGetOption_ResolvesStyleNamesCaseInsensitively()
    {
        var found = TableStyleGalleryPlanner.TryGetOption("tablestylemedium2", out var option);

        found.Should().BeTrue();
        option.StyleName.Should().Be("TableStyleMedium2");
        TableStyleGalleryPlanner.TryGetOption("CustomTableStyle", out _).Should().BeFalse();
    }

    [Fact]
    public void BuiltInMediumStyle_UsesWorkbookThemeBandingForSwatchAndMaterialization()
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 80, 120));
        var workbook = new Workbook("TableStyleThemeRenderTest") { Theme = theme };
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        TableStyleGalleryPlanner.TryGetOption("TableStyleMedium2", theme, out var option)
            .Should()
            .BeTrue();
        option.Banding.HeaderFill.Should().Be(theme.ResolveColor(WorkbookThemeColorSlot.Accent1));
        option.Banding.OddRowFill.Should().Be(theme.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.8));

        var command = new CreateStyledStructuredTableCommand(
            sheet.Id,
            range,
            option.StyleName,
            firstRowHasHeaders: true,
            option.Banding);

        command.Apply(new SimpleCtx(workbook)).Success.Should().BeTrue();

        sheet.StructuredTables.Should().ContainSingle()
            .Which.StyleName.Should().Be("TableStyleMedium2");
        StyleAt(workbook, sheet, 1, 1).FillColor.Should().Be(option.Banding.HeaderFill);
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(option.Banding.EvenRowFill);
        StyleAt(workbook, sheet, 3, 1).FillColor.Should().Be(option.Banding.OddRowFill);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }

    private static CellStyle StyleAt(Workbook workbook, Sheet sheet, uint row, uint col) =>
        workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, row, col))!.StyleId);
}
