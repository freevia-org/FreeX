using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public enum MailMergeStartType
{
    Letters,
    Directory,
    NormalDocument,
}

public readonly record struct MailMergeStartChoice(
    MailMergeStartType Type,
    string Label,
    bool IsMergeMode);

public static class MailMergeStartDialogPlanner
{
    private static readonly MailMergeStartChoice[] Choices =
    [
        new(MailMergeStartType.Letters, "Letters", true),
        new(MailMergeStartType.Directory, "Directory", true),
        new(MailMergeStartType.NormalDocument, "Normal Word document", false),
    ];

    public static MailMergeStartType DefaultType => MailMergeStartType.Letters;

    public static IReadOnlyList<MailMergeStartChoice> GetChoices() => Choices;

    public static MailMergeStartType GetType(int selectedIndex) =>
        selectedIndex >= 0 && selectedIndex < Choices.Length
            ? Choices[selectedIndex].Type
            : DefaultType;

    public static int GetSelectedIndex(MailMergeStartType type) =>
        Array.FindIndex(Choices, choice => choice.Type == type) is var index && index >= 0
            ? index
            : 0;
}

public readonly record struct MailMergeRecipientDialogPlan(
    string SeedHeader,
    string InitialCsv,
    bool IsEditingExistingData);

public readonly record struct MailMergeRecipientDialogValidation(
    bool IsValid,
    bool HasRecipients,
    string Message,
    MergeData Data);

public static class MailMergeRecipientDialogPlanner
{
    public static MailMergeRecipientDialogPlan CreatePlan(
        IReadOnlyList<string> documentFields,
        MergeData? existingData = null)
    {
        ArgumentNullException.ThrowIfNull(documentFields);

        var seedHeader = existingData is not null
            ? string.Join(",", existingData.Header)
            : string.Join(",", documentFields);
        var initialCsv = existingData is not null
            ? ToCsv(existingData)
            : string.IsNullOrWhiteSpace(seedHeader) ? string.Empty : seedHeader + Environment.NewLine;

        return new(seedHeader, initialCsv, existingData is not null);
    }

    public static string? NormalizeAcceptedCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? null : csv;

    public static MailMergeRecipientDialogValidation Validate(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return new(false, false, "Enter a header row and at least one recipient.", new MergeData([], []));

        var data = MergeData.FromCsv(csv);
        return data.Count == 0
            ? new(false, false, "Enter a header row and at least one recipient.", data)
            : new(true, true, $"Ready to load {data.Count} recipient(s).", data);
    }

    public static string ToCsv(MergeData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var lines = new List<string> { string.Join(",", data.Header.Select(Escape)) };
        lines.AddRange(data.Rows.Select(row =>
            string.Join(",", data.Header.Select(header =>
                row.TryGetValue(header, out var value) ? Escape(value) : string.Empty))));
        return string.Join(Environment.NewLine, lines);
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}

public enum MailMergeInsertionKind
{
    MergeField,
    AddressBlock,
    GreetingLine,
}

public readonly record struct MailMergeInsertionPlan(
    bool IsEnabled,
    string Placeholder,
    string DisabledMessage);

public static class MailMergeInsertionPlanner
{
    public static MailMergeInsertionPlan Plan(MailMergeInsertionKind kind, bool hasRecipients)
    {
        var placeholder = kind switch
        {
            MailMergeInsertionKind.MergeField => string.Empty,
            MailMergeInsertionKind.AddressBlock => "AddressBlock",
            MailMergeInsertionKind.GreetingLine => "GreetingLine",
            _ => string.Empty,
        };
        var requiresRecipients = kind != MailMergeInsertionKind.MergeField;
        return new(
            !requiresRecipients || hasRecipients,
            placeholder,
            requiresRecipients && !hasRecipients
                ? "Select recipients first (Mailings > Select Recipients)."
                : string.Empty);
    }

    public static string? NormalizeFieldName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmed = name.Trim().Trim(MailMerge.FieldOpen, MailMerge.FieldClose).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static string CreatePlaceholder(MailMergeInsertionKind kind, string? fieldName = null)
    {
        var name = kind == MailMergeInsertionKind.MergeField
            ? NormalizeFieldName(fieldName) ?? string.Empty
            : Plan(kind, hasRecipients: true).Placeholder;
        return name.Length == 0 ? string.Empty : $"{MailMerge.FieldOpen}{name}{MailMerge.FieldClose}";
    }
}

public readonly record struct MailMergeFilterSortDialogPlan(
    IReadOnlyList<string> SortColumns,
    string SelectedSortColumn,
    bool Ascending,
    IReadOnlyList<int> IncludedRowIndexes,
    string PreviewHeader,
    IReadOnlyList<string> PreviewRows);

public static class MailMergeFilterSortDialogPlanner
{
    public static MailMergeFilterSortDialogPlan CreatePlan(MergeData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var columns = data.Header.ToList();
        var previewColumns = MailMergeRecipientFilterSortPlanner.GetPreviewColumns(data.Header);
        return new(
            columns,
            columns.FirstOrDefault() ?? string.Empty,
            Ascending: true,
            Enumerable.Range(0, data.Count).ToList(),
            MailMergeRecipientFilterSortPlanner.FormatPreviewHeader(previewColumns),
            data.Rows.Select((row, index) =>
                MailMergeRecipientFilterSortPlanner.FormatPreviewRow(index, row, previewColumns)).ToList());
    }

    public static MergeData Apply(
        MergeData data,
        IEnumerable<int> includedRowIndexes,
        string? selectedSortColumn,
        bool ascending) =>
        MailMergeRecipientFilterSortPlanner.Apply(
            data,
            includedRowIndexes.ToList(),
            selectedSortColumn ?? string.Empty,
            ascending);
}

public readonly record struct MailMergePreviewDialogPlan(
    int CurrentIndex,
    int RecordCount,
    bool CanGoPrevious,
    bool CanGoNext,
    string RecordLabel);

public enum MailMergePreviewDialogAction
{
    MovePrevious,
    MoveNext,
    Done,
    Cancel,
}

public static class MailMergePreviewDialogPlanner
{
    public static MailMergePreviewDialogPlan CreatePlan(int currentIndex, int recordCount)
    {
        var index = recordCount <= 0 ? 0 : Math.Clamp(currentIndex, 0, recordCount - 1);
        return new(
            index,
            Math.Max(0, recordCount),
            index > 0,
            recordCount > 0 && index < recordCount - 1,
            recordCount > 0 ? $"Record {index + 1} of {recordCount}" : "No records");
    }

    public static int Move(int currentIndex, int recordCount, bool next) =>
        MailMergePreviewNavigationPlanner.TargetIndex(
            next ? MailMergePreviewNavigationAction.Next : MailMergePreviewNavigationAction.Previous,
            currentIndex,
            recordCount);
}

public readonly record struct MailMergeFindRecipientResult(
    bool Found,
    int Index,
    string Query,
    string Message);

public static class MailMergeFindRecipientPlanner
{
    public static MailMergeFindRecipientResult Find(
        MergeData data,
        string? query,
        int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        var normalized = query?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || data.Count == 0)
            return new(false, 0, normalized, "Enter a search value and load recipients first.");

        var start = Math.Clamp(startIndex, 0, data.Count - 1);
        var indexes = Enumerable.Range(start, data.Count - start)
            .Concat(Enumerable.Range(0, start));
        foreach (var index in indexes)
        {
            if (data.Rows[index].Values.Any(value => value.Contains(normalized, StringComparison.OrdinalIgnoreCase)))
                return new(true, index, normalized, $"Found recipient {index + 1} of {data.Count}.");
        }

        return new(false, start, normalized, $"No recipient contains \"{normalized}\".");
    }
}

public readonly record struct MailMergeCheckForErrorsChoice(
    MailMergeCheckForErrorsMode Mode,
    string Label);

public enum MailMergeCheckForErrorsMode
{
    SimulateAndReport,
    CompleteAndPause,
    CompleteWithoutPausing,
}

public sealed record MailMergeErrorCheckIssue(string Instruction, string Message);

public sealed record MailMergeErrorCheckResult(
    MailMergeCheckForErrorsMode Mode,
    int RecordsChecked,
    IReadOnlyList<MailMergeErrorCheckIssue> Issues,
    bool ShouldCompleteMerge)
{
    public bool HasErrors => Issues.Count > 0;
    public bool ShouldPauseForErrors =>
        Mode == MailMergeCheckForErrorsMode.CompleteAndPause && HasErrors;
    public bool ShouldOpenReportDocument =>
        Mode == MailMergeCheckForErrorsMode.SimulateAndReport
        || Mode == MailMergeCheckForErrorsMode.CompleteWithoutPausing && HasErrors;

    public string Message
    {
        get
        {
            var prefix = $"Checked {RecordsChecked} recipient(s).";
            if (!HasErrors)
                return prefix + " No mail merge errors were found.";

            var details = string.Join(" ", Issues.Take(3).Select(issue => issue.Message));
            var remainder = Issues.Count > 3 ? $" {Issues.Count - 3} more error(s)." : string.Empty;
            return $"{prefix} Found {Issues.Count} error(s). {details}{remainder}";
        }
    }
}

public static class MailMergeCheckForErrorsPlanner
{
    private static readonly string[] RulePrefixes =
    [
        "If ", "Skip Record If ", "Next Record If ", "Set ", "Ref ", "Fill-in ", "Ask "
    ];

    private static readonly MailMergeCheckForErrorsChoice[] Choices =
    [
        new(MailMergeCheckForErrorsMode.SimulateAndReport, "Simulate the merge and report errors in a new document"),
        new(MailMergeCheckForErrorsMode.CompleteAndPause, "Complete the merge, pausing to report each error"),
        new(MailMergeCheckForErrorsMode.CompleteWithoutPausing, "Complete the merge without pausing"),
    ];

    public static MailMergeCheckForErrorsMode DefaultMode => MailMergeCheckForErrorsMode.SimulateAndReport;

    public static IReadOnlyList<MailMergeCheckForErrorsChoice> GetChoices() => Choices;

    public static MailMergeCheckForErrorsMode GetMode(int selectedIndex) =>
        selectedIndex >= 0 && selectedIndex < Choices.Length
            ? Choices[selectedIndex].Mode
            : DefaultMode;

    public static MailMergeErrorCheckResult Check(
        TextDocument template,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        MailMergeCheckForErrorsMode mode)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(rows);

        var issues = new List<MailMergeErrorCheckIssue>();
        var firstRow = rows.FirstOrDefault()
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var instructions = MailMerge.FieldNames(template).ToList();
        foreach (var section in template.Sections)
            AddInstructions(section.HeadersFooters, instructions);

        foreach (var instruction in instructions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsSpecialInstruction(instruction))
                continue;

            var rule = MergeRuleEvaluator.Evaluate(instruction, firstRow, new MergeState(), 1);
            if (rule is not null)
            {
                if (MergeRuleEvaluator.TryGetReferencedFieldName(instruction, out var fieldName)
                    && !firstRow.ContainsKey(fieldName))
                {
                    issues.Add(new(instruction,
                        $"Rule '{instruction}' references missing recipient field '{fieldName}'."));
                }
                continue;
            }

            if (RulePrefixes.Any(prefix => instruction.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new(instruction, $"Merge rule '{instruction}' is invalid."));
            }
            else if (!firstRow.ContainsKey(instruction))
            {
                issues.Add(new(instruction,
                    $"Merge field '{instruction}' is not in the recipient data source."));
            }
        }

        for (var index = 0; index < rows.Count; index++)
        {
            try
            {
                MailMerge.MergeRecordWithRules(template, rows[index], new MergeState(), index + 1);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                issues.Add(new($"Record {index + 1}",
                    $"Record {index + 1} could not be merged: {exception.Message}"));
            }
        }

        var distinct = issues.DistinctBy(issue => (issue.Instruction, issue.Message)).ToList();
        var shouldComplete = mode != MailMergeCheckForErrorsMode.SimulateAndReport;
        return new(mode, rows.Count, distinct, shouldComplete);
    }

    public static TextDocument BuildReportDocument(MailMergeErrorCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var report = TextDocument.CreateEmpty();
        report.Blocks.Clear();
        report.Properties.Title = "Mail Merge Error Report";
        report.Blocks.Add(new Paragraph("Mail Merge Error Report") { StyleId = "Title" });
        report.Blocks.Add(new Paragraph($"Records checked: {result.RecordsChecked}"));

        if (!result.HasErrors)
        {
            report.Blocks.Add(new Paragraph("No mail merge errors were found."));
            return report;
        }

        report.Blocks.Add(new Paragraph($"Errors found: {result.Issues.Count}") { StyleId = "Heading1" });
        for (var index = 0; index < result.Issues.Count; index++)
        {
            var issue = result.Issues[index];
            report.Blocks.Add(new Paragraph($"Error {index + 1}: {issue.Message}"));
            report.Blocks.Add(new Paragraph($"Instruction: {issue.Instruction}"));
        }

        return report;
    }

    private static void AddInstructions(string text, ICollection<string> instructions)
    {
        foreach (var instruction in MailMerge.FieldNames(text))
            instructions.Add(instruction);
    }

    private static void AddInstructions(
        SectionHeadersFooters headersFooters,
        ICollection<string> instructions)
    {
        foreach (var story in new[]
                 {
                     headersFooters.Header,
                     headersFooters.Footer,
                     headersFooters.EvenHeader,
                     headersFooters.EvenFooter,
                     headersFooters.FirstHeader,
                     headersFooters.FirstFooter
                 })
        {
            if (story is not null)
                AddInstructions(story.PlainText, instructions);
        }
    }

    private static bool IsSpecialInstruction(string instruction) =>
        instruction.Equals(MailMerge.NextRecordField, StringComparison.OrdinalIgnoreCase)
        || instruction.Equals(MailMerge.MergeRecordNumberField, StringComparison.OrdinalIgnoreCase)
        || instruction.Equals(MailMerge.MergeSequenceNumberField, StringComparison.OrdinalIgnoreCase)
        || instruction.Equals("AddressBlock", StringComparison.OrdinalIgnoreCase)
        || instruction.Equals("GreetingLine", StringComparison.OrdinalIgnoreCase);
}
