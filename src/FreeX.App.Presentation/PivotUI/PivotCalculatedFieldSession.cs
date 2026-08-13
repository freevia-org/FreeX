using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

public sealed record PivotCalculatedFieldSessionText(
    string EmptyNameMessage,
    string EmptyFormulaMessage,
    string NoFieldToDeleteMessage,
    string SavedStatusFormat,
    string DeletedStatusFormat)
{
    public static PivotCalculatedFieldSessionText Default { get; } = new(
        PivotCalculatedFieldPlanner.EmptyNameMessage,
        PivotCalculatedFieldPlanner.EmptyFormulaMessage,
        PivotCalculatedFieldPlanner.NoFieldToDeleteMessage,
        "Calculated field \"{0}\" saved.",
        "Calculated field \"{0}\" deleted.");
}

public sealed record PivotCalculatedFieldSubmission(
    PivotCalculatedWorkflowOperation Operation,
    PivotCalculatedFieldPlanner.PivotCalculatedFieldResult? Result,
    string Name);

public sealed record PivotCalculatedFieldSubmissionPlan(
    PivotCalculatedFieldSubmission? Submission,
    PivotCalculatedWorkflowIssue? Issue)
{
    public bool Success => Submission is not null && Issue is null;
}

public sealed record PivotCalculatedFieldCommitPlan(
    IReadOnlyList<PivotCalculatedFieldModel> CalculatedFields,
    PivotCalculatedWorkflowIssue? Issue,
    string? Status)
{
    public bool Success => Issue is null;
}

/// <summary>
/// Portable draft and add/modify/delete workflow for calculated PivotTable fields. Native shells own controls,
/// focus, dialog lifetime, and command execution; this session owns list projection, state transitions, and
/// planner-backed validation and mutations.
/// </summary>
public sealed class PivotCalculatedFieldSession
{
    private readonly PivotTableModel _pivotTable;
    private readonly string _newFormula;
    private readonly PivotCalculatedFieldSessionText _text;

    private PivotCalculatedFieldSession(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        string? name,
        string? formula,
        string newFormula,
        PivotCalculatedFieldSessionText text)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(text);

        _pivotTable = pivotTable;
        _newFormula = newFormula;
        _text = text;
        ExistingNames = PivotCalculatedFieldPlanner.ExistingFieldNames(pivotTable);
        FieldReferences = PivotCalculatedFieldPlanner.AvailableFieldReferences(headers);
        Name = name ?? string.Empty;
        Formula = formula ?? string.Empty;
    }

    public IReadOnlyList<string> ExistingNames { get; }
    public IReadOnlyList<string> FieldReferences { get; }
    public string? SelectedExistingName { get; private set; }
    public string Name { get; private set; }
    public string Formula { get; private set; }
    public PivotCalculatedDraft Draft => new(Name, Formula);

    public static PivotCalculatedFieldSession Create(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotCalculatedFieldSessionText? text = null,
        string newFormula = "= ") =>
        new(pivotTable, headers, string.Empty, newFormula, newFormula, text ?? PivotCalculatedFieldSessionText.Default);

    public static PivotCalculatedFieldSession CreateDraft(
        string? name,
        string? formula,
        IEnumerable<string> fieldNames,
        PivotCalculatedFieldSessionText? text = null)
    {
        ArgumentNullException.ThrowIfNull(fieldNames);
        return new PivotCalculatedFieldSession(
            new PivotTableModel(),
            fieldNames.ToList(),
            name,
            formula,
            string.Empty,
            text ?? PivotCalculatedFieldSessionText.Default);
    }

    public PivotCalculatedDraft UpdateDraft(string? name, string? formula)
    {
        Name = name ?? string.Empty;
        Formula = formula ?? string.Empty;
        return Draft;
    }

    public PivotCalculatedDraft SelectExisting(string? name)
    {
        var match = PivotCalculatedFieldPlanner.FindByName(_pivotTable, name);
        SelectedExistingName = match?.Name;
        return UpdateDraft(match?.Name ?? string.Empty, match?.Formula ?? _newFormula);
    }

    public (string Formula, int CaretIndex) InsertReference(
        string? reference,
        int selectionStart,
        int selectionLength)
    {
        var inserted = PivotCalculatedFieldPlanner.InsertReference(
            Formula,
            reference,
            selectionStart,
            selectionLength);
        Formula = inserted.Formula;
        return inserted;
    }

    public PivotCalculatedFieldSubmissionPlan PlanSave(string? name, string? formula)
    {
        UpdateDraft(name, formula);
        if (!PivotCalculatedFieldPlanner.TryCreateResult(Name, Formula, out var result, out var error))
        {
            var issue = string.Equals(error, PivotCalculatedFieldPlanner.EmptyFormulaMessage, StringComparison.Ordinal)
                ? new PivotCalculatedWorkflowIssue(PivotCalculatedInputTarget.Formula, _text.EmptyFormulaMessage)
                : new PivotCalculatedWorkflowIssue(PivotCalculatedInputTarget.Name, _text.EmptyNameMessage);
            return new PivotCalculatedFieldSubmissionPlan(null, issue);
        }

        return new PivotCalculatedFieldSubmissionPlan(
            new PivotCalculatedFieldSubmission(PivotCalculatedWorkflowOperation.Save, result, result!.Name),
            null);
    }

    public PivotCalculatedFieldSubmissionPlan PlanDelete(string? name)
    {
        Name = name ?? string.Empty;
        var normalizedName = Name.Trim();
        if (normalizedName.Length == 0)
        {
            return new PivotCalculatedFieldSubmissionPlan(
                null,
                new PivotCalculatedWorkflowIssue(
                    PivotCalculatedInputTarget.Name,
                    _text.NoFieldToDeleteMessage));
        }

        return new PivotCalculatedFieldSubmissionPlan(
            new PivotCalculatedFieldSubmission(PivotCalculatedWorkflowOperation.Delete, null, normalizedName),
            null);
    }

    public PivotCalculatedFieldCommitPlan Commit(PivotCalculatedFieldSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (submission.Operation == PivotCalculatedWorkflowOperation.Save && submission.Result is not null)
        {
            return new PivotCalculatedFieldCommitPlan(
                PivotCalculatedFieldPlanner.Upsert(_pivotTable, submission.Result),
                null,
                FormatStatus(_text.SavedStatusFormat, submission.Name));
        }

        if (submission.Operation == PivotCalculatedWorkflowOperation.Delete)
        {
            var removed = PivotCalculatedFieldPlanner.TryRemove(
                _pivotTable,
                submission.Name,
                out var remaining,
                out _);
            return removed
                ? new PivotCalculatedFieldCommitPlan(
                    remaining,
                    null,
                    FormatStatus(_text.DeletedStatusFormat, submission.Name))
                : new PivotCalculatedFieldCommitPlan(
                    remaining,
                    new PivotCalculatedWorkflowIssue(
                        PivotCalculatedInputTarget.Name,
                        _text.NoFieldToDeleteMessage),
                    null);
        }

        throw new ArgumentException("The calculated-field submission is not valid.", nameof(submission));
    }

    private static string? FormatStatus(string format, string name) =>
        string.IsNullOrEmpty(format)
            ? null
            : string.Format(CultureInfo.CurrentCulture, format, name);
}
