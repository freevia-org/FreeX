using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WpfDefinedNamesSessionOwnershipSourceTests
{
    [Fact]
    public void NamedRangeDialog_DelegatesPortableBehaviorToPresentationSession()
    {
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "NamedRangeDialog.xaml.cs");

        source.Should().Contain("private readonly DefinedNamesSession _definedNames;");
        source.Should().Contain("_items.AddRange(_definedNames.BuildRows())");
        source.Should().Contain("_definedNames.ProjectRows(_items, selected)");
        source.Should().Contain("_definedNames.PlanSave(draft, original)");
        source.Should().Contain("_definedNames.BuildDeleteCommand(vm)");
        source.Should().Contain("Func<IWorkbookCommand, CommandOutcome> _executeCommand");
        source.Should().Contain("_executeCommand(plan.Command!)");
        source.Should().Contain("_executeCommand(cmd)");

        source.Should().NotContain("NamedRangeDialogPlanner.FilterItems(");
        source.Should().NotContain("NamedRangeInputParser.TryParseRange(");
        source.Should().NotContain("FormulaEvaluator");
        source.Should().NotContain("FormatNamedFormulaValue");
        source.Should().NotContain("FormatScalarValuePreview");
        source.Should().NotContain("NameAlreadyExistsInScope");
        source.Should().NotContain(".NamedRanges");
        source.Should().NotContain(".NamedFormulas");
        source.Should().NotContain(".ScopedNamedRanges");
        source.Should().NotContain(".ScopedNamedFormulas");
        source.Should().NotContain("new DefineNamedRangeCommand");
        source.Should().NotContain("new DefineNamedFormulaCommand");
        source.Should().NotContain("new RemoveNamedRangeCommand");
        source.Should().NotContain("ICommandBus");
        source.Should().NotContain("_commandBus");
    }
}
