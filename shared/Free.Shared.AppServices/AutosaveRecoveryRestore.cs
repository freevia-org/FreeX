namespace Free.Shared.AppServices;

/// <summary>
/// How an application restores an accepted recovery snapshot into the CURRENT window: either by
/// handing the snapshot path (and the original file path) to the app's guarded open workflow, or by
/// handing back the already-deserialized document. Lets the shared native autosave hosts complete a
/// recovery through <see cref="AutosaveApplicationSession{TDocument}"/> without knowing which of the
/// two shapes the product binds.
/// </summary>
public sealed class AutosaveRecoveryRestore<TDocument>
{
    private readonly Func<
        AutosaveApplicationSession<TDocument>,
        AutosaveRecoveryPlan,
        bool,
        AutosaveRecoveryRestoreExceptionPolicy,
        bool> _complete;

    private AutosaveRecoveryRestore(
        Func<
            AutosaveApplicationSession<TDocument>,
            AutosaveRecoveryPlan,
            bool,
            AutosaveRecoveryRestoreExceptionPolicy,
            bool> complete) =>
        _complete = complete;

    /// <summary>Restore by path: <c>(snapshotPath, originalFilePath) =&gt; succeeded</c>.</summary>
    public static AutosaveRecoveryRestore<TDocument> FromSnapshotPath(
        Func<string, string?, bool> restoreSnapshot)
    {
        ArgumentNullException.ThrowIfNull(restoreSnapshot);
        return new((session, plan, accepted, policy) =>
            session.CompleteRecovery(plan, accepted, restoreSnapshot, policy));
    }

    /// <summary>Restore by document: the session deserializes the snapshot and hands it to the app.</summary>
    public static AutosaveRecoveryRestore<TDocument> FromDocument(
        Action<TDocument, string?> applyRecoveredDocument)
    {
        ArgumentNullException.ThrowIfNull(applyRecoveredDocument);
        return new((session, plan, accepted, policy) =>
            session.CompleteDocumentRecovery(plan, accepted, applyRecoveredDocument, policy));
    }

    public bool Complete(
        AutosaveApplicationSession<TDocument> session,
        AutosaveRecoveryPlan plan,
        bool accepted,
        AutosaveRecoveryRestoreExceptionPolicy exceptionPolicy) =>
        _complete(session, plan, accepted, exceptionPolicy);
}
