namespace FreeW.App.Presentation.Ribbon;

public enum MailMergeFinishDestination
{
    NewDocument,
    Printer,
    Email,
}

public enum MailMergeRecipientScope
{
    All,
    CurrentRecord,
    FromTo,
}

public enum MailMergeFinishIssue
{
    None,
    NoRecipients,
    InvalidCurrentRecord,
    InvalidRange,
    UnsupportedDestination,
}

public readonly record struct MailMergeFinishDestinationChoice(
    MailMergeFinishDestination Destination,
    string Label,
    bool IsSupported);

public sealed record MailMergeFinishPlan(
    MailMergeFinishDestination Destination,
    MailMergeRecipientScope Scope,
    IReadOnlyList<int> RowIndexes,
    MailMergeFinishIssue Issue)
{
    public bool Success => Issue == MailMergeFinishIssue.None;
}

public readonly record struct MailMergeFinishScopeChoice(
    MailMergeRecipientScope Scope,
    string Label);

public readonly record struct MailMergeFinishDialogPlan(
    IReadOnlyList<MailMergeFinishDestinationChoice> Destinations,
    IReadOnlyList<MailMergeFinishScopeChoice> Scopes,
    int DestinationIndex,
    int ScopeIndex,
    string FromRecordText,
    string ToRecordText,
    bool HasRecipients);

public static class MailMergeFinishPlanner
{
    private static readonly MailMergeFinishDestinationChoice[] DestinationChoices =
    [
        new(MailMergeFinishDestination.NewDocument, "Edit Individual Documents", IsSupported: true),
        new(MailMergeFinishDestination.Printer, "Print Documents", IsSupported: true),
        new(MailMergeFinishDestination.Email, "Send E-mail Messages", IsSupported: false),
    ];

    public static IReadOnlyList<MailMergeFinishDestinationChoice> GetDestinationChoices() =>
        DestinationChoices;

    public static MailMergeFinishDialogPlan CreateDialogPlan(int recordCount, int currentIndex)
    {
        var scopes = new[]
        {
            new MailMergeFinishScopeChoice(MailMergeRecipientScope.All, "All"),
            new MailMergeFinishScopeChoice(MailMergeRecipientScope.CurrentRecord, "Current record"),
            new MailMergeFinishScopeChoice(MailMergeRecipientScope.FromTo, "From record ... To record"),
        };
        var current = recordCount <= 0 ? 1 : Math.Clamp(currentIndex + 1, 1, recordCount);
        return new(
            DestinationChoices,
            scopes,
            DestinationIndex: 0,
            ScopeIndex: 0,
            FromRecordText: current.ToString(),
            ToRecordText: current.ToString(),
            HasRecipients: recordCount > 0);
    }

    public static MailMergeFinishPlan PlanNewDocumentAllRecords(int recordCount) =>
        Plan(
            MailMergeFinishDestination.NewDocument,
            MailMergeRecipientScope.All,
            recordCount,
            currentIndex: 0,
            fromRecordText: null,
            toRecordText: null);

    public static MailMergeFinishPlan Plan(
        MailMergeFinishDestination destination,
        MailMergeRecipientScope scope,
        int recordCount,
        int currentIndex,
        string? fromRecordText,
        string? toRecordText)
    {
        if (recordCount <= 0)
            return Failed(destination, scope, MailMergeFinishIssue.NoRecipients);

        if (!IsDestinationSupported(destination))
            return Failed(destination, scope, MailMergeFinishIssue.UnsupportedDestination);

        return scope switch
        {
            MailMergeRecipientScope.All => Succeeded(destination, scope, Enumerable.Range(0, recordCount)),
            MailMergeRecipientScope.CurrentRecord => PlanCurrent(destination, scope, recordCount, currentIndex),
            MailMergeRecipientScope.FromTo => PlanRange(destination, scope, recordCount, fromRecordText, toRecordText),
            _ => Succeeded(destination, MailMergeRecipientScope.All, Enumerable.Range(0, recordCount)),
        };
    }

    private static MailMergeFinishPlan PlanCurrent(
        MailMergeFinishDestination destination,
        MailMergeRecipientScope scope,
        int recordCount,
        int currentIndex)
    {
        if (currentIndex < 0 || currentIndex >= recordCount)
            return Failed(destination, scope, MailMergeFinishIssue.InvalidCurrentRecord);

        return Succeeded(destination, scope, [currentIndex]);
    }

    private static MailMergeFinishPlan PlanRange(
        MailMergeFinishDestination destination,
        MailMergeRecipientScope scope,
        int recordCount,
        string? fromRecordText,
        string? toRecordText)
    {
        if (!int.TryParse(fromRecordText, out var fromRecord) ||
            !int.TryParse(toRecordText, out var toRecord) ||
            fromRecord < 1 ||
            toRecord < fromRecord ||
            toRecord > recordCount)
            return Failed(destination, scope, MailMergeFinishIssue.InvalidRange);

        return Succeeded(
            destination,
            scope,
            Enumerable.Range(fromRecord - 1, toRecord - fromRecord + 1));
    }

    private static bool IsDestinationSupported(MailMergeFinishDestination destination) =>
        destination is MailMergeFinishDestination.NewDocument or MailMergeFinishDestination.Printer;

    private static MailMergeFinishPlan Succeeded(
        MailMergeFinishDestination destination,
        MailMergeRecipientScope scope,
        IEnumerable<int> rowIndexes) =>
        new(destination, scope, rowIndexes.ToList(), MailMergeFinishIssue.None);

    private static MailMergeFinishPlan Failed(
        MailMergeFinishDestination destination,
        MailMergeRecipientScope scope,
        MailMergeFinishIssue issue) =>
        new(destination, scope, [], issue);
}
