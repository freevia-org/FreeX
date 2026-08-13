using FluentAssertions;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DefinedNames;

public sealed class DefinedNameUiPolicyTests
{
    [Fact]
    public void FilterCatalog_HasStableLocalizedOrderAndFallback()
    {
        DefinedNameUiPolicy.Filters
            .Select(descriptor => (descriptor.Filter, descriptor.LabelResourceKey))
            .Should()
            .Equal(
                (DefinedNameFilter.All, "NamedRange_AllNames"),
                (DefinedNameFilter.Workbook, "NamedRange_NamesScopedToWorkbook"),
                (DefinedNameFilter.Worksheet, "NamedRange_NamesScopedToWorksheet"),
                (DefinedNameFilter.Errors, "NamedRange_NamesWithErrors"),
                (DefinedNameFilter.NoErrors, "NamedRange_NamesWithoutErrors"));

        DefinedNameUiPolicy.ResolveFilter(-1).Should().Be(DefinedNameFilter.All);
        DefinedNameUiPolicy.ResolveFilter(99).Should().Be(DefinedNameFilter.All);
    }

    [Fact]
    public void ScopeAndDraftPolicy_PreservesIdentityWhenLabelsCollideAndNormalizesInput()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Workbook");
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "OrdersTable",
            DisplayName = "Orders",
            Range = Cell(sheet, 1),
        });
        var session = new DefinedNamesSession(workbook, sheet.Id);
        var options = DefinedNameUiPolicy.BuildScopeOptions(session.ScopeChoices);

        options.Select(option => option.Label).Should().Equal("Workbook", "Workbook");
        options[0].Scope.IsWorkbook.Should().BeTrue();
        options[1].SheetId.Should().Be(sheet.Id);
        DefinedNameUiPolicy.FindScopeOption(options, "Workbook", sheet.Id).SheetId.Should().Be(sheet.Id);

        DefinedNameUiPolicy.CreateDraft(" Sales ", options, 1, " Sheet1!A1:A2 ", " Rows ")
            .Should()
            .Be(new DefinedNameDraft(
                "Sales",
                DefinedNameScope.ForSheet(sheet.Id, "Workbook"),
                "Sheet1!A1:A2",
                "Rows"));
        session.ValidateNameStructure("Orders").Error.Should().Be(DefinedNameError.Duplicate);
        session.ValidateNameStructure("OrdersTable").Error.Should().Be(DefinedNameError.Duplicate);
        session.ValidateDraft(new DefinedNameDraft("Orders", options[1].Scope, "=1"))
            .Name.Error.Should().Be(DefinedNameError.Duplicate);
        DefinedNameIdentifierCatalog.GetTableNames(workbook).Should().Equal("OrdersTable", "Orders");
    }

    [Fact]
    public void SelectionPolicy_NormalizesManagerAndPasteNameCommandState()
    {
        var row = DefinedNameListProjector.CreateRow(
            "Sales",
            DefinedNameScope.Workbook,
            "Sheet1!A1:A2",
            "{1;2}");

        DefinedNameUiPolicy.PlanManagerSelection(row, DefinedNameUiProfile.Wpf).Should().Be(
            new DefinedNameManagerSelectionPlan(row, true, true, true, "Sheet1!A1:A2", true));
        DefinedNameUiPolicy.PlanManagerSelection([row], 3, DefinedNameUiProfile.Wpf).Should().Be(
            new DefinedNameManagerSelectionPlan(null, false, false, false, "", false));
        DefinedNameUiPolicy.PlanManagerSelection([row], 3, DefinedNameUiProfile.Avalonia)
            .Should().Be(new DefinedNameManagerSelectionPlan(null, false, false, false, "", true));

        var item = new PasteNamesItem("Sales", "Sheet1!A1:A2");
        DefinedNameUiPolicy.PlanPasteNamesSelection([item], 0).Should().Be(
            new PasteNamesSelectionPlan(item, true, true));
        DefinedNameUiPolicy.PlanPasteNamesSelection([item], -1).Should().Be(
            new PasteNamesSelectionPlan(null, false, true));
    }

    [Fact]
    public void UseInFormulaProfiles_PreserveRendererSpecificCatalogs()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedRange("GlobalRange", Cell(sheet, 1));
        workbook.DefineNamedRange("LocalRange", Cell(sheet, 2), NamedRangeMetadata.WorkbookScope, sheet.Id);
        workbook.NamedFormulas["GlobalFormula"] = "1+2";
        workbook.DefineNamedFormula("LocalFormula", "3+4", sheet.Id);

        static string Format(GridRange range) => $"{range.Start.ToA1()}:{range.End.ToA1()}";

        var wpf = DefinedNameUiPolicy.PlanUseInFormula(
            workbook,
            _ => throw new InvalidOperationException("The direct WPF menu does not format range targets."),
            DefinedNameUiProfile.Wpf);
        var avalonia = DefinedNameUiPolicy.PlanUseInFormula(workbook, Format, DefinedNameUiProfile.Avalonia);

        wpf.Mode.Should().Be(DefinedNameUseInFormulaMode.DirectMenu);
        wpf.Items.Select(item => item.Name).Should().Equal("GlobalRange");
        wpf.Items.Single().RefersTo.Should().BeEmpty();
        avalonia.Mode.Should().Be(DefinedNameUseInFormulaMode.PasteNamesDialog);
        avalonia.Items.Select(item => item.Name).Should().BeEquivalentTo(
            ["GlobalFormula", "GlobalRange", "Sheet1!LocalFormula", "Sheet1!LocalRange"]);
    }

    [Fact]
    public void NameBoxDefinitionPolicy_RejectsInvalidAndFormulaNamesAndBuildsRendererCommand()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var selection = Cell(sheet, 2);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Orders",
            DisplayName = "Orders",
            Range = selection,
        });
        workbook.NamedFormulas["Rate"] = "0.08";
        workbook.DefineNamedFormula("LocalRate", "0.09", sheet.Id);

        DefinedNameUiPolicy.PlanNameBoxDefinition(
                workbook, sheet.Id, selection, "A1", DefinedNameUiProfile.Wpf)
            .Rejection.Should().Be(NameBoxDefinitionRejection.InvalidIdentifier);
        DefinedNameUiPolicy.PlanNameBoxDefinition(
                workbook, sheet.Id, selection, "Rate", DefinedNameUiProfile.Wpf)
            .Rejection.Should().Be(NameBoxDefinitionRejection.ExistingFormula);
        DefinedNameUiPolicy.PlanNameBoxDefinition(
                workbook, sheet.Id, selection, "Orders", DefinedNameUiProfile.Wpf)
            .Rejection.Should().Be(NameBoxDefinitionRejection.ExistingTable);
        DefinedNameUiPolicy.ResolveNameBoxNavigationDisplayText(
                workbook, sheet.Id, " orders ")
            .Should().Be("Orders");
        DefinedNameUiPolicy.PlanNameBoxDefinition(
                workbook, sheet.Id, selection, "LocalRate", DefinedNameUiProfile.Avalonia)
            .Rejection.Should().Be(NameBoxDefinitionRejection.ExistingFormula);

        var plan = DefinedNameUiPolicy.PlanNameBoxDefinition(
            workbook, sheet.Id, selection, " Sales ", DefinedNameUiProfile.Avalonia);
        plan.CanDefine.Should().BeTrue();
        plan.Name.Should().Be("Sales");
        Run(workbook, plan.Command!).Success.Should().BeTrue();
        workbook.NamedRanges["Sales"].Should().Be(selection);
        workbook.TryGetNamedRangeMetadata("Sales", out var metadata).Should().BeTrue();
        metadata.Should().Be(NamedRangeMetadata.WorkbookScope);
    }

    [Fact]
    public void RangeSelectionAndErrorPolicy_NormalizesRequestsAndKeepsLocalizationProfilesExplicit()
    {
        DefinedNameUiPolicy.CreateRangeSelectionRequest(
                NamedRangeSelectionTarget.DefinitionRefersTo,
                " Sheet1!A1:C5 ")
            .Should()
            .Be(new NamedRangeSelectionRequest(
                NamedRangeSelectionTarget.DefinitionRefersTo,
                "Sheet1!A1:C5",
                CollapseDialog: true));

        DefinedNameUiPolicy.GetPasteNamesListErrorResourceKey(
                PasteNamesListError.NotEnoughRows,
                DefinedNameUiProfile.Wpf)
            .Should().Be("PasteNames_NotEnoughRowsMessage");
        DefinedNameUiPolicy.GetPasteNamesListErrorResourceKey(
                PasteNamesListError.NotEnoughRows,
                DefinedNameUiProfile.Avalonia)
            .Should().Be("PasteNames_NotEnoughRows");
    }

    private static GridRange Cell(Sheet sheet, uint row) =>
        new(new CellAddress(sheet.Id, row, 1), new CellAddress(sheet.Id, row, 1));

    private static CommandOutcome Run(Workbook workbook, IWorkbookCommand command) =>
        command.Apply(new TestContext(workbook));

    private sealed class TestContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException();
    }
}
