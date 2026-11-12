using System.Globalization;
using FreeW.Core.IO.RecipientSources;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>One entry of the "Use an Existing List" file picker's type filter.</summary>
public sealed record RecipientSourceFileFilter(string Label, IReadOnlyList<string> Extensions)
{
    /// <summary>The semicolon-separated wildcard list file dialogs expect (<c>*.xlsx;*.xlsm</c>).</summary>
    public string Pattern => string.Join(";", Extensions.Select(extension => "*" + extension));
}

public sealed record RecipientSourceChoice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

/// <summary>A preview of the list as it will be loaded with the current import options.</summary>
public sealed record RecipientSourcePreview(
    IReadOnlyList<string> Header,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    int TotalRecords,
    string? ErrorMessage)
{
    public bool IsValid => ErrorMessage is null;

    public string Summary => IsValid
        ? MailMergeRecipientSourceText.PreviewCount(Rows.Count, TotalRecords)
        : ErrorMessage!;
}

/// <summary>
/// Word's Select Recipients &gt; Use an Existing List, after a file has been picked: which worksheet,
/// named range or table to read (workbooks), which delimiter (text files), whether the first row holds
/// the column headers, and a preview of the result. The file is read through delegates so the session
/// itself never touches the disk.
/// </summary>
public sealed class RecipientSourceImportSession
{
    public const int PreviewRowCount = 5;
    public const string TableAutomationId = "MailMergeSourceTableComboBox";
    public const string DelimiterAutomationId = "MailMergeSourceDelimiterComboBox";
    public const string FirstRowHeadersAutomationId = "MailMergeSourceFirstRowHeadersCheckBox";
    public const string PreviewAutomationId = "MailMergeSourcePreview";

    private readonly Func<RecipientSourceOptions, IReadOnlyList<IReadOnlyList<string>>> _readRows;
    private RecipientSourcePreview? _preview;
    private MergeData? _data;

    public RecipientSourceImportSession(
        string fileName,
        RecipientSourceInfo info,
        Func<RecipientSourceOptions, IReadOnlyList<IReadOnlyList<string>>> readRows)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(readRows);

        FileName = fileName;
        Info = info;
        _readRows = readRows;
        Tables = info.Tables
            .Select(table => new RecipientSourceChoice<string>(MailMergeRecipientSourceText.TableName(table), table.Key))
            .ToArray();
        Delimiters = MergeRecipientTable.SupportedDelimiters
            .Select(delimiter => new RecipientSourceChoice<char>(
                MailMergeRecipientSourceText.DelimiterName(delimiter),
                delimiter))
            .ToArray();
        SelectedTableIndex = Tables.Count > 0 ? 0 : -1;
        SelectedDelimiterIndex = Math.Max(0, IndexOfDelimiter(info.Delimiter));
    }

    /// <summary>Opens the session for a file on disk; throws <see cref="RecipientSourceException"/> when it cannot be used.</summary>
    public static RecipientSourceImportSession Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var info = RecipientListFile.Inspect(path);
        return new RecipientSourceImportSession(
            Path.GetFileName(path),
            info,
            options => RecipientListFile.ReadRows(path, options));
    }

    public string FileName { get; }

    public RecipientSourceInfo Info { get; }

    public string Title => MailMergeRecipientSourceText.ImportTitle;

    public string FileLabel => MailMergeRecipientSourceText.FileLabel(FileName);

    public IReadOnlyList<RecipientSourceChoice<string>> Tables { get; }

    public IReadOnlyList<RecipientSourceChoice<char>> Delimiters { get; }

    public bool ShowsTables => Info.Kind == RecipientSourceKind.Workbook;

    public bool ShowsDelimiters => Info.Kind == RecipientSourceKind.DelimitedText;

    /// <summary>The detected text encoding, or the first-table note for a document source.</summary>
    public string? SourceNote => Info.Kind switch
    {
        RecipientSourceKind.DelimitedText when Info.EncodingName.Length > 0 =>
            MailMergeRecipientSourceText.Encoding(Info.EncodingName),
        RecipientSourceKind.Document => MailMergeRecipientSourceText.DocumentHint,
        _ => null,
    };

    public int SelectedTableIndex { get; private set; }

    public int SelectedDelimiterIndex { get; private set; }

    public bool FirstRowIsHeader { get; private set; } = true;

    public RecipientSourceOptions Options => new(
        ShowsTables && SelectedTableIndex >= 0 ? Tables[SelectedTableIndex].Value : null,
        FirstRowIsHeader,
        ShowsDelimiters ? Delimiters[SelectedDelimiterIndex].Value : null);

    public void SelectTable(int index)
    {
        if (index < 0 || index >= Tables.Count || index == SelectedTableIndex)
            return;
        SelectedTableIndex = index;
        Invalidate();
    }

    public void SelectDelimiter(int index)
    {
        if (index < 0 || index >= Delimiters.Count || index == SelectedDelimiterIndex)
            return;
        SelectedDelimiterIndex = index;
        Invalidate();
    }

    public void SetFirstRowIsHeader(bool value)
    {
        if (value == FirstRowIsHeader)
            return;
        FirstRowIsHeader = value;
        Invalidate();
    }

    /// <summary>The list as the current options would load it (read once per option change).</summary>
    public RecipientSourcePreview Preview
    {
        get
        {
            if (_preview is not null)
                return _preview;

            try
            {
                var data = MergeRecipientTable.FromTable(_readRows(Options), FirstRowIsHeader);
                if (data.Header.Count == 0)
                    throw new RecipientSourceException(RecipientSourceError.Empty);
                _data = data;
                _preview = new RecipientSourcePreview(
                    data.Header,
                    Enumerable.Range(0, Math.Min(PreviewRowCount, data.Count))
                        .Select(index => MergeRecipientTable.RowValues(data, index))
                        .ToArray(),
                    data.Count,
                    ErrorMessage: null);
            }
            catch (RecipientSourceException exception)
            {
                _data = null;
                _preview = new RecipientSourcePreview(
                    [],
                    [],
                    0,
                    MailMergeRecipientSourceText.ErrorMessage(exception.Error));
            }

            return _preview;
        }
    }

    public bool CanAccept => Preview.IsValid;

    /// <summary>The recipient list to load, or null when the current options do not yield one.</summary>
    public MergeData? Accept() => Preview.IsValid ? _data : null;

    private void Invalidate()
    {
        _preview = null;
        _data = null;
    }

    private int IndexOfDelimiter(char delimiter)
    {
        for (var index = 0; index < Delimiters.Count; index++)
        {
            if (Delimiters[index].Value == delimiter)
                return index;
        }
        return -1;
    }
}

/// <summary>Plans the "Use an Existing List" file picker and the "Type a New List" save dialog.</summary>
public static class MailMergeRecipientSourcePlanner
{
    public static IReadOnlyList<RecipientSourceFileFilter> OpenFilters { get; } =
    [
        new(
            MailMergeRecipientSourceText.FilterAll,
            [
                .. RecipientListFile.WorkbookExtensions,
                .. RecipientListFile.DelimitedTextExtensions,
                .. RecipientListFile.DocumentExtensions,
            ]),
        new(MailMergeRecipientSourceText.FilterExcel, RecipientListFile.WorkbookExtensions),
        new(MailMergeRecipientSourceText.FilterText, RecipientListFile.DelimitedTextExtensions),
        new(MailMergeRecipientSourceText.FilterWord, RecipientListFile.DocumentExtensions),
    ];

    public static IReadOnlyList<RecipientSourceFileFilter> SaveFilters { get; } =
    [
        new(MailMergeRecipientSourceText.SaveFilterCsv, [".csv"]),
        new(MailMergeRecipientSourceText.SaveFilterText, [".txt", ".tsv"]),
    ];

    /// <summary>The <c>Label|pattern|Label|pattern</c> string WPF and Win32 file dialogs take.</summary>
    public static string ToDialogFilter(IReadOnlyList<RecipientSourceFileFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        return string.Join("|", filters.Select(filter => $"{filter.Label} ({filter.Pattern})|{filter.Pattern}"));
    }

    /// <summary>
    /// The message for a recipient source that could not be used. Known failures get their localized
    /// explanation; an unexpected one is reported as unreadable rather than leaking an exception.
    /// </summary>
    public static string DescribeFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is RecipientSourceException source
            ? MailMergeRecipientSourceText.ErrorMessage(source.Error)
            : MailMergeRecipientSourceText.ErrorMessage(RecipientSourceError.Unreadable);
    }
}

public enum RecipientListEditorError
{
    EmptyColumnName,
    DuplicateColumnName,
    LastColumn,
}

public sealed record RecipientListEditorResult(bool Success, RecipientListEditorError? Error, string? Message)
{
    public static RecipientListEditorResult Ok { get; } = new(true, null, null);

    public static RecipientListEditorResult Fail(RecipientListEditorError error, string message) =>
        new(false, error, message);
}

/// <summary>
/// Word's Select Recipients &gt; Type a New List (New Address List): an editable grid of entries with
/// customizable columns. It starts from Word's standard address fields, or from an existing list when
/// the current recipients are being edited.
/// </summary>
public sealed class RecipientListEditorSession
{
    public const string GridAutomationId = "MailMergeNewListGrid";
    public const string NewEntryAutomationId = "MailMergeNewListNewEntryButton";
    public const string DeleteEntryAutomationId = "MailMergeNewListDeleteEntryButton";
    public const string AddColumnAutomationId = "MailMergeNewListAddColumnButton";
    public const string RemoveColumnAutomationId = "MailMergeNewListRemoveColumnButton";
    public const string RenameColumnAutomationId = "MailMergeNewListRenameColumnButton";

    /// <summary>
    /// The fields of Word's New Address List. They are data (merge-field names that Match Fields and the
    /// Address Block / Greeting Line recognize), so they are not translated.
    /// </summary>
    public static IReadOnlyList<string> DefaultColumns { get; } =
    [
        "Title", "First Name", "Last Name", "Company Name", "Address Line 1", "Address Line 2", "City",
        "State", "ZIP Code", "Country or Region", "Home Phone", "Work Phone", "E-mail Address",
    ];

    private readonly List<string> _columns;
    private readonly List<List<string>> _rows;

    public RecipientListEditorSession(MergeData? existing = null)
    {
        if (existing is { Header.Count: > 0 })
        {
            _columns = [.. existing.Header];
            _rows = Enumerable.Range(0, existing.Count)
                .Select(index => MergeRecipientTable.RowValues(existing, index).ToList())
                .ToList();
        }
        else
        {
            _columns = [.. DefaultColumns];
            _rows = [];
        }

        if (_rows.Count == 0)
            AddRow();
    }

    public string Title => MailMergeRecipientSourceText.NewListTitle;

    public IReadOnlyList<string> Columns => _columns;

    public int RowCount => _rows.Count;

    public IReadOnlyList<string> Row(int rowIndex) => _rows[rowIndex];

    public string Cell(int rowIndex, int columnIndex) =>
        IsCell(rowIndex, columnIndex) ? _rows[rowIndex][columnIndex] : string.Empty;

    public void SetCell(int rowIndex, int columnIndex, string? value)
    {
        if (IsCell(rowIndex, columnIndex))
            _rows[rowIndex][columnIndex] = value ?? string.Empty;
    }

    /// <summary>Adds an empty entry and returns its index.</summary>
    public int AddRow()
    {
        _rows.Add(Enumerable.Repeat(string.Empty, _columns.Count).ToList());
        return _rows.Count - 1;
    }

    /// <summary>
    /// Deletes an entry. The grid always keeps one entry to type into, so deleting the last one clears it.
    /// </summary>
    public void RemoveRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rows.Count)
            return;
        _rows.RemoveAt(rowIndex);
        if (_rows.Count == 0)
            AddRow();
    }

    public RecipientListEditorResult AddColumn(string? name)
    {
        if (ValidateColumnName(name, ignoreIndex: -1) is { } failure)
            return failure;
        _columns.Add(name!.Trim());
        foreach (var row in _rows)
            row.Add(string.Empty);
        return RecipientListEditorResult.Ok;
    }

    public RecipientListEditorResult RenameColumn(int columnIndex, string? name)
    {
        if (columnIndex < 0 || columnIndex >= _columns.Count)
            return RecipientListEditorResult.Ok;
        if (ValidateColumnName(name, columnIndex) is { } failure)
            return failure;
        _columns[columnIndex] = name!.Trim();
        return RecipientListEditorResult.Ok;
    }

    public RecipientListEditorResult RemoveColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _columns.Count)
            return RecipientListEditorResult.Ok;
        if (_columns.Count == 1)
        {
            return RecipientListEditorResult.Fail(
                RecipientListEditorError.LastColumn,
                MailMergeRecipientSourceText.LastColumnError);
        }

        _columns.RemoveAt(columnIndex);
        foreach (var row in _rows)
            row.RemoveAt(columnIndex);
        return RecipientListEditorResult.Ok;
    }

    /// <summary>
    /// The typed list as merge data. Entries left completely blank are not recipients and are dropped.
    /// </summary>
    public MergeData Build()
    {
        return new MergeData(
            _columns.ToArray(),
            _rows
                .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                .Select(row => (IReadOnlyList<string>)row.ToArray())
                .ToArray());
    }

    private RecipientListEditorResult? ValidateColumnName(string? name, int ignoreIndex)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return RecipientListEditorResult.Fail(
                RecipientListEditorError.EmptyColumnName,
                MailMergeRecipientSourceText.ColumnNameError);
        }

        for (var index = 0; index < _columns.Count; index++)
        {
            // Merge fields are matched ignoring case, so two columns may not differ only by case.
            if (index != ignoreIndex
                && string.Equals(_columns[index], trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return RecipientListEditorResult.Fail(
                    RecipientListEditorError.DuplicateColumnName,
                    MailMergeRecipientSourceText.DuplicateColumnError(trimmed));
            }
        }

        return null;
    }

    private bool IsCell(int rowIndex, int columnIndex) =>
        rowIndex >= 0 && rowIndex < _rows.Count && columnIndex >= 0 && columnIndex < _columns.Count;
}

public enum RecipientFilterComparison
{
    Equals,
    NotEquals,
    Contains,
    NotContains,
    IsBlank,
    IsNotBlank,
}

/// <summary>A recipient shown in Edit Recipient List.</summary>
/// <param name="SourceIndex">The record's position in the list as it was loaded.</param>
public sealed record RecipientListRow(int SourceIndex, bool IsIncluded, IReadOnlyList<string> Values);

/// <summary>
/// Word's Edit Recipient List (Mail Merge Recipients): every record of the loaded list with a check box
/// that includes or excludes it, a sort, and a simple filter. Unlike the older Filter &amp; Sort command
/// it works on the full list as loaded, so an excluded recipient can be included again later.
/// </summary>
public sealed class RecipientListSelectionSession
{
    public const string GridAutomationId = "MailMergeEditListGrid";
    public const string SortColumnAutomationId = "MailMergeEditListSortColumnComboBox";
    public const string SortDirectionAutomationId = "MailMergeEditListSortDirectionComboBox";
    public const string FilterColumnAutomationId = "MailMergeEditListFilterColumnComboBox";
    public const string FilterComparisonAutomationId = "MailMergeEditListFilterComparisonComboBox";
    public const string FilterValueAutomationId = "MailMergeEditListFilterValueTextBox";
    public const string SelectAllAutomationId = "MailMergeEditListSelectAllButton";
    public const string ClearAllAutomationId = "MailMergeEditListClearAllButton";
    public const string ClearFilterAutomationId = "MailMergeEditListClearFilterButton";

    private readonly MergeData _source;
    private readonly bool[] _included;
    private readonly List<int> _order;
    private int _filterColumn = -1;
    private RecipientFilterComparison _filterComparison = RecipientFilterComparison.Contains;
    private string _filterValue = string.Empty;

    /// <param name="source">The full recipient list as loaded.</param>
    /// <param name="activeSourceIndexes">
    /// The source positions of the records currently in the merge, in merge order. They are listed
    /// first and checked; every other record follows, unchecked, in source order.
    /// </param>
    public RecipientListSelectionSession(MergeData source, IEnumerable<int> activeSourceIndexes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(activeSourceIndexes);

        _source = source;
        _included = new bool[source.Count];
        _order = new List<int>(source.Count);
        foreach (var index in activeSourceIndexes)
        {
            if (index < 0 || index >= source.Count || _included[index])
                continue;
            _included[index] = true;
            _order.Add(index);
        }
        for (var index = 0; index < source.Count; index++)
        {
            if (!_included[index])
                _order.Add(index);
        }
    }

    public string Title => MailMergeRecipientSourceText.EditListTitle;

    public IReadOnlyList<string> Columns => _source.Header;

    public IReadOnlyList<RecipientSourceChoice<int>> SortColumns =>
    [
        new(MailMergeRecipientSourceText.SortNone, -1),
        .. _source.Header.Select((name, index) => new RecipientSourceChoice<int>(name, index)),
    ];

    public IReadOnlyList<RecipientSourceChoice<bool>> SortDirections { get; } =
    [
        new(MailMergeRecipientSourceText.SortAscending, true),
        new(MailMergeRecipientSourceText.SortDescending, false),
    ];

    public IReadOnlyList<RecipientSourceChoice<int>> FilterColumns =>
    [
        new(MailMergeRecipientSourceText.FilterAllFields, -1),
        .. _source.Header.Select((name, index) => new RecipientSourceChoice<int>(name, index)),
    ];

    public IReadOnlyList<RecipientSourceChoice<RecipientFilterComparison>> FilterComparisons { get; } =
        Enum.GetValues<RecipientFilterComparison>()
            .Select(comparison => new RecipientSourceChoice<RecipientFilterComparison>(
                MailMergeRecipientSourceText.ComparisonName(comparison),
                comparison))
            .ToArray();

    public int FilterColumnIndex => _filterColumn + 1;

    public int FilterComparisonIndex => (int)_filterComparison;

    public string FilterValue => _filterValue;

    /// <summary>Whether the chosen comparison takes a value ("Is blank" / "Is not blank" do not).</summary>
    public bool FilterNeedsValue =>
        _filterComparison is not (RecipientFilterComparison.IsBlank or RecipientFilterComparison.IsNotBlank);

    public bool HasFilter => !FilterNeedsValue || _filterValue.Length > 0;

    /// <summary>The recipients that pass the filter, in the current sort order.</summary>
    public IReadOnlyList<RecipientListRow> VisibleRows =>
        _order
            .Where(PassesFilter)
            .Select(index => new RecipientListRow(index, _included[index], MergeRecipientTable.RowValues(_source, index)))
            .ToArray();

    public int IncludedCount => _order.Count(index => _included[index] && PassesFilter(index));

    public string Summary => MailMergeRecipientSourceText.Summary(IncludedCount, _source.Count);

    public void SetIncluded(int sourceIndex, bool included)
    {
        if (sourceIndex >= 0 && sourceIndex < _included.Length)
            _included[sourceIndex] = included;
    }

    /// <summary>Checks or clears every recipient the filter currently shows.</summary>
    public void SetAllVisibleIncluded(bool included)
    {
        foreach (var index in _order.Where(PassesFilter))
            _included[index] = included;
    }

    /// <summary>
    /// Sorts by one field. Text is compared the way the user's language sorts it, ignoring case; the
    /// sort is stable, so recipients with equal values keep their previous relative order.
    /// </summary>
    public void Sort(int columnIndex, bool ascending)
    {
        if (columnIndex < 0 || columnIndex >= _source.Header.Count)
            return;

        var name = _source.Header[columnIndex];
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        string Key(int index) => _source.Rows[index].TryGetValue(name, out var value) ? value : string.Empty;
        var sorted = ascending
            ? _order.OrderBy(Key, comparer).ToList()
            : _order.OrderByDescending(Key, comparer).ToList();
        _order.Clear();
        _order.AddRange(sorted);
    }

    /// <summary>Sets the filter. A column index of -1 tests every field of the record.</summary>
    public void SetFilter(int columnIndex, RecipientFilterComparison comparison, string? value)
    {
        _filterColumn = columnIndex >= 0 && columnIndex < _source.Header.Count ? columnIndex : -1;
        _filterComparison = Enum.IsDefined(comparison) ? comparison : RecipientFilterComparison.Contains;
        _filterValue = value?.Trim() ?? string.Empty;
    }

    public void ClearFilter() => SetFilter(-1, RecipientFilterComparison.Contains, string.Empty);

    /// <summary>
    /// The source positions of the recipients to merge: those that are checked and pass the filter, in
    /// the displayed order. A filtered-out recipient is not merged, as in Word.
    /// </summary>
    public IReadOnlyList<int> BuildSelection() =>
        _order.Where(index => _included[index] && PassesFilter(index)).ToArray();

    private bool PassesFilter(int sourceIndex)
    {
        if (!HasFilter)
            return true;

        var row = _source.Rows[sourceIndex];
        var values = _filterColumn >= 0
            ? [row.TryGetValue(_source.Header[_filterColumn], out var single) ? single : string.Empty]
            : _source.Header.Select(name => row.TryGetValue(name, out var value) ? value : string.Empty).ToArray();

        return _filterComparison switch
        {
            RecipientFilterComparison.Equals => values.Any(Matches),
            // "Not equal" / "does not contain" over any field means no field matches.
            RecipientFilterComparison.NotEquals => !values.Any(Matches),
            RecipientFilterComparison.Contains => values.Any(ContainsValue),
            RecipientFilterComparison.NotContains => !values.Any(ContainsValue),
            RecipientFilterComparison.IsBlank => _filterColumn >= 0
                ? string.IsNullOrWhiteSpace(values[0])
                : values.All(string.IsNullOrWhiteSpace),
            RecipientFilterComparison.IsNotBlank => _filterColumn >= 0
                ? !string.IsNullOrWhiteSpace(values[0])
                : values.Any(value => !string.IsNullOrWhiteSpace(value)),
            _ => true,
        };

        bool Matches(string value) =>
            string.Equals(value.Trim(), _filterValue, StringComparison.CurrentCultureIgnoreCase);

        bool ContainsValue(string value) =>
            value.Contains(_filterValue, StringComparison.CurrentCultureIgnoreCase);
    }
}
