using System.IO;

using FreeX.App.Presentation.InteractionValidation;

namespace FreeX.App.Avalonia.Tests;

public sealed class DialogRangeSelectionNeutralInventoryTests
{
    private static readonly string[] ExpectedTargetIds =
    [
        "range.named-ranges.selected-refers-to",
        "range.named-ranges.definition-refers-to",
        "range.pivot-create.source",
        "range.pivot-create.destination",
        "range.scenario-manager.changing-cells",
        "range.scenario-manager.result-cells",
        "range.function-argument.reference",
        "range.conditional-format.applies-to",
        "range.move-pivot.destination",
        "range.pivot-data-source.range",
    ];

    private static readonly (string FileName, string TargetId, string PickerAutomationId, string TextBoxAutomationId)[] ExpectedWiring =
    [
        ("MainWindow.DefinedNames.cs", "range.named-ranges.selected-refers-to", "NameManagerSelectedRefersToPickerButton", "NameManagerSelectedRefersToBox"),
        ("MainWindow.DefinedNames.cs", "range.named-ranges.definition-refers-to", "DefineNameRefersToPickerButton", "DefineNameRefersToBox"),
        ("MainWindow.PivotCreate.cs", "range.pivot-create.source", "InsertPivotTableSourceRangePickerButton", "InsertPivotTableSourceRangeBox"),
        ("MainWindow.PivotCreate.cs", "range.pivot-create.destination", "InsertPivotTableDestinationRangePickerButton", "InsertPivotTableDestinationRangeBox"),
        ("MainWindow.ScenarioManagerRangePickers.cs", "range.scenario-manager.changing-cells", "FreeXAutomationIdCatalog.ScenarioManager.ChangingCellsPickerButton", "FreeXAutomationIdCatalog.ScenarioManager.ChangingCellsBox"),
        ("MainWindow.ScenarioManagerRangePickers.cs", "range.scenario-manager.result-cells", "FreeXAutomationIdCatalog.ScenarioManager.ResultCellsPickerButton", "FreeXAutomationIdCatalog.ScenarioManager.ResultCellsBox"),
        ("MainWindow.InsertFunction.cs", "range.function-argument.reference", "FunctionArgumentReferencePicker", "FunctionArgumentBox"),
        ("MainWindow.ConditionalFormat.cs", "range.conditional-format.applies-to", "ManageConditionalFormatsAppliesToPickerButton", "ManageConditionalFormatsAppliesToBox"),
        ("MainWindow.PivotMove.cs", "range.move-pivot.destination", "MovePivotDestinationPickerButton", "MovePivotDestinationBox"),
        ("MainWindow.PivotDataSource.cs", "range.pivot-data-source.range", "PivotDataSourceRangePickerButton", "PivotDataSourceRangeBox"),
    ];

    [Fact]
    public void NeutralRangeTargets_RecordTheTenSharedInventoryIds()
    {
        InteractiveValidationInventory.WorksheetRangeTargets
            .Where(target => ExpectedTargetIds.Contains(target.Id, StringComparer.Ordinal))
            .Select(target => target.Id)
            .Should().BeEquivalentTo(ExpectedTargetIds);
        ExpectedTargetIds.Should().OnlyHaveUniqueItems().And.HaveCount(10);
    }

    [Fact]
    public void NeutralDialogBuilders_ExposeStableControlsAndDelegateToTheSharedPickerSession()
    {
        foreach (var wiring in ExpectedWiring)
        {
            var source = ReadSource(wiring.FileName);
            source.Should().Contain(wiring.TargetId, $"{wiring.FileName} should wire {wiring.TargetId}");
            source.Should().Contain(wiring.PickerAutomationId, $"{wiring.TargetId} needs a stable picker automation id");
            source.Should().Contain(wiring.TextBoxAutomationId, $"{wiring.TargetId} needs a stable input automation id");
            source.Should().Contain("AttachDialogRangePicker", $"{wiring.TargetId} should use the shared picker session");
        }
    }

    private static string ReadSource(string fileName) =>
        File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", fileName));

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
