using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

public sealed record PivotCalculatedItemSessionText(
    string EmptyNameMessage,
    string EmptyFormulaMessage,
    string NoSourceFieldMessage,
    string NoItemToDeleteMessage,
    string SavedStatusFormat,
    string DeletedStatusFormat)
{
    public static PivotCalculatedItemSessionText Default { get; } = new(
        PivotCalculatedItemPlanner.EmptyNameMessage,
        PivotCalculatedItemPlanner.EmptyFormulaMessage,
        PivotCalculatedItemPlanner.NoSourceFieldMessage,
        PivotCalculatedItemPlanner.NoItemToDeleteMessage,
        "Calculated item \"{0}\" saved.",
        "Calculated item \"{0}\" deleted.");
}

public sealed record PivotCalculatedItemSubmission(
    PivotCalculatedWorkflowOperation Operation,
    PivotCalculatedItemPlanner.PivotCalculatedItemResult? Result,
    int SourceFieldIndex,
    string Name);

public sealed record PivotCalculatedItemSubmissionPlan(
    PivotCalculatedItemSubmission? Submission,
    PivotCalculatedWorkflowIssue? Issue)
{
    public bool Success => Submission is not null && Issue is null;
}

public sealed record PivotCalculatedItemCommitPlan(
    IReadOnlyList<PivotCalculatedItemModel> CalculatedItems,
    PivotCalculatedWorkflowIssue? Issue,
    string? Status)
{
    public bool Success => Issue is null;
}

/// <summary>
/// Portable draft and add/modify/delete workflow for calculated PivotTable items, including source-field and
/// existing-item projections. Native shells retain their own controls and dialog lifecycle.
/// </summary>
public sealed class PivotCalculatedItemSession
{
    private readonly PivotTableModel _pivotTable;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<string>> _itemReferencesBySourceFieldIndex;
    private readonly string _newFormula;
    private readonly PivotCalculatedItemSessionText _text;

    private PivotCalculatedItemSession(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotCalculatedItemPlanner.CalculatedItemField> fields,
        IReadOnlyList<string> fieldReferences,
        IReadOnlyDictionary<int, IReadOnlyList<string>> itemReferencesBySourceFieldIndex,
        int selectedSourceFieldIndex,
        string? name,
        string? formula,
        string newFormula,
        PivotCalculatedItemSessionText text)
    {
        _pivotTable = pivotTable;
        Fields = fields;
        FieldReferences = fieldReferences;
        _itemReferencesBySourceFieldIndex = itemReferencesBySourceFieldIndex;
        _newFormula = newFormula;
        _text = text;
        Name = name ?? string.Empty;
        Formula = formula ?? string.Empty;
        SelectSourceField(selectedSourceFieldIndex);
    }

    public IReadOnlyList<PivotCalculatedItemPlanner.CalculatedItemField> Fields { get; }
    public IReadOnlyList<string> FieldReferences { get; }
    public IReadOnlyList<string> ExistingNames { get; private set; } = [];
    public IReadOnlyList<string> ItemReferences { get; private set; } = [];
    public int SelectedSourceFieldIndex { get; private set; }
    public PivotCalculatedItemPlanner.CalculatedItemField? SelectedSourceField { get; private set; }
    public string? SelectedExistingName { get; private set; }
    public string Name { get; private set; }
    public string Formula { get; private set; }
    public PivotCalculatedDraft Draft => new(Name, Formula);
    public PivotCalculatedWorkflowIssue? OpenIssue => Fields.Count == 0
        ? new PivotCalculatedWorkflowIssue(PivotCalculatedInputTarget.SourceField, _text.NoSourceFieldMessage)
        : null;

    public static PivotCalculatedItemSession Create(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotCalculatedItemSessionText? text = null,
        string newFormula = "= ")
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(headers);
        var fields = PivotCalculatedItemPlanner.AvailableFields(pivotTable, headers);
        return new PivotCalculatedItemSession(
            pivotTable,
            fields,
            PivotCalculatedItemPlanner.AvailableFieldReferences(headers),
            new Dictionary<int, IReadOnlyList<string>>(),
            fields.FirstOrDefault()?.SourceFieldIndex ?? -1,
            string.Empty,
            newFormula,
            newFormula,
            text ?? PivotCalculatedItemSessionText.Default);
    }

    public static PivotCalculatedItemSession CreateDraft(
        IEnumerable<string> fieldNames,
        int selectedSourceFieldIndex,
        string? name,
        string? formula,
        IReadOnlyDictionary<int, IEnumerable<string>>? itemNamesBySourceFieldIndex = null,
        PivotCalculatedItemSessionText? text = null)
    {
        ArgumentNullException.ThrowIfNull(fieldNames);
        var headers = fieldNames.ToList();
        var itemReferences = itemNamesBySourceFieldIndex?.ToDictionary(
            pair => Math.Max(0, pair.Key),
            pair => PivotCalculatedItemPlanner.AvailableFieldReferences(pair.Value.ToList()))
            ?? new Dictionary<int, IReadOnlyList<string>>();
        return new PivotCalculatedItemSession(
            new PivotTableModel(),
            PivotCalculatedItemPlanner.AvailableSourceFields(headers),
            PivotCalculatedItemPlanner.AvailableFieldReferences(headers),
            itemReferences,
            Math.Max(0, selectedSourceFieldIndex),
            name,
            formula,
            string.Empty,
            text ?? PivotCalculatedItemSessionText.Default);
    }

    public PivotCalculatedDraft UpdateDraft(string? name, string? formula)
    {
        Name = name ?? string.Empty;
        Formula = formula ?? string.Empty;
        return Draft;
    }

    public PivotCalculatedDraft SelectSourceField(int sourceFieldIndex, bool startNew = false)
    {
        var normalizedIndex = Math.Max(0, sourceFieldIndex);
        SelectedSourceField = Fields.FirstOrDefault(field => field.SourceFieldIndex == normalizedIndex)
            ?? Fields.FirstOrDefault();
        SelectedSourceFieldIndex = SelectedSourceField?.SourceFieldIndex ?? sourceFieldIndex;
        ExistingNames = PivotCalculatedItemPlanner.ExistingItemNames(_pivotTable, SelectedSourceFieldIndex);
        ItemReferences = _itemReferencesBySourceFieldIndex.TryGetValue(
            SelectedSourceFieldIndex,
            out var itemReferences)
                ? itemReferences
                : [];
        SelectedExistingName = null;
        return startNew
            ? UpdateDraft(string.Empty, _newFormula)
            : Draft;
    }

    public PivotCalculatedDraft SelectExisting(string? name)
    {
        var match = PivotCalculatedItemPlanner.FindByName(_pivotTable, SelectedSourceFieldIndex, name);
        SelectedExistingName = match?.Name;
        return UpdateDraft(match?.Name ?? string.Empty, match?.Formula ?? _newFormula);
    }

    public (string Formula, int CaretIndex) InsertReference(
        string? reference,
        int selectionStart,
        int selectionLength)
    {
        var inserted = PivotCalculatedItemPlanner.InsertReference(
            Formula,
            reference,
            selectionStart,
            selectionLength);
        Formula = inserted.Formula;
        return inserted;
    }

    public PivotCalculatedItemSubmissionPlan PlanSave(string? name, string? formula)
    {
        UpdateDraft(name, formula);
        if (!PivotCalculatedItemPlanner.TryCreateResult(
                SelectedSourceFieldIndex,
                Name,
                Formula,
                out var result,
                out var error))
        {
            var issue = error switch
            {
                PivotCalculatedItemPlanner.NoSourceFieldMessage =>
                    new PivotCalculatedWorkflowIssue(PivotCalculatedInputTarget.SourceField, _text.NoSourceFieldMessage),
                PivotCalculatedItemPlanner.EmptyFormulaMessage =>
                    new PivotCalculatedWorkflowIssue(PivotCalculatedInputTarget.Formula, _text.EmptyFormulaMessage),
                _ => new PivotCalculatedWorkflowIssue(PivotCalculatedInputTarget.Name, _text.EmptyNameMessage)
            };
            return new PivotCalculatedItemSubmissionPlan(null, issue);
        }

        return new PivotCalculatedItemSubmissionPlan(
            new PivotCalculatedItemSubmission(
                PivotCalculatedWorkflowOperation.Save,
                result,
                SelectedSourceFieldIndex,
                result!.Name),
            null);
    }

    public PivotCalculatedItemSubmissionPlan PlanDelete(string? name)
    {
        Name = name ?? string.Empty;
        if (SelectedSourceFieldIndex < 0)
        {
            return new PivotCalculatedItemSubmissionPlan(
                null,
                new PivotCalculatedWorkflowIssue(
                    PivotCalculatedInputTarget.SourceField,
                    _text.NoSourceFieldMessage));
        }

        var normalizedName = Name.Trim();
        if (normalizedName.Length == 0)
        {
            return new PivotCalculatedItemSubmissionPlan(
                null,
                new PivotCalculatedWorkflowIssue(
                    PivotCalculatedInputTarget.Name,
                    _text.NoItemToDeleteMessage));
        }

        return new PivotCalculatedItemSubmissionPlan(
            new PivotCalculatedItemSubmission(
                PivotCalculatedWorkflowOperation.Delete,
                null,
                SelectedSourceFieldIndex,
                normalizedName),
            null);
    }

    public PivotCalculatedItemCommitPlan Commit(PivotCalculatedItemSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (submission.Operation == PivotCalculatedWorkflowOperation.Save && submission.Result is not null)
        {
            return new PivotCalculatedItemCommitPlan(
                PivotCalculatedItemPlanner.Upsert(_pivotTable, submission.Result),
                null,
                FormatStatus(_text.SavedStatusFormat, submission.Name));
        }

        if (submission.Operation == PivotCalculatedWorkflowOperation.Delete)
        {
            var removed = PivotCalculatedItemPlanner.TryRemove(
                _pivotTable,
                submission.SourceFieldIndex,
                submission.Name,
                out var remaining,
                out _);
            return removed
                ? new PivotCalculatedItemCommitPlan(
                    remaining,
                    null,
                    FormatStatus(_text.DeletedStatusFormat, submission.Name))
                : new PivotCalculatedItemCommitPlan(
                    remaining,
                    new PivotCalculatedWorkflowIssue(
                        PivotCalculatedInputTarget.Name,
                        _text.NoItemToDeleteMessage),
                    null);
        }

        throw new ArgumentException("The calculated-item submission is not valid.", nameof(submission));
    }

    private static string? FormatStatus(string format, string name) =>
        string.IsNullOrEmpty(format)
            ? null
            : string.Format(CultureInfo.CurrentCulture, format, name);
}
