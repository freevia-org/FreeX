using FluentAssertions;
using FreeX.App.Presentation.SheetUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.SheetUI;

public sealed class SheetTabListPlannerTests
{
    [Fact]
    public void Build_AvoidsLinqMaterializationOnSheetTabRefreshPath()
    {
        var source = File.ReadAllText(TestWorkspaceFileLocator.Find(
            "src",
            "FreeX.App.Presentation",
            "SheetUI",
            "SheetTabListPlanner.cs"));
        var buildStart = source.IndexOf("public static SheetTabListPlan Build(", StringComparison.Ordinal);
        var groupedStart = source.IndexOf("public static bool IsWorkbookGrouped(", StringComparison.Ordinal);
        var buildSource = source[buildStart..groupedStart];
        var adjacentStart = source.IndexOf("public static SheetId? AdjacentVisibleSheet(", StringComparison.Ordinal);
        var groupStart = source.IndexOf("public static SheetKeyboardGroupSelectionPlan? SelectAdjacentVisibleSheetGroup(", StringComparison.Ordinal);
        var adjacentSource = source[adjacentStart..groupStart];

        source.Should().Contain("public sealed record SheetTabListEntry");
        source.Should().NotContain("SheetTabViewModel");
        buildSource.Should().NotContain(".Where(");
        buildSource.Should().NotContain(".Select(");
        buildSource.Should().NotContain(".ToList(");
        adjacentSource.Should().NotContain(".Where(");
        adjacentSource.Should().NotContain(".ToList(");
    }

    [Fact]
    public void Build_EnsuresAtLeastOneVisibleSheetAndActiveVisibleSheet()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("Hidden1");
        var second = workbook.AddSheet("Hidden2");
        first.IsHidden = true;
        second.IsHidden = true;
        var grouped = new HashSet<SheetId>();

        var plan = SheetTabListPlanner.Build(workbook, second.Id, grouped);

        first.IsHidden.Should().BeFalse();
        plan.CurrentSheetId.Should().Be(first.Id);
        plan.Tabs.Should().ContainSingle().Which.Should().Match<SheetTabListEntry>(tab =>
            tab.Id == first.Id && tab.IsActive && tab.IsGrouped);
        grouped.Should().Equal(first.Id);
    }

    [Fact]
    public void Build_RecoversMissingCurrentSheetToFirstVisibleSheet()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        workbook.AddSheet("Second");
        var grouped = new HashSet<SheetId>();

        var plan = SheetTabListPlanner.Build(workbook, SheetId.New(), grouped);

        plan.CurrentSheetId.Should().Be(first.Id);
        plan.Tabs.Should().ContainSingle(tab => tab.Id == first.Id && tab.IsActive && tab.IsGrouped);
        grouped.Should().Equal(first.Id);
    }

    [Fact]
    public void Build_RemovesHiddenSheetsFromGroupedSet()
    {
        var workbook = new Workbook("Book");
        var visible = workbook.AddSheet("Visible");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var grouped = new HashSet<SheetId> { visible.Id, hidden.Id };

        var plan = SheetTabListPlanner.Build(workbook, visible.Id, grouped);

        grouped.Should().Equal(visible.Id);
        plan.Tabs.Should().ContainSingle().Which.IsGrouped.Should().BeTrue();
    }

    [Fact]
    public void Build_CarriesSheetProtectionStateIntoTabs()
    {
        var workbook = new Workbook("Book");
        var visible = workbook.AddSheet("Visible");
        visible.IsProtected = true;
        var grouped = new HashSet<SheetId>();

        var plan = SheetTabListPlanner.Build(workbook, visible.Id, grouped);

        plan.Tabs.Should().ContainSingle().Which.IsProtected.Should().BeTrue();
    }

    [Fact]
    public void GenerateUniqueSheetName_SkipsExistingWorkbookNames()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        SheetTabListPlanner.GenerateUniqueSheetName(workbook).Should().Be("Sheet3");
    }

    [Fact]
    public void GenerateUniqueSheetName_UsesLowestAvailableExcelDefaultName()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("UX Overview");
        workbook.AddSheet("Sheet2");
        workbook.AddSheet("Sheet8");

        SheetTabListPlanner.GenerateUniqueSheetName(workbook).Should().Be("Sheet1");
    }

    [Fact]
    public void AdjacentVisibleSheet_ClampsToVisibleSheets()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var hidden = workbook.AddSheet("Hidden");
        var second = workbook.AddSheet("Second");
        hidden.IsHidden = true;

        SheetTabListPlanner.AdjacentVisibleSheet(workbook, first.Id, 1).Should().Be(second.Id);
        SheetTabListPlanner.AdjacentVisibleSheet(workbook, second.Id, 1).Should().Be(second.Id);
        SheetTabListPlanner.AdjacentVisibleSheet(workbook, second.Id, -1).Should().Be(first.Id);
    }

    [Fact]
    public void AdjacentVisibleSheet_TreatsMissingCurrentAsFirstVisibleBeforeApplyingDirection()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");

        SheetTabListPlanner.AdjacentVisibleSheet(workbook, SheetId.New(), 1).Should().Be(second.Id);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void AdjacentVisibleSheet_RecoversMissingCurrentToFirstVisibleWhenNotMovingForward(int direction)
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        workbook.AddSheet("Second");

        SheetTabListPlanner.AdjacentVisibleSheet(workbook, SheetId.New(), direction).Should().Be(first.Id);
    }

    [Fact]
    public void SelectAdjacentVisibleSheetGroup_ExtendsFromAnchorAcrossVisibleSheets()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var hidden = workbook.AddSheet("Hidden");
        var third = workbook.AddSheet("Third");
        hidden.IsHidden = true;

        var plan = SheetTabListPlanner.SelectAdjacentVisibleSheetGroup(
            workbook,
            first.Id,
            anchorSheetId: null,
            direction: 1);

        plan.Should().NotBeNull();
        plan!.CurrentSheetId.Should().Be(second.Id);
        plan.AnchorSheetId.Should().Be(first.Id);
        plan.GroupedSheetIds.Should().Equal(first.Id, second.Id);

        var extended = SheetTabListPlanner.SelectAdjacentVisibleSheetGroup(
            workbook,
            plan.CurrentSheetId,
            plan.AnchorSheetId,
            direction: 1);

        extended.Should().NotBeNull();
        extended!.CurrentSheetId.Should().Be(third.Id);
        extended.AnchorSheetId.Should().Be(first.Id);
        extended.GroupedSheetIds.Should().Equal(first.Id, second.Id, third.Id);
    }

    [Fact]
    public void SelectAdjacentVisibleSheetGroup_ClampsAtWorkbookEdges()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        workbook.AddSheet("Second");

        var plan = SheetTabListPlanner.SelectAdjacentVisibleSheetGroup(
            workbook,
            first.Id,
            anchorSheetId: null,
            direction: -1);

        plan.Should().NotBeNull();
        plan!.CurrentSheetId.Should().Be(first.Id);
        plan.GroupedSheetIds.Should().Equal(first.Id);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public void SelectAdjacentVisibleSheetGroup_TreatsPositiveDirectionAsSingleStep(int direction)
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var third = workbook.AddSheet("Third");

        var plan = SheetTabListPlanner.SelectAdjacentVisibleSheetGroup(
            workbook,
            first.Id,
            anchorSheetId: null,
            direction);

        plan.Should().NotBeNull();
        plan!.CurrentSheetId.Should().Be(second.Id);
        plan.GroupedSheetIds.Should().Equal(first.Id, second.Id);
        plan.GroupedSheetIds.Should().NotContain(third.Id);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-10)]
    public void SelectAdjacentVisibleSheetGroup_TreatsNegativeDirectionAsSingleStep(int direction)
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var third = workbook.AddSheet("Third");

        var plan = SheetTabListPlanner.SelectAdjacentVisibleSheetGroup(
            workbook,
            third.Id,
            anchorSheetId: null,
            direction);

        plan.Should().NotBeNull();
        plan!.CurrentSheetId.Should().Be(second.Id);
        plan.GroupedSheetIds.Should().Equal(second.Id, third.Id);
        plan.GroupedSheetIds.Should().NotContain(first.Id);
    }

    [Fact]
    public void SelectAdjacentVisibleSheetGroup_ZeroDirectionKeepsCurrentSheetOnly()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        workbook.AddSheet("Third");

        var plan = SheetTabListPlanner.SelectAdjacentVisibleSheetGroup(
            workbook,
            second.Id,
            anchorSheetId: null,
            direction: 0);

        plan.Should().NotBeNull();
        plan!.CurrentSheetId.Should().Be(second.Id);
        plan.GroupedSheetIds.Should().Equal(second.Id);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void SelectAdjacentVisibleSheetGroup_ResetsAnchorWhenCurrentSheetIsMissing(int direction)
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var third = workbook.AddSheet("Third");

        var plan = SheetTabListPlanner.SelectAdjacentVisibleSheetGroup(
            workbook,
            SheetId.New(),
            third.Id,
            direction);

        plan.Should().NotBeNull();
        plan!.AnchorSheetId.Should().Be(first.Id);
        if (direction > 0)
        {
            plan.CurrentSheetId.Should().Be(second.Id);
            plan.GroupedSheetIds.Should().Equal(first.Id, second.Id);
        }
        else
        {
            plan.CurrentSheetId.Should().Be(first.Id);
            plan.GroupedSheetIds.Should().Equal(first.Id);
        }
    }

    [Fact]
    public void Build_HandlesLargeSheetTabListsWithoutDroppingVisibleTabs()
    {
        var workbook = new Workbook("Book");
        SheetId currentSheetId = default;
        for (var index = 0; index < 2_000; index++)
        {
            var sheet = workbook.AddSheet($"Sheet{index + 1}");
            if (index % 5 == 0)
                sheet.IsHidden = true;
            if (index == 1_501)
                currentSheetId = sheet.Id;
        }

        var grouped = new HashSet<SheetId>();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var plan = SheetTabListPlanner.Build(workbook, currentSheetId, grouped);
            if (plan.CurrentSheetId != currentSheetId || plan.Tabs.Count != 1_600)
                throw new InvalidOperationException("Sheet tab planner returned an unexpected large-workbook plan.");
        }
    }

    [Fact]
    public void Build_ResolvesThemeRelativeTabColorAgainstTheCurrentWorkbookTheme()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Data");
        // Baked-stale RGB plus a live theme link, exactly as XlsxFileAdapter loads <tabColor theme="4"/>.
        sheet.TabColor = new CellColor(1, 2, 3);
        sheet.TabThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1);

        var themeA = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 10, 20));
        var themeB = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(20, 200, 10));

        workbook.Theme = themeA;
        var planA = SheetTabListPlanner.Build(workbook, sheet.Id, []);
        workbook.Theme = themeB;
        var planB = SheetTabListPlanner.Build(workbook, sheet.Id, []);

        planA.Tabs.Should().ContainSingle().Which.TabColor.Should().Be(sheet.ResolveTabColor(themeA));
        planB.Tabs.Should().ContainSingle().Which.TabColor.Should().Be(sheet.ResolveTabColor(themeB));
        planA.Tabs[0].TabColor.Should().NotBe(planB.Tabs[0].TabColor);
        planA.Tabs[0].TabColor.Should().NotBe(new CellColor(1, 2, 3));
    }

    [Fact]
    public void Build_KeepsExplicitRgbTabColorConstantAcrossThemeChanges()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Data");
        sheet.TabColor = new CellColor(0, 112, 192);
        sheet.TabThemeColor.Should().BeNull();

        workbook.Theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 10, 20));
        var planA = SheetTabListPlanner.Build(workbook, sheet.Id, []);
        workbook.Theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(20, 200, 10));
        var planB = SheetTabListPlanner.Build(workbook, sheet.Id, []);

        planA.Tabs.Should().ContainSingle().Which.TabColor.Should().Be(new CellColor(0, 112, 192));
        planB.Tabs.Should().ContainSingle().Which.TabColor.Should().Be(new CellColor(0, 112, 192));
    }

    [Fact]
    public void Build_LeavesTabColorNullWhenSheetHasNeitherRgbNorThemeLink()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Data");

        var plan = SheetTabListPlanner.Build(workbook, sheet.Id, []);

        plan.Tabs.Should().ContainSingle().Which.TabColor.Should().BeNull();
    }
}
