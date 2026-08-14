using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class MailMergeRibbonWorkflowTests
{
    [Fact]
    public void RegistersEveryOwnedActionAndCompatibilityAliasWithSharedIdentity()
    {
        var registry = new FreeWRibbonCommandBindingPorts();
        var bindings = CreateBindings([]);

        MailMergeRibbonWorkflow.Register(registry, bindings);

        MailMergeRibbonWorkflow.Actions.Should().OnlyHaveUniqueItems().And.HaveCount(33);
        foreach (var action in MailMergeRibbonWorkflow.Actions)
        {
            var id = FreeWRibbonCommandWorkflow.GetPrimaryCommandId(action);
            registry.TryGet(id, out var command).Should().BeTrue(action.ToString());
            command.Should().NotBeNull();
        }

        SameCommand(registry, "freew.start-mail-merge", "freew.start-mail-merge-letters");
        SameCommand(registry, "freew.merge-data", "freew.merge-edit-recipients");
        SameCommand(registry, "freew.merge-data", "freew.select-recipients");
        SameCommand(registry, "freew.merge-address-block", "freew.address-block");
        SameCommand(registry, "freew.merge-greeting-line", "freew.greeting-line");
        SameCommand(registry, "freew.merge-preview", "freew.preview-results");
        SameCommand(registry, "freew.merge-preview-next", "freew.next-record");
        SameCommand(registry, "freew.merge-preview-previous", "freew.prev-record");
        SameCommand(registry, "freew.merge-finish", "freew.finish-merge");
    }

    [Fact]
    public void SharedWorkflowOwnsRuleKindsAndFailsClosedForMissingHostRoutes()
    {
        var events = new List<string>();
        var registry = new FreeWRibbonCommandBindingPorts();
        MailMergeRibbonWorkflow.Register(registry, CreateBindings(events));

        Execute(registry, "freew.merge-rule-if");
        Execute(registry, "freew.merge-rule-skip-record-if");
        Execute(registry, "freew.merge-rule-next-record-if");
        Execute(registry, "freew.merge-rule-fill-in");
        Execute(registry, "freew.merge-rule-ask");
        Execute(registry, "freew.merge-rule-set");
        Execute(registry, "freew.merge-rule-ref");

        events.Should().Equal(
            "rule:IfThenElse",
            "rule:SkipRecordIf",
            "rule:NextRecordIf",
            "rule:FillIn",
            "rule:Ask",
            "rule:Set",
            "rule:Ref");

        Stateful(registry, "freew.merge-find-recipient").GetState().IsEnabled.Should().BeFalse();
        Stateful(registry, "freew.merge-check-errors").GetState().IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void BothRenderersDelegateMailingsIdentityToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("MailMergeRibbonWorkflow.Register(");
            source.Should().Contain("new MailMergeRibbonBindings(");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.StartMailMerge");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.MergeData");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.MergeRuleIf");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.MergePreview");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.MergeFinish");
            source.Should().NotContain("RegisterMailingsAlias(");
        }
    }

    private static MailMergeRibbonBindings CreateBindings(ICollection<string> events)
    {
        IRibbonCommand Command(string name) => new RecordingCommand(events, name);

        return new MailMergeRibbonBindings(
            Envelopes: Command("envelopes"),
            Labels: Command("labels"),
            StartLetters: Command("letters"),
            StartDirectory: Command("directory"),
            StartNormalDocument: Command("normal"),
            SelectRecipients: Command("recipients"),
            InsertMergeField: Command("field"),
            InsertAddressBlock: Command("address"),
            InsertGreetingLine: Command("greeting"),
            MatchFields: Command("match"),
            FilterSortRecipients: Command("filter-sort"),
            CreateRuleCommand: kind => Command($"rule:{kind}"),
            InsertNextRecordField: Command("next-field"),
            InsertMergeRecordNumberField: Command("record-number"),
            InsertMergeSequenceNumberField: Command("sequence-number"),
            TogglePreview: Command("preview"),
            FirstRecord: Command("first"),
            PreviousRecord: Command("previous"),
            NextRecord: Command("next"),
            LastRecord: Command("last"),
            FinishMerge: Command("finish"),
            SendEmail: Command("email"));
    }

    private static void SameCommand(
        FreeWRibbonCommandBindingPorts registry,
        string canonicalId,
        string aliasId)
    {
        registry.TryGet(canonicalId, out var canonical).Should().BeTrue(canonicalId);
        registry.TryGet(aliasId, out var alias).Should().BeTrue(aliasId);
        alias.Should().BeSameAs(canonical);
    }

    private static void Execute(FreeWRibbonCommandBindingPorts registry, string id)
    {
        registry.TryGet(id, out var command).Should().BeTrue(id);
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static IRibbonStatefulCommand Stateful(
        FreeWRibbonCommandBindingPorts registry,
        string id)
    {
        registry.TryGet(id, out var command).Should().BeTrue(id);
        return command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
    }

    private sealed class RecordingCommand(ICollection<string> events, string name) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => events.Add(name);
    }
}
