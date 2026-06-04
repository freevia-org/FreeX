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

    [Theory]
    [InlineData("TableStyleMedium2", WorkbookThemeColorSlot.Accent1)]
    [InlineData("TableStyleMedium3", WorkbookThemeColorSlot.Accent2)]
    [InlineData("TableStyleMedium4", WorkbookThemeColorSlot.Accent3)]
    [InlineData("TableStyleMedium5", WorkbookThemeColorSlot.Accent4)]
    [InlineData("TableStyleMedium6", WorkbookThemeColorSlot.Accent5)]
    [InlineData("TableStyleMedium7", WorkbookThemeColorSlot.Accent6)]
    public void BuiltInMediumAccentStyles_ResolveWorkbookThemeSlots(
        string styleName,
        WorkbookThemeColorSlot expectedSlot)
    {
        var theme = CreateDistinctAccentTheme();

        TableStyleGalleryPlanner.TryGetOption(styleName, theme, out var option)
            .Should()
            .BeTrue();

        option.Banding.HeaderFill.Should().Be(theme.ResolveColor(expectedSlot));
        option.Banding.OddRowFill.Should().Be(theme.ResolveColor(expectedSlot, 0.8));
        option.Banding.EvenRowFill.Should().Be(CellColor.White);
        option.Banding.HeaderFontColor.Should().Be(CellColor.White);
    }

    [Theory]
    [InlineData("TableStyleLight16", WorkbookThemeColorSlot.Accent1)]
    [InlineData("TableStyleLight17", WorkbookThemeColorSlot.Accent2)]
    [InlineData("TableStyleLight18", WorkbookThemeColorSlot.Accent3)]
    [InlineData("TableStyleLight19", WorkbookThemeColorSlot.Accent4)]
    [InlineData("TableStyleLight20", WorkbookThemeColorSlot.Accent5)]
    [InlineData("tablestylelight21", WorkbookThemeColorSlot.Accent6)]
    public void BuiltInLightAccentStyles_ResolveWorkbookThemeSlots(
        string styleName,
        WorkbookThemeColorSlot expectedSlot)
    {
        var theme = CreateDistinctAccentTheme();

        TableStyleGalleryPlanner.TryGetOption(styleName, theme, out var option)
            .Should()
            .BeTrue();

        option.Banding.HeaderFill.Should().Be(theme.ResolveColor(expectedSlot, 0.8));
        option.Banding.OddRowFill.Should().Be(theme.ResolveColor(expectedSlot, 0.95));
        option.Banding.EvenRowFill.Should().Be(CellColor.White);
        option.Banding.HeaderFontColor.Should().Be(CellColor.Black);
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

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.StructuredTables.Should().ContainSingle()
            .Which.StyleName.Should().Be("TableStyleMedium2");
        StyleAt(workbook, sheet, 1, 1).FillColor.Should().Be(option.Banding.HeaderFill);
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(option.Banding.EvenRowFill);
        StyleAt(workbook, sheet, 3, 1).FillColor.Should().Be(option.Banding.OddRowFill);
    }

    [Fact]
    public void BuiltInLightStyle_UsesWorkbookThemeBandingForMaterializedTable()
    {
        var theme = CreateDistinctAccentTheme();
        var workbook = new Workbook("TableStyleLightThemeRenderTest") { Theme = theme };
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

        TableStyleGalleryPlanner.TryGetOption("TableStyleLight16", theme, out var option)
            .Should()
            .BeTrue();

        var command = new CreateStyledStructuredTableCommand(
            sheet.Id,
            range,
            option.StyleName,
            firstRowHasHeaders: true,
            option.Banding);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.StructuredTables.Should().ContainSingle()
            .Which.StyleName.Should().Be("TableStyleLight16");
        StyleAt(workbook, sheet, 1, 1).FillColor.Should().Be(theme.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.8));
        StyleAt(workbook, sheet, 1, 1).FontColor.Should().Be(CellColor.Black);
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(CellColor.White);
        StyleAt(workbook, sheet, 3, 1).FillColor.Should().Be(theme.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.95));
    }

    private static WorkbookTheme CreateDistinctAccentTheme() =>
        WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 80, 120))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(120, 40, 20))
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(25, 130, 60))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(40, 90, 180))
            .WithColor(WorkbookThemeColorSlot.Accent5, new CellColor(150, 45, 140))
            .WithColor(WorkbookThemeColorSlot.Accent6, new CellColor(80, 145, 35));

    private static CellStyle StyleAt(Workbook workbook, Sheet sheet, uint row, uint col) =>
        workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, row, col))!.StyleId);
}
