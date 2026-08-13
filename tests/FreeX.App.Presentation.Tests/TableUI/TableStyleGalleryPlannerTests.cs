using FluentAssertions;
using FreeX.App.Presentation.TableUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.TableUI;

public sealed class TableStyleGalleryPlannerTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void GetOptions_ExposesLightMediumAndDarkExcelGalleryInOrder()
    {
        var options = TableStyleGalleryPlanner.GetOptions();

        options.Should().HaveCount(60);
        options.Select(option => option.StyleName)
            .Should()
            .ContainInOrder(
                "TableStyleLight1",
                "TableStyleLight21",
                "TableStyleMedium1",
                "TableStyleMedium28",
                "TableStyleDark1",
                "TableStyleDark11");
        options.Select(option => option.StyleName).Should().OnlyHaveUniqueItems();

        options.Take(21).Should().OnlyContain(option => option.Label.StartsWith("Light ", StringComparison.Ordinal));
        options.Skip(21).Take(28).Should().OnlyContain(option => option.Label.StartsWith("Medium ", StringComparison.Ordinal));
        options.Skip(49).Should().OnlyContain(option => option.Label.StartsWith("Dark ", StringComparison.Ordinal));
    }

    [Fact]
    public void GetSurface_ExposesSharedDescriptorGroupsItemsAndKeyTipsInOrder()
    {
        var surface = TableStyleGalleryPlanner.GetSurface();

        surface.Groups.Select(group => (group.Family, group.Items.Count))
            .Should()
            .Equal(("Light", 21), ("Medium", 28), ("Dark", 11));
        surface.Items.Should().HaveCount(60);
        surface.Groups.SelectMany(group => group.Items).Should().Equal(surface.Items);

        surface.Items[0].Should().BeEquivalentTo(new
        {
            Index = 0,
            Family = "Light",
            FamilyIndex = 1,
            Label = "Light 1",
            KeyTip = "L1",
            StyleName = "TableStyleLight1"
        });
        surface.Items[21].Should().BeEquivalentTo(new
        {
            Index = 21,
            Family = "Medium",
            FamilyIndex = 1,
            Label = "Medium 1",
            KeyTip = "M1",
            StyleName = "TableStyleMedium1"
        });
        surface.Items[59].Should().BeEquivalentTo(new
        {
            Index = 59,
            Family = "Dark",
            FamilyIndex = 11,
            Label = "Dark 11",
            KeyTip = "D11",
            StyleName = "TableStyleDark11"
        });

        surface.Items.Select(item => item.Option)
            .Should()
            .Equal(TableStyleGalleryPlanner.GetOptions());
    }

    [Fact]
    public void GetOption_ClampsOutOfRangeIndexes()
    {
        TableStyleGalleryPlanner.GetOption(-10).StyleName.Should().Be("TableStyleLight1");
        TableStyleGalleryPlanner.GetOption(999).StyleName.Should().Be("TableStyleDark11");
    }

    [Fact]
    public void NormalizeStyleName_DefaultsBlankToMedium2()
    {
        TableStyleGalleryPlanner.NormalizeStyleName(null).Should().Be("TableStyleMedium2");
        TableStyleGalleryPlanner.NormalizeStyleName("  ").Should().Be("TableStyleMedium2");
        TableStyleGalleryPlanner.NormalizeStyleName(" TableStyleLight5 ").Should().Be("TableStyleLight5");
    }

    [Fact]
    public void FindStyleIndex_LocatesCurrentStyleCaseInsensitivelyAndDefaultsToFirst()
    {
        var options = TableStyleGalleryPlanner.GetOptions();
        var medium2 = TableStyleGalleryPlanner.FindStyleIndex(options, "tablestylemedium2");
        options[medium2].StyleName.Should().Be("TableStyleMedium2");

        TableStyleGalleryPlanner.FindStyleIndex(options, "CustomStyle").Should().Be(0);
        TableStyleGalleryPlanner.FindStyleIndex(options, null).Should().Be(0);
    }

    [Fact]
    public void SurfaceSelectionHelpers_LocateAndClampSharedItems()
    {
        var surface = TableStyleGalleryPlanner.GetSurface();

        var medium2 = TableStyleGalleryPlanner.FindSurfaceItemIndex(surface, "tablestylemedium2");
        surface.Items[medium2].StyleName.Should().Be("TableStyleMedium2");
        TableStyleGalleryPlanner.FindSurfaceItemIndex(surface, "CustomStyle").Should().Be(0);
        TableStyleGalleryPlanner.FindSurfaceItemIndex(surface, null).Should().Be(0);

        TableStyleGalleryPlanner.GetSurfaceItem(surface, -1).StyleName.Should().Be("TableStyleLight1");
        TableStyleGalleryPlanner.GetSurfaceItem(surface, 999).StyleName.Should().Be("TableStyleDark11");
    }

    [Fact]
    public void TryGetOption_ResolvesBuiltInStyleNamesAndRejectsCustom()
    {
        TableStyleGalleryPlanner.TryGetOption("tablestylemedium2", out var option).Should().BeTrue();
        option.StyleName.Should().Be("TableStyleMedium2");

        TableStyleGalleryPlanner.TryGetOption("CustomTableStyle", out _).Should().BeFalse();
        TableStyleGalleryPlanner.TryGetOption("  ", out _).Should().BeFalse();
    }

    [Fact]
    public void GetOptions_BandingHeaderContrastsWithFill()
    {
        var options = TableStyleGalleryPlanner.GetOptions();

        // Each gallery option carries a banding whose header font is either white or black (Excel's Light 8-14
        // use a genuine black header paired with white text), so the header always reads with contrast.
        options.Should().OnlyContain(option =>
            option.Banding.HeaderFontColor == CellColor.White || option.Banding.HeaderFontColor == CellColor.Black);
        options.Should().Contain(option => option.Banding.HeaderFill == CellColor.Black,
            "Excel's Light 8-14 table styles have a solid black header");
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

    [Fact]
    public void HostTableStyleGalleryFacade_IsRemoved()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "TableStyleGalleryPlanner.cs"))
            .Should()
            .BeFalse("WPF host should consume the shared table-style gallery planner directly");
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
