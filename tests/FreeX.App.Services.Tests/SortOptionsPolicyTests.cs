using FluentAssertions;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class SortOptionsPolicyTests
{
    private static readonly SortOptionsDialogPresentation Presentation =
        SortOptionsDialogCatalog.Create(key => $"localized:{key}");

    [Fact]
    public void ResolveFirstKeyOrderSelection_CanonicalizesValuesAndLocalizedLabels()
    {
        var byValue = SortOptionsPolicy.ResolveFirstKeyOrderSelection(
            SortOptionsDialogCatalog.ShortMonthFirstKeySortOrder,
            Presentation.FirstKeySortOrders,
            preserveUnlistedEditorText: true);
        var byLabel = SortOptionsPolicy.ResolveFirstKeyOrderSelection(
            "localized:SortOptions_FirstKeyJanuaryToDecember",
            Presentation.FirstKeySortOrders,
            preserveUnlistedEditorText: true);

        byValue.SelectedChoice.Should().Be(Presentation.FirstKeySortOrders[3]);
        byValue.EditorText.Should().Be(SortOptionsDialogCatalog.ShortMonthFirstKeySortOrder);
        byLabel.SelectedChoice.Should().Be(Presentation.FirstKeySortOrders[4]);
        byLabel.EditorText.Should().Be(SortOptionsDialogCatalog.LongMonthFirstKeySortOrder);
    }

    [Fact]
    public void ResolveFirstKeyOrderSelection_PreservesOnlyRendererSupportedCustomText()
    {
        var editable = SortOptionsPolicy.ResolveFirstKeyOrderSelection(
            "Low, Medium, High",
            Presentation.FirstKeySortOrders,
            preserveUnlistedEditorText: true);
        var fixedChoice = SortOptionsPolicy.ResolveFirstKeyOrderSelection(
            "Low, Medium, High",
            Presentation.FirstKeySortOrders,
            preserveUnlistedEditorText: false);
        var blank = SortOptionsPolicy.ResolveFirstKeyOrderSelection(
            "  ",
            Presentation.FirstKeySortOrders,
            preserveUnlistedEditorText: true);

        editable.SelectedChoice.Should().BeNull();
        editable.EditorText.Should().Be("Low, Medium, High");
        fixedChoice.SelectedChoice.Should().Be(Presentation.FirstKeySortOrders[0]);
        fixedChoice.EditorText.Should().Be(SortOptionsDialogCatalog.NormalFirstKeySortOrder);
        blank.Should().Be(new SortFirstKeyOrderSelection(
            Presentation.FirstKeySortOrders[0],
            SortOptionsDialogCatalog.NormalFirstKeySortOrder));
    }

    [Fact]
    public void CreateResult_PrefersSelectionThenTrimmedEditorTextThenNormal()
    {
        SortOptionsPolicy.CreateResult(
                caseSensitive: true,
                leftToRight: true,
                Presentation.FirstKeySortOrders[2],
                "ignored")
            .Should()
            .Be(new SortDialogOptions(
                true,
                true,
                SortOptionsDialogCatalog.LongDayFirstKeySortOrder));

        SortOptionsPolicy.CreateResult(false, false, null, "  Low, Medium, High  ")
            .FirstKeySortOrder.Should().Be("Low, Medium, High");
        SortOptionsPolicy.CreateResult(false, false, null, "  ")
            .FirstKeySortOrder.Should().Be(SortOptionsDialogCatalog.NormalFirstKeySortOrder);
    }

    [Fact]
    public void CreateCommandPlan_ParsesCustomListAndOwnsOptionsAndHeaderRange()
    {
        var sheetId = SheetId.New();
        var selectedRange = new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 8, 5));
        var levels = new[]
        {
            new SortDialogLevel(1, true),
            new SortDialogLevel(2, false)
        };

        var plan = SortDialogPlanner.CreateCommandPlan(
            levels,
            new SortDialogOptions(
                CaseSensitive: true,
                LeftToRight: false,
                FirstKeySortOrder: "Low, Medium, High"),
            hasHeaders: true);

        plan.Options.Should().Be(new SortOptions(CaseSensitive: true, LeftToRight: false));
        plan.HasHeaders.Should().BeTrue();
        plan.SortKeys.Should().HaveCount(2);
        plan.SortKeys[0].CustomOrder.Should().NotBeNull();
        plan.SortKeys[0].CustomOrder!.Tokens.Should().Equal("Low", "Medium", "High");
        plan.SortKeys[1].CustomOrder.Should().BeNull();
        plan.ResolveRange(selectedRange).Should().Be(new GridRange(
            new CellAddress(sheetId, 3, 3),
            new CellAddress(sheetId, 8, 5)));

        SortDialogPlanner.CreateCommandPlan(
                levels,
                new SortDialogOptions(LeftToRight: true),
                hasHeaders: true)
            .ResolveRange(selectedRange)
            .Should()
            .Be(selectedRange);
    }
}

public sealed class SortOptionsPolicyOwnershipTests
{
    [Fact]
    public void NativeDialogsDelegateCatalogSelectionDefaultsAndResults()
    {
        var catalog = Read("src", "FreeX.App.Presentation", "Dialogs", "SortOptionsDialogCatalog.cs");
        var policy = Read("src", "FreeX.App.Services", "SortOptionsPolicy.cs");
        var wpf = Read("src", "FreeX.App.Host", "SortOptionsDialog.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var renderers = wpf + Environment.NewLine + avalonia;

        catalog.Should().Contain("SortOptions_FirstKeySunToSatShort")
            .And.Contain("SortOptions_FirstKeyJanuaryToDecember");
        policy.Should().Contain("ResolveFirstKeyOrderSelection(")
            .And.Contain("CreateResult(");
        wpf.Should().Contain("SortOptionsDialogCatalog.Create(UiText.Get)")
            .And.Contain("SortOptionsPolicy.ResolveFirstKeyOrderSelection(")
            .And.Contain("SortOptionsPolicy.CreateResult(");
        avalonia.Should().Contain("SortOptionsDialogCatalog.Create(UiText.Get)")
            .And.Contain("SortOptionsPolicy.ResolveFirstKeyOrderSelection(")
            .And.Contain("SortOptionsPolicy.CreateResult(");
        renderers.Should().NotContain("SortOptions_FirstKeySunToSatShort")
            .And.NotContain("SortOptions_FirstKeyJanuaryToDecember")
            .And.NotContain("private static string NormalizeFirstKeySortOrder")
            .And.NotContain("const string normalFirstKeySortOrder");
    }

    [Fact]
    public void SortCommandsDelegateCustomParsingOptionsAndRangePlanning()
    {
        var planner = Read("src", "FreeX.App.Services", "SortDialogPlanner.cs");
        var policy = Read("src", "FreeX.App.Services", "SortOptionsPolicy.cs");
        var session = Read("src", "FreeX.App.Services", "WorkbookSession.cs");
        var wpf = Read("src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var renderers = wpf + Environment.NewLine + avalonia;

        planner.Should().Contain("public static SortDialogCommandPlan CreateCommandPlan(")
            .And.Contain("SortOptionsPolicy.ApplyFirstKeySortOrder(")
            .And.Contain("SortOptionsPolicy.CreateCoreOptions(options)");
        policy.Should().Contain("CustomSortOrder.TryParse(firstKeySortOrder, out var customOrder)");
        session.Should().Contain("public WorkbookCellEditResult SortSelectedRange(SortDialogCommandPlan sortPlan)")
            .And.Contain("sortPlan.CreateCommand");
        // R142-services-sort-customdialog-1: both hosts now resolve the Sort Warning (via
        // ResolveSortRangeAfterAdjacentDataPrompt) BEFORE building the dialog's column/row/color/
        // icon choices, then execute against that same already-resolved range -- via
        // SortSelectedRange(sortPlan, range) -- rather than re-deriving/re-prompting from
        // SelectedRange a second time inside the single-arg overload.
        wpf.Should().Contain("SortDialogPlanner.CreateCommandPlan(")
            .And.Contain("_session.SortSelectedRange(sortPlan, range)")
            .And.Contain("_session.ResolveSortRangeAfterAdjacentDataPrompt(");
        avalonia.Should().Contain("SortDialogPlanner.CreateCommandPlan(")
            .And.Contain("_session.SortSelectedRange(sortPlan, range)")
            .And.Contain("_session.ResolveSortRangeAfterAdjacentDataPrompt(");
        renderers.Should().NotContain("CustomSortOrder.TryParse(")
            .And.NotContain("ApplyCustomOrderToFirstKey(");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(RepositoryFileLocator.Find(parts));
}
