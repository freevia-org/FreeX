using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

public sealed record MailMergeRibbonBindings(
    IRibbonCommand Envelopes,
    IRibbonCommand Labels,
    IRibbonCommand StartLetters,
    IRibbonCommand StartDirectory,
    IRibbonCommand StartNormalDocument,
    IRibbonCommand SelectRecipients,
    IRibbonCommand InsertMergeField,
    IRibbonCommand InsertAddressBlock,
    IRibbonCommand InsertGreetingLine,
    IRibbonCommand MatchFields,
    IRibbonCommand FilterSortRecipients,
    Func<MailMergeRuleKind, IRibbonCommand> CreateRuleCommand,
    IRibbonCommand InsertNextRecordField,
    IRibbonCommand InsertMergeRecordNumberField,
    IRibbonCommand InsertMergeSequenceNumberField,
    IRibbonCommand TogglePreview,
    IRibbonCommand FirstRecord,
    IRibbonCommand PreviousRecord,
    IRibbonCommand NextRecord,
    IRibbonCommand LastRecord,
    IRibbonCommand FinishMerge,
    IRibbonCommand SendEmail,
    IRibbonCommand? FindRecipient = null,
    IRibbonCommand? CheckErrors = null);

/// <summary>
/// Owns Mailings-tab command identity, aliases, rule-kind mapping, and unavailable-route policy.
/// Renderers provide only concrete mail-merge engine, dialog, document, print, and delivery adapters.
/// </summary>
public static class MailMergeRibbonWorkflow
{
    public static IReadOnlyList<FreeWRibbonCommandAction> Actions { get; } =
    [
        FreeWRibbonCommandAction.MergeEnvelopes,
        FreeWRibbonCommandAction.MergeLabels,
        FreeWRibbonCommandAction.StartMailMerge,
        FreeWRibbonCommandAction.StartMailMergeLetters,
        FreeWRibbonCommandAction.StartMailMergeDirectory,
        FreeWRibbonCommandAction.StartMailMergeNormal,
        FreeWRibbonCommandAction.MergeData,
        FreeWRibbonCommandAction.MergeEditRecipients,
        FreeWRibbonCommandAction.MergeField,
        FreeWRibbonCommandAction.MergeAddressBlock,
        FreeWRibbonCommandAction.MergeGreetingLine,
        FreeWRibbonCommandAction.MergeMatchFields,
        FreeWRibbonCommandAction.MergeFilterSort,
        FreeWRibbonCommandAction.MergeRules,
        FreeWRibbonCommandAction.MergeRuleIf,
        FreeWRibbonCommandAction.MergeRuleSkipRecordIf,
        FreeWRibbonCommandAction.MergeRuleNextRecordIf,
        FreeWRibbonCommandAction.MergeNextRecord,
        FreeWRibbonCommandAction.MergeRecordNumber,
        FreeWRibbonCommandAction.MergeSequenceNumber,
        FreeWRibbonCommandAction.MergeRuleFillIn,
        FreeWRibbonCommandAction.MergeRuleAsk,
        FreeWRibbonCommandAction.MergeRuleSet,
        FreeWRibbonCommandAction.MergeRuleRef,
        FreeWRibbonCommandAction.MergePreview,
        FreeWRibbonCommandAction.MergePreviewFirst,
        FreeWRibbonCommandAction.MergePreviewPrevious,
        FreeWRibbonCommandAction.MergePreviewNext,
        FreeWRibbonCommandAction.MergePreviewLast,
        FreeWRibbonCommandAction.MergeFindRecipient,
        FreeWRibbonCommandAction.MergeCheckErrors,
        FreeWRibbonCommandAction.MergeFinish,
        FreeWRibbonCommandAction.MergeEmail,
    ];

    public static void Register(
        IRibbonCommandRegistry registry,
        MailMergeRibbonBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(bindings.CreateRuleCommand);

        Bind(FreeWRibbonCommandAction.MergeEnvelopes, bindings.Envelopes);
        Bind(FreeWRibbonCommandAction.MergeLabels, bindings.Labels);
        Bind(FreeWRibbonCommandAction.StartMailMerge, bindings.StartLetters);
        Bind(FreeWRibbonCommandAction.StartMailMergeLetters, bindings.StartLetters);
        Bind(FreeWRibbonCommandAction.StartMailMergeDirectory, bindings.StartDirectory);
        Bind(FreeWRibbonCommandAction.StartMailMergeNormal, bindings.StartNormalDocument);
        BindWithAliases(
            FreeWRibbonCommandAction.MergeData,
            bindings.SelectRecipients,
            "freew.select-recipients");
        Bind(FreeWRibbonCommandAction.MergeEditRecipients, bindings.SelectRecipients);
        Bind(FreeWRibbonCommandAction.MergeField, bindings.InsertMergeField);
        BindWithAliases(
            FreeWRibbonCommandAction.MergeAddressBlock,
            bindings.InsertAddressBlock,
            "freew.address-block");
        BindWithAliases(
            FreeWRibbonCommandAction.MergeGreetingLine,
            bindings.InsertGreetingLine,
            "freew.greeting-line");
        Bind(FreeWRibbonCommandAction.MergeMatchFields, bindings.MatchFields);
        Bind(FreeWRibbonCommandAction.MergeFilterSort, bindings.FilterSortRecipients);
        Bind(FreeWRibbonCommandAction.MergeRules, EmptyRibbonCommand.Instance);
        BindRule(FreeWRibbonCommandAction.MergeRuleIf, MailMergeRuleKind.IfThenElse);
        BindRule(FreeWRibbonCommandAction.MergeRuleSkipRecordIf, MailMergeRuleKind.SkipRecordIf);
        BindRule(FreeWRibbonCommandAction.MergeRuleNextRecordIf, MailMergeRuleKind.NextRecordIf);
        Bind(FreeWRibbonCommandAction.MergeNextRecord, bindings.InsertNextRecordField);
        Bind(FreeWRibbonCommandAction.MergeRecordNumber, bindings.InsertMergeRecordNumberField);
        Bind(FreeWRibbonCommandAction.MergeSequenceNumber, bindings.InsertMergeSequenceNumberField);
        BindRule(FreeWRibbonCommandAction.MergeRuleFillIn, MailMergeRuleKind.FillIn);
        BindRule(FreeWRibbonCommandAction.MergeRuleAsk, MailMergeRuleKind.Ask);
        BindRule(FreeWRibbonCommandAction.MergeRuleSet, MailMergeRuleKind.Set);
        BindRule(FreeWRibbonCommandAction.MergeRuleRef, MailMergeRuleKind.Ref);
        BindWithAliases(
            FreeWRibbonCommandAction.MergePreview,
            bindings.TogglePreview,
            "freew.preview-results");
        Bind(FreeWRibbonCommandAction.MergePreviewFirst, bindings.FirstRecord);
        BindWithAliases(
            FreeWRibbonCommandAction.MergePreviewPrevious,
            bindings.PreviousRecord,
            "freew.prev-record");
        BindWithAliases(
            FreeWRibbonCommandAction.MergePreviewNext,
            bindings.NextRecord,
            "freew.next-record");
        Bind(FreeWRibbonCommandAction.MergePreviewLast, bindings.LastRecord);
        Bind(
            FreeWRibbonCommandAction.MergeFindRecipient,
            bindings.FindRecipient ?? FreeWRibbonExecutionProfile.UnavailableCommand);
        Bind(
            FreeWRibbonCommandAction.MergeCheckErrors,
            bindings.CheckErrors ?? FreeWRibbonExecutionProfile.UnavailableCommand);
        BindWithAliases(
            FreeWRibbonCommandAction.MergeFinish,
            bindings.FinishMerge,
            "freew.finish-merge");
        Bind(FreeWRibbonCommandAction.MergeEmail, bindings.SendEmail);

        void Bind(FreeWRibbonCommandAction action, IRibbonCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            registry.Bind(action, command);
        }

        void BindWithAliases(
            FreeWRibbonCommandAction action,
            IRibbonCommand command,
            params string[] aliases)
        {
            Bind(action, command);
            foreach (var alias in aliases)
                registry.Register(alias, command);
        }

        void BindRule(FreeWRibbonCommandAction action, MailMergeRuleKind kind) =>
            Bind(action, bindings.CreateRuleCommand(kind));
    }
}
