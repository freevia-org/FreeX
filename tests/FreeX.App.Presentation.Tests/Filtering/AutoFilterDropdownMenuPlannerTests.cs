using FluentAssertions;

using Free.Shared.Ribbon;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

public sealed class AutoFilterDropdownMenuPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();
    private static readonly TestTextProvider Text = new();

    [Fact]
    public void TryPlan_ReturnsCurrentRegionAndColumnOffsetForHeaderCell()
    {
        var region = new GridRange(
            new CellAddress(SheetId, 2, 3),
            new CellAddress(SheetId, 10, 6));
        var activeCell = new CellAddress(SheetId, 2, 5);

        var planned = AutoFilterDropdownMenuPlanner.TryPlan(region, activeCell, out var plan);

        planned.Should().BeTrue();
        plan.Range.Should().Be(region);
        plan.FilterColumnOffset.Should().Be(2);
    }

    [Fact]
    public void CreateMenuPlan_BuildsExcelStyleTextFilterMenuSections()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(SheetId, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(SheetId, 3, 1), new TextValue("Banana"));

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(SheetId, 1, 1),
                new CellAddress(SheetId, 3, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(sheet, plan, Text, "(Blanks)");

        menu.HeaderText.Should().Be("Fruit");
        menu.FilterKind.Should().Be(AutoFilterMenuFilterKind.Text);
        menu.Entries.Select(entry => entry.Header).Should().ContainInOrder(
            "Sort A to Z",
            "Sort Z to A",
            "Clear Filter from Fruit",
            "Text Filters",
            "Search",
            "(Select All)",
            "Apple",
            "Banana");
        menu.Sections.Select(section => section.Kind).Should().Equal(
            AutoFilterMenuSectionKind.Sort,
            AutoFilterMenuSectionKind.FilterCommands,
            AutoFilterMenuSectionKind.Search,
            AutoFilterMenuSectionKind.Checklist);
    }

    [Fact]
    public void CreateMenuPlan_CarriesSharedRowPresentationForIconsFocusAndContinuations()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Region"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("West"));
        var fillStyle = CellStyle.Default.Clone();
        fillStyle.FillColor = new CellColor(0x21, 0x73, 0x46);
        sheet.GetCell(2, 1)!.StyleId = workbook.RegisterStyle(fillStyle);

        var plan = new AutoFilterDropdownPlan(
            new GridRange(Address(sheet, 1, 1), Address(sheet, 2, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        menu.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.SortAscending)
            .Presentation.Should().Be(new AutoFilterMenuEntryPresentation(
                RibbonCommandIconKind.SortAscending,
                AutoFilterMenuEntryFocusRole.Command));
        menu.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.SortDescending)
            .Presentation.IconKind.Should().Be(RibbonCommandIconKind.SortDescending);
        menu.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.ClearFilter)
            .Presentation.IconKind.Should().Be(RibbonCommandIconKind.Clear);
        menu.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.FilterByColor)
            .Presentation.Should().Be(new AutoFilterMenuEntryPresentation(
                RibbonCommandIconKind.Color,
                AutoFilterMenuEntryFocusRole.Command,
                ShowsContinuation: true));
        menu.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.FilterFamily)
            .Presentation.Should().Be(new AutoFilterMenuEntryPresentation(
                RibbonCommandIconKind.Filter,
                AutoFilterMenuEntryFocusRole.Submenu,
                ShowsContinuation: true));
        menu.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.Search)
            .Presentation.Should().Be(new AutoFilterMenuEntryPresentation(
                RibbonCommandIconKind.Search,
                AutoFilterMenuEntryFocusRole.SearchBox,
                ParticipatesInSearch: true));
        menu.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.SelectAll)
            .Presentation.FocusRole.Should().Be(AutoFilterMenuEntryFocusRole.TriStateSelectAll);
        menu.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .Presentation.Should().Be(new AutoFilterMenuEntryPresentation(
                RibbonCommandIconKind.CheckBox,
                AutoFilterMenuEntryFocusRole.ChecklistItem,
                ParticipatesInSearch: true));
    }

    [Theory]
    [InlineData("number", AutoFilterMenuFilterKind.Number, "Sort Smallest to Largest", "Sort Largest to Smallest")]
    [InlineData("date", AutoFilterMenuFilterKind.Date, "Sort Oldest to Newest", "Sort Newest to Oldest")]
    public void CreateMenuPlan_UsesExcelSortLabelsForDetectedValueType(
        string valueKind,
        AutoFilterMenuFilterKind expectedFilterKind,
        string expectedAscending,
        string expectedDescending)
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Value"));
        if (valueKind == "number")
        {
            sheet.SetCell(new CellAddress(SheetId, 2, 1), new NumberValue(42));
            sheet.SetCell(new CellAddress(SheetId, 3, 1), new NumberValue(7));
        }
        else
        {
            sheet.SetCell(new CellAddress(SheetId, 2, 1), new DateTimeValue(new DateTime(2026, 5, 1).ToOADate()));
            sheet.SetCell(new CellAddress(SheetId, 3, 1), new DateTimeValue(new DateTime(2026, 6, 1).ToOADate()));
        }

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(SheetId, 1, 1),
                new CellAddress(SheetId, 3, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(sheet, plan, Text, "(Blanks)");

        menu.FilterKind.Should().Be(expectedFilterKind);
        menu.Entries.Where(entry => entry.Kind is AutoFilterMenuEntryKind.SortAscending or AutoFilterMenuEntryKind.SortDescending)
            .Select(entry => entry.Header)
            .Should()
            .Equal(expectedAscending, expectedDescending);
    }

    [Fact]
    public void SharedCriteriaCatalog_PreservesSuggestionsAndDialogPrefixes()
    {
        var textCriteria = AutoFilterMenuCatalog.GetCriteriaDescriptors(AutoFilterMenuFilterKind.Text);

        textCriteria.Select(descriptor => descriptor.SuggestionPrefix ?? descriptor.CriteriaPrefix)
            .Should()
            .Equal("equals:", "text<>", "contains:", "notcontains:", "begins:", "ends:", "blank", "nonblank");
        textCriteria.Select(descriptor => descriptor.CriteriaPrefix)
            .Should()
            .Equal("text=", "text<>", "contains:", "notcontains:", "begins:", "ends:", "blank", "nonblank");

        AutoFilterMenuCatalog.IsBetweenCriteriaPrefix("between:").Should().BeTrue();
        AutoFilterMenuCatalog.IsBetweenCriteriaPrefix("datebetween:").Should().BeTrue();
        AutoFilterMenuCatalog.IsTopBottomCriteriaPrefix("toppercent:").Should().BeTrue();
        AutoFilterMenuCatalog.IsTopBottomCriteriaPrefix("blank").Should().BeFalse();
    }

    [Fact]
    public void CreateMenuPlan_IncludesColorOptions_WhenWorkbookStylesHaveColors()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Banana"));
        var fillStyle = CellStyle.Default.Clone();
        fillStyle.FillColor = new CellColor(0x21, 0x73, 0x46);
        var fillStyleId = workbook.RegisterStyle(fillStyle);
        sheet.GetCell(2, 1)!.StyleId = fillStyleId;

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        menu.ColorOptions.Should().Contain(option =>
            option.Kind == AutoFilterColorFilterKind.CellFillColor &&
            option.Label == "#217346");
        menu.ColorOptions.Should().Contain(option => option.Kind == AutoFilterColorFilterKind.NoFill);
        menu.Entries.Should().Contain(entry => entry.Kind == AutoFilterMenuEntryKind.FilterByColor);
    }

    [Fact]
    public void CreateMenuPlan_ColorOptions_ResolveConditionalFormatDrivenFillColor()
    {
        // filter-by-color-cf: a CF rule colors row 3 (value > 100) red purely via conditional
        // formatting -- no manual/static fill is set on that cell. The offered color list must
        // include that CF-driven red as a Cell Fill Color option, and that CF-red row must NOT
        // count towards "No Fill" (only the genuinely uncolored row 2 does).
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(50));  // no CF match, no fill
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(200)); // CF match -> red fill

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        menu.ColorOptions.Should().Contain(option =>
            option.Kind == AutoFilterColorFilterKind.CellFillColor &&
            option.Label == "#FF0000");
        // "No Fill" is only offered/matched for the genuinely uncolored row (50); the CF-red row
        // must not be lumped into it.
        menu.ColorOptions.Should().Contain(option => option.Kind == AutoFilterColorFilterKind.NoFill);
    }

    [Fact]
    public void SpillOverlayRootF5_ColorOptions_ResolveConditionalFormatDrivenFillColor_OnSpillMember()
    {
        // spill-overlay-root F5: same CF-driven-fill scenario as
        // CreateMenuPlan_ColorOptions_ResolveConditionalFormatDrivenFillColor above, but this time
        // the row that satisfies the rule is a non-anchor dynamic-array spill member -- Sheet.GetCell
        // returns null for it (its live value lives only in the sheet's spill overlay), so before the
        // fix the color scan always judged it against BlankValue.Instance and the swatch was silently
        // dropped. Row 2 (the real anchor Cell, value 50) does NOT match; row 3 (spill member, value
        // 200) DOES match and must still surface its CF-driven red fill here.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));

        var anchor = new CellAddress(sheet.Id, 2, 1);
        sheet.SetFormula(anchor, "{50;200}");
        sheet.GetCell(anchor)!.Value = new NumberValue(50); // anchor: real Cell, no CF match
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[2, 1]
        {
            { new NumberValue(50) },  // row 0 (anchor slot) -- SetSpillRange ignores this element
            { new NumberValue(200) }, // row 3: spill member, no stored Cell -- CF match -> red fill
        }));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        menu.ColorOptions.Should().Contain(option =>
            option.Kind == AutoFilterColorFilterKind.CellFillColor &&
            option.Label == "#FF0000");
    }

    [Fact]
    public void SpillOverlayRootF5_ColorOptions_NonMatchingSpillMember_StaysNoFill()
    {
        // Sibling/no-regression for F5: a spill member whose real value does NOT satisfy the CF
        // rule must still be treated as uncolored (no phantom fill color offered) -- guarding
        // against an over-broad fix that treats every spill member as colored regardless of its
        // actual value. This behavior is unchanged by the fix (true both before and after).
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));

        var anchor = new CellAddress(sheet.Id, 2, 1);
        sheet.SetFormula(anchor, "{5;10}");
        sheet.GetCell(anchor)!.Value = new NumberValue(5); // anchor: real Cell, no CF match
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[2, 1]
        {
            { new NumberValue(5) },
            { new NumberValue(10) }, // row 3: spill member, no CF match either
        }));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        menu.ColorOptions.Should().NotContain(option => option.Kind == AutoFilterColorFilterKind.CellFillColor);
    }

    [Fact]
    public void SpillOverlayRootF6_ColorOptions_ResolveConditionalFormatDrivenFontColor_OnSpillMember()
    {
        // spill-overlay-root F6: same gap as F5 above, but for the font-color swatch pass -- a CF
        // rule that sets DxfFontColor and matches only a non-anchor spill member's real value must
        // still surface that font color here.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));

        var anchor = new CellAddress(sheet.Id, 2, 1);
        sheet.SetFormula(anchor, "{50;200}");
        sheet.GetCell(anchor)!.Value = new NumberValue(50); // anchor: real Cell, no CF match
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[2, 1]
        {
            { new NumberValue(50) },
            { new NumberValue(200) }, // row 3: spill member, no stored Cell -- CF match -> blue font
        }));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { DxfFontColor = new CellColor(0, 0, 255) }
        });

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        menu.ColorOptions.Should().Contain(option =>
            option.Kind == AutoFilterColorFilterKind.FontColor &&
            option.Label == "#0000FF");
    }

    [Fact]
    public void SpillOverlayRootF6_ColorOptions_NonMatchingSpillMember_StaysDefaultFontColor()
    {
        // Sibling/no-regression for F6: a spill member whose real value does NOT satisfy the
        // font-color CF rule must not contribute a phantom font-color swatch. Unchanged by the fix.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));

        var anchor = new CellAddress(sheet.Id, 2, 1);
        sheet.SetFormula(anchor, "{5;10}");
        sheet.GetCell(anchor)!.Value = new NumberValue(5); // anchor: real Cell, no CF match
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[2, 1]
        {
            { new NumberValue(5) },
            { new NumberValue(10) }, // row 3: spill member, no CF match either
        }));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { DxfFontColor = new CellColor(0, 0, 255) }
        });

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, Text, "(Blanks)");

        menu.ColorOptions.Should().NotContain(option => option.Kind == AutoFilterColorFilterKind.FontColor);
    }

    [Fact]
    public void CreateMenuPlan_ReflectsFilteredRowsInChecklistAndSelectAllState()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(SheetId, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(SheetId, 3, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(SheetId, 4, 1), new TextValue("Cherry"));
        sheet.FilterHiddenRows.Add(3);
        // This column's OWN persisted value-filter selection is what the checklist reflects
        // (R45-commands-autofilter-topbottom-3-1) -- register it alongside the raw hidden-row
        // flag so this fixture exercises the same state a real FilterCommand.Apply would leave.
        sheet.ActiveValueFilterColumns[1] = ["Apple", "Cherry"];

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(SheetId, 1, 1),
                new CellAddress(SheetId, 4, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(sheet, plan, Text, "(Blanks)");

        menu.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.SelectAll)
            .IsChecked.Should().BeNull();
        menu.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .ToDictionary(entry => entry.Value, entry => entry.IsChecked)
            .Should()
            .BeEquivalentTo(new Dictionary<string, bool?>
            {
                ["Apple"] = true,
                ["Banana"] = false,
                ["Cherry"] = true
            });
    }

    [Fact]
    public void R108_ChecklistItem_ReflectsActiveValueFilter_CaseInsensitively()
    {
        // R108: the checklist's own distinct-value dedup (AutoFilterChecklistPlanner.CreateItems)
        // is case-insensitive, and the live filter match (FilterCommand's
        // FilterAllowedValueMatcher) is also case-insensitive -- so when the ONLY row supplying a
        // value shows it as "apple" but the persisted ActiveValueFilterColumns criterion recorded
        // "Apple" (e.g. because a differently-cased row supplied the casing when the filter was
        // originally applied, and that row was since edited/deleted/reordered), the checklist must
        // still show that entry as checked: the live filter still allows it.
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(SheetId, 2, 1), new TextValue("apple"));
        sheet.SetCell(new CellAddress(SheetId, 3, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(SheetId, 4, 1), new TextValue("Cherry"));
        sheet.ActiveValueFilterColumns[1] = ["Apple", "Cherry"];

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(SheetId, 1, 1),
                new CellAddress(SheetId, 4, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(sheet, plan, Text, "(Blanks)");

        menu.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .ToDictionary(entry => entry.Value, entry => entry.IsChecked)
            .Should()
            .BeEquivalentTo(new Dictionary<string, bool?>
            {
                ["apple"] = true,
                ["Banana"] = false,
                ["Cherry"] = true
            });
    }

    [Fact]
    public void R108_ChecklistItem_ReflectsColumnFilterOwnedRows_CaseInsensitively()
    {
        // R108 sibling: CollectValuesNotOwnedHidden (the ColumnFilterOwnedRows branch, e.g. for a
        // Top-10/condition/color filter) must also compare case-insensitively. Row 2 ("APPLE") is
        // the owned-hidden row supplying the checklist item's displayed casing (dedup runs across
        // ALL rows, hidden or not, and keeps the first-seen casing), but row 3 ("apple", a
        // differently-cased spelling of the same value) is still visible -- so that value group is
        // NOT actually filtered out and the checklist entry must stay checked.
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(SheetId, 2, 1), new TextValue("APPLE"));
        sheet.SetCell(new CellAddress(SheetId, 3, 1), new TextValue("apple"));
        sheet.SetCell(new CellAddress(SheetId, 4, 1), new TextValue("Grape"));
        sheet.ColumnFilterOwnedRows[1] = [2];

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(SheetId, 1, 1),
                new CellAddress(SheetId, 4, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(sheet, plan, Text, "(Blanks)");

        menu.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .ToDictionary(entry => entry.Value, entry => entry.IsChecked)
            .Should()
            .BeEquivalentTo(new Dictionary<string, bool?>
            {
                ["APPLE"] = true,
                ["Grape"] = true
            });
    }

    [Fact]
    public void R108_ChecklistItem_ColumnFilterOwnedRows_StillUnchecksTrulyHiddenValue()
    {
        // No-regression sibling: when the owned-hidden row's value has NO surviving visible
        // spelling anywhere in the column (case-insensitively or otherwise), the checklist entry
        // must still show unchecked -- the case-insensitive comparer must not turn every hidden
        // value into a false-checked entry.
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(SheetId, 2, 1), new TextValue("Grape"));
        sheet.SetCell(new CellAddress(SheetId, 3, 1), new TextValue("Banana"));
        sheet.ColumnFilterOwnedRows[1] = [2];

        var plan = new AutoFilterDropdownPlan(
            new GridRange(
                new CellAddress(SheetId, 1, 1),
                new CellAddress(SheetId, 3, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(sheet, plan, Text, "(Blanks)");

        menu.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .ToDictionary(entry => entry.Value, entry => entry.IsChecked)
            .Should()
            .BeEquivalentTo(new Dictionary<string, bool?>
            {
                ["Grape"] = false,
                ["Banana"] = true
            });
    }

    [Fact]
    public void HasActiveFilter_DetectsFilteredDataRowsInsideRange()
    {
        var sheet = CreateSheetWithList();
        var range = new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 2));

        sheet.FilterHiddenRows.Add(3);

        AutoFilterDropdownMenuPlanner.HasActiveFilter(sheet, range).Should().BeTrue();
    }

    [Fact]
    public void HasActiveFilter_IgnoresHeaderAndRowsOutsideRange()
    {
        var sheet = CreateSheetWithList();
        var range = new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 2));

        sheet.FilterHiddenRows.UnionWith([1u, 8u]);

        AutoFilterDropdownMenuPlanner.HasActiveFilter(sheet, range).Should().BeFalse();
    }

    private static Sheet CreateSheetWithList()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Name"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Score"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("Ada"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(1));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Beth"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(2));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("Cy"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(3));
        return sheet;
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);

    private sealed class TestTextProvider : IAutoFilterMenuTextProvider
    {
        public string Get(string resourceKey) => resourceKey switch
        {
            "AutoFilter_SortAscending" => "Sort A to Z",
            "AutoFilter_SortDescending" => "Sort Z to A",
            "AutoFilter_SortAToZ" => "Sort A to Z",
            "AutoFilter_SortZToA" => "Sort Z to A",
            "AutoFilter_SortSmallestToLargest" => "Sort Smallest to Largest",
            "AutoFilter_SortLargestToSmallest" => "Sort Largest to Smallest",
            "AutoFilter_SortOldestToNewest" => "Sort Oldest to Newest",
            "AutoFilter_SortNewestToOldest" => "Sort Newest to Oldest",
            "AutoFilter_FilterByColor" => "Filter by Color",
            "AutoFilter_Search" => "Search",
            "AutoFilter_SelectAll" => "(Select All)",
            "AutoFilter_NoFill" => "No Fill",
            "AutoFilter_FilterFamily_Text" => "Text Filters",
            "AutoFilter_FilterFamily_Number" => "Number Filters",
            "AutoFilter_FilterFamily_Date" => "Date Filters",
            "AutoFilter_SectionSort" => "Sort",
            "AutoFilter_SectionFilter" => "Filter",
            "AutoFilter_SectionSearch" => "Search",
            "AutoFilter_SectionValues" => "Values",
            "AutoFilter_Criteria_Equals" => "Equals",
            "AutoFilter_Criteria_DoesNotEqual" => "Does Not Equal",
            "AutoFilter_Criteria_GreaterThan" => "Greater Than",
            "AutoFilter_Criteria_GreaterThanOrEqualTo" => "Greater Than or Equal To",
            "AutoFilter_Criteria_LessThan" => "Less Than",
            "AutoFilter_Criteria_LessThanOrEqualTo" => "Less Than or Equal To",
            "AutoFilter_Criteria_Between" => "Between",
            "AutoFilter_Criteria_Top10" => "Top 10",
            "AutoFilter_Criteria_Bottom10" => "Bottom 10",
            "AutoFilter_Criteria_Top10Percent" => "Top 10%",
            "AutoFilter_Criteria_Bottom10Percent" => "Bottom 10%",
            "AutoFilter_Criteria_AboveAverage" => "Above Average",
            "AutoFilter_Criteria_BelowAverage" => "Below Average",
            "AutoFilter_Criteria_Blanks" => "Blanks",
            "AutoFilter_Criteria_NonBlanks" => "NonBlanks",
            "AutoFilter_Criteria_After" => "After",
            "AutoFilter_Criteria_OnOrAfter" => "On or After",
            "AutoFilter_Criteria_Before" => "Before",
            "AutoFilter_Criteria_OnOrBefore" => "On or Before",
            "AutoFilter_Criteria_Contains" => "Contains",
            "AutoFilter_Criteria_DoesNotContain" => "Does Not Contain",
            "AutoFilter_Criteria_BeginsWith" => "Begins With",
            "AutoFilter_Criteria_EndsWith" => "Ends With",
            _ => resourceKey
        };

        public string Format(string resourceKey, string value) => resourceKey switch
        {
            "AutoFilter_ClearFilterFrom" => $"Clear Filter from {value}",
            "AutoFilter_ColumnHeader" => $"Column {value}",
            _ => $"{resourceKey}: {value}"
        };
    }
}
