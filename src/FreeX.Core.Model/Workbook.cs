namespace FreeX.Core.Model;

public sealed class WorkbookFileSharingModel
{
    public bool? ReadOnlyRecommended { get; set; }
    public string? UserName { get; set; }
    public string? ReservationPassword { get; set; }
}

public sealed class WorkbookFileRecoveryPropertiesModel
{
    public bool? AutoRecover { get; set; }
    public bool? CrashSave { get; set; }
    public bool? DataExtractLoad { get; set; }
    public bool? RepairLoad { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorkbookFileVersionModel
{
    public string? AppName { get; set; }
    public string? LastEdited { get; set; }
    public string? LowestEdited { get; set; }
    public string? RupBuild { get; set; }
    public string? CodeName { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorkbookCountrySettingsModel
{
    public int? DefaultCountryId { get; set; }
    public int? CurrentCountryId { get; set; }
}

public sealed class WorkbookLegacyMenuSettingsModel
{
    public int? AddMenuCount { get; set; }
    public int? DeleteMenuCount { get; set; }
}

public sealed class WorkbookLegacyWorkbookSettingsModel
{
    public List<int> SheetTabIds { get; set; } = [];
    public bool? UseNaturalLanguageFormulas { get; set; }
}

// WorkbookPropertiesModel and WorkbookProtectionMetadataModel were simple bags of
// NativeAttributes + NativeChildXmls with no behaviour.
// They have been consolidated into NativeXmlPreserveBag.

public sealed class WorkbookFunctionGroupsModel
{
    public string? BuiltInGroupCount { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorkbookFunctionGroupModel> Groups { get; set; } = [];
}

public sealed class WorkbookFunctionGroupModel
{
    public string? Name { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorkbookSmartTagMetadataModel
{
    public bool? Embed { get; set; }
    public string? Show { get; set; }
    public Dictionary<string, string> PropertiesNativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> TypesNativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorkbookSmartTagTypeModel> Types { get; set; } = [];
}

public sealed class WorkbookSmartTagTypeModel
{
    public string? NamespaceUri { get; set; }
    public string? Name { get; set; }
    public string? Url { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorkbookAdditionalViewsModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorkbookAdditionalViewModel> Views { get; set; } = [];
}

public sealed class WorkbookAdditionalViewModel
{
    public string? NativeXml { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Represents a workbook containing one or more worksheets.
/// This is the top-level domain object.
/// </summary>
public sealed class Workbook
{
    private static readonly char[] InvalidSheetNameChars = [':', '\\', '/', '?', '*', '[', ']'];
    private readonly List<Sheet> _sheets = [];
    private readonly Dictionary<SheetId, Sheet> _sheetById = [];
    private readonly List<CellStyle> _styles = [CellStyle.Default];
    private readonly Dictionary<CellStyle, int> _styleIndex = new() { [CellStyle.Default] = 0 };

    // Sheet-scoped defined names: keyed by (name, sheetId). Only populated when a name has
    // explicit sheet scope (Excel "localSheetId"). Workbook-scoped names go in NamedRanges /
    // NamedFormulas as before. Resolution order: sheet-scoped first, then workbook-global.
    private Dictionary<(string Name, SheetId Sheet), GridRange>? _scopedNamedRanges;
    private Dictionary<(string Name, SheetId Sheet), NamedRangeMetadata>? _scopedNamedRangeMetadata;
    private Dictionary<(string Name, SheetId Sheet), string>? _scopedNamedFormulas;

    /// <summary>Unique identifier for this workbook instance.</summary>
    public WorkbookId Id { get; }

    /// <summary>Return whether a sheet name contains a character Excel does not allow.</summary>
    public static bool ContainsInvalidSheetNameCharacter(string name) => name.IndexOfAny(InvalidSheetNameChars) >= 0;

    /// <summary>File name or title of the workbook.</summary>
    public string Name { get; set; }

    /// <summary>
    /// Full on-disk path of the workbook's last saved or opened location, or <see langword="null"/>
    /// when the workbook has never been saved (a brand-new, in-memory-only workbook). Consumed by
    /// <c>CELL("filename")</c> to reproduce Excel's "drive:\path\[filename]sheetname" result. The
    /// host application's open/save code is responsible for setting this after an IO operation
    /// completes (mirroring how Excel updates the title bar / CELL("filename") on Save As).
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>All sheets in order.</summary>
    public IReadOnlyList<Sheet> Sheets => _sheets;

    /// <summary>Named ranges defined in this workbook (case-insensitive keys).</summary>
    public Dictionary<string, GridRange> NamedRanges { get; } =
        new Dictionary<string, GridRange>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Excel-style metadata for named ranges, keyed by defined name.</summary>
    public Dictionary<string, NamedRangeMetadata> NamedRangeMetadataByName { get; } =
        new Dictionary<string, NamedRangeMetadata>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Defined names whose "refers to" is a formula expression rather than a plain cell range.
    /// Keys are name strings (case-insensitive). Values are the raw refers-to formula text
    /// (without the leading '='). These are evaluated on-demand by the formula engine.
    /// </summary>
    public Dictionary<string, string> NamedFormulas { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pivot cache metadata loaded from XLSX packages.</summary>
    public List<PivotCacheModel> PivotCaches { get; } = [];

    /// <summary>Slicer metadata loaded from XLSX packages.</summary>
    public List<SlicerModel> Slicers { get; } = [];

    /// <summary>Timeline metadata loaded from XLSX packages.</summary>
    public List<TimelineModel> Timelines { get; } = [];

    /// <summary>
    /// External workbook link metadata loaded from XLSX packages: package path/target plus (when
    /// present in the source file) the linked workbook's cached sheet names, defined names, and
    /// cached cell values — see <see cref="ExternalLinkModel"/>.
    /// </summary>
    public List<ExternalLinkModel> ExternalLinks { get; } = [];

    /// <summary>Whether the loaded workbook package contained an xl/vbaProject.bin macro project.</summary>
    public bool HasVbaProjectPackage { get; set; }

    /// <summary>Custom PivotTable style metadata loaded from XLSX stylesheet tableStyle definitions.</summary>
    public List<PivotTableStyleModel> PivotTableStyles { get; } = [];

    /// <summary>Custom structured-table style metadata loaded from XLSX stylesheet tableStyle definitions.</summary>
    public List<StructuredTableStyleModel> StructuredTableStyles { get; } = [];

    /// <summary>
    /// R107-round2: the highest <see cref="StructuredTableModel.Id"/> ever handed out to a NEW table
    /// created in this workbook during THIS session (not persisted -- reset to 0 on load, matching
    /// StructuredTableModel.Id's own load-time-only persistence). Exists purely so a freshly-allocated
    /// table id is never reused after its table is removed: allocating a new id from "the current max
    /// id among LIVE tables" (the pre-existing scheme) silently reuses a freed id the instant the
    /// highest-numbered table is deleted and a new one is created, because the freed id no longer
    /// appears among any live table to raise that max. A stale, orphaned <see
    /// cref="PivotCacheModel.SourceTableId"/> or <see cref="SlicerModel.SourceTableId"/> that was
    /// deliberately pinned to the removed table's id (see
    /// CommandGuards.PinOrphanedPivotCacheSourceTableIds) would then collide with the new table and
    /// silently resolve to it, defeating the very id-based identity these fields exist to guarantee.
    /// Tracked here (never decremented, including on Undo of a table creation) instead of derived from
    /// live tables so an id, once handed out, is never handed out again for the lifetime of the
    /// in-memory workbook.
    ///
    /// R108: this property itself is never persisted (no field for it in NativeJsonAdapter's
    /// WorkbookDto, and no equivalent slot in XLSX), so it always resets to 0 across a save/reload.
    /// That is fine on its own -- <c>CreateStructuredTableCommand.NextTableId</c> (the sole allocator
    /// of new table ids, in FreeX.Core.Commands) also floors its result against every live <see
    /// cref="SlicerModel.SourceTableId"/> and <see cref="PivotCacheModel.SourceTableId"/>, so a
    /// dangling reference pinned to a freed id before save still blocks that id from being reissued
    /// after reload even though this counter comes back at 0 -- PROVIDED the dangling reference itself
    /// survived the round-trip.
    ///
    /// R109: <see cref="SlicerModel.SourceTableId"/> genuinely round-trips through both real XLSX (the
    /// x15:tableSlicerCache/@tableId attribute) and native-JSON, so the slicer vector is covered as
    /// r108 intended. <see cref="PivotCacheModel.SourceTableId"/> is different: r108's own comment here
    /// claimed it round-tripped through the native-JSON pivot-cache DTO, but that DTO never actually had
    /// a field for it -- the id was silently discarded on every native save (XLSX never carried it
    /// either; OOXML's pivotCacheDefinition only has a name-based worksheetSource, no id slot). r109
    /// added the missing field to the native-JSON DTO, so PivotCacheModel.SourceTableId now genuinely
    /// round-trips through native .fxl too. XLSX still has no schema-valid home for it (a custom
    /// extLst attribute was deliberately NOT invented here -- see the "never invent non-native OOXML"
    /// policy); a pivot cache reloaded from XLSX always comes back with SourceTableId null, which is
    /// safe rather than dangerous: PivotTableRefreshService.Refresh only ever sets SourceTableId to a
    /// CURRENTLY-LIVE table's actual id (via a name-based lookup) when it is null, so a null
    /// SourceTableId can never itself resolve back to a freed id -- there is nothing left for this
    /// watermark/floor scheme to protect on the XLSX pivot-cache path, because nothing durable dangles
    /// there in the first place.
    /// </summary>
    public int NextStructuredTableIdWatermark { get; set; }

    /// <summary>Workbook number-format catalog entries keyed by XLSX numFmtId.</summary>
    public Dictionary<int, string> NumberFormatCatalog { get; } = [];

    /// <summary>Saved workbook view snapshots, similar to Excel Custom Views.</summary>
    public List<WorkbookCustomView> CustomViews { get; } = [];

    /// <summary>Saved What-If Analysis scenarios.</summary>
    public List<WorkbookScenario> Scenarios { get; } = [];

    /// <summary>Cells tracked in the formulas Watch Window.</summary>
    public List<CellAddress> WatchedCells { get; } = [];

    /// <summary>Formula error codes disabled in Error Checking options.</summary>
    public HashSet<string> DisabledFormulaErrorCodes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Workbook calculation mode.</summary>
    public WorkbookCalculationMode CalculationMode { get; set; } = WorkbookCalculationMode.Automatic;

    /// <summary>Whether workbook date serials use Excel's 1904 date system.</summary>
    public bool Uses1904DateSystem { get; set; }

    /// <summary>Whether the workbook sheet-tab strip is visible in Excel.</summary>
    public bool? ShowSheetTabs { get; set; }

    /// <summary>Excel workbook-view sheet tab ratio. Null means Excel/default.</summary>
    public int? SheetTabRatio { get; set; }

    /// <summary>Zero-based index of the first sheet visible in the sheet-tab strip.</summary>
    public int? FirstVisibleSheetIndex { get; set; }

    /// <summary>Zero-based index of the active sheet recorded in the workbook view.</summary>
    public int? ActiveSheetIndex { get; set; }

    /// <summary>Whether Excel should fully recalculate the workbook when it is opened.</summary>
    public bool FullCalculationOnLoad { get; set; }

    /// <summary>Whether Excel should force a full calculation pass even if dependencies appear clean.</summary>
    public bool ForceFullCalculation { get; set; }

    /// <summary>Whether iterative calculation is enabled for circular formulas.</summary>
    public bool IterativeCalculation { get; set; }

    /// <summary>Maximum iterative-calculation passes. Null means Excel/default.</summary>
    public int? MaxCalculationIterations { get; set; }

    /// <summary>Maximum iterative-calculation change threshold. Null means Excel/default.</summary>
    public double? MaxCalculationChange { get; set; }

    /// <summary>
    /// Whether stored numeric values retain full internal precision (Excel default, true) or are
    /// permanently rounded to their displayed precision (Excel's File &gt; Options &gt; Advanced
    /// &gt; "Set precision as displayed", false). Corresponds to XLSX <c>calcPr/@fullPrecision</c>
    /// (attribute omitted/true means full precision; <c>fullPrecision="0"</c> means precision as
    /// displayed).
    /// </summary>
    public bool FullPrecision { get; set; } = true;

    /// <summary>Workbook-level theme definition for Excel-style theme colors, fonts, and effects.</summary>
    public WorkbookTheme Theme { get; set; } = WorkbookTheme.Office;

    /// <summary>Workbook-level indexed color overrides loaded from XLSX styles.xml.</summary>
    public WorkbookIndexedColorPalette IndexedColors { get; } = new();

    /// <summary>Excel workbook file-sharing/read-only recommendation metadata.</summary>
    public WorkbookFileSharingModel? FileSharing { get; set; }

    /// <summary>Excel workbook file recovery metadata records.</summary>
    public List<WorkbookFileRecoveryPropertiesModel> FileRecoveryProperties { get; } = [];

    /// <summary>Excel workbook file-version metadata.</summary>
    public WorkbookFileVersionModel? FileVersion { get; set; }

    /// <summary>Legacy BIFF workbook country/localization identifiers.</summary>
    public WorkbookCountrySettingsModel? CountrySettings { get; set; }

    /// <summary>Legacy BIFF workbook add/delete menu metadata.</summary>
    public WorkbookLegacyMenuSettingsModel? LegacyMenuSettings { get; set; }

    /// <summary>Legacy BIFF workbook compatibility metadata.</summary>
    public WorkbookLegacyWorkbookSettingsModel? LegacyWorkbookSettings { get; set; }

    /// <summary>Excel workbook property metadata loaded from XLSX workbookPr (residual native XML).</summary>
    public NativeXmlPreserveBag? Properties { get; set; }

    /// <summary>Excel workbook function-group metadata.</summary>
    public WorkbookFunctionGroupsModel? FunctionGroups { get; set; }

    /// <summary>Excel workbook smart-tag metadata.</summary>
    public WorkbookSmartTagMetadataModel? SmartTags { get; set; }

    /// <summary>Additional native Excel workbook-window view metadata loaded from XLSX.</summary>
    public WorkbookAdditionalViewsModel? AdditionalViews { get; set; }

    /// <summary>Last requested workbook-window arrangement.</summary>
    public WorkbookWindowArrangement WindowArrangement { get; set; } = WorkbookWindowArrangement.Tiled;

    /// <summary>True when workbook structure operations such as sheet add/delete/rename/move are protected.</summary>
    public bool IsStructureProtected { get; set; }

    /// <summary>Password hash/text for workbook structure protection. Null means no password required.</summary>
    public string? StructureProtectionPassword { get; set; }

    /// <summary>Native Excel workbook protection metadata not yet modeled as editable fields.</summary>
    public NativeXmlPreserveBag? ProtectionMetadata { get; set; }

    /// <summary>Define or replace a named range.</summary>
    public void DefineNamedRange(string name, GridRange range)
    {
        DefineNamedRange(name, range, null);
    }

    /// <summary>Define or replace a named range and its Excel-style metadata.</summary>
    public void DefineNamedRange(string name, GridRange range, NamedRangeMetadata? metadata)
    {
        var error = ValidateNamedRangeName(name);
        if (error is not null)
            throw new ArgumentException(error, nameof(name));

        // NamedRanges/NamedRangeMetadataByName use a case-insensitive comparer. .NET's
        // Dictionary<TKey,TValue> indexer-set, when an entry already exists under the comparer's
        // equality, only overwrites the VALUE and leaves the previously-stored KEY text untouched.
        // Without removing first, a case-only rename (e.g. "revenue" -> "Revenue") would silently
        // keep enumerating/displaying the old casing even though the caller asked for a rename.
        NamedRanges.Remove(name);
        NamedRanges[name] = range;

        NamedRangeMetadataByName.Remove(name);
        NamedRangeMetadataByName[name] = metadata ?? NamedRangeMetadata.WorkbookScope;

        // A defined name is unique per scope regardless of whether it resolves to a range or a
        // formula/constant expression (Excel ground truth: Name Manager never lets a range name
        // and a formula name coexist under the same text at the same scope). Without this,
        // NamedRanges[name] and NamedFormulas[name] could both exist simultaneously; the formula
        // evaluator always resolves a bare name via NamedRanges first (see
        // FormulaEvaluator.References.cs EvaluateNamedRange), so the stale NamedFormulas entry
        // would become permanently unreachable while still occupying the name -- silently
        // changing what every pre-existing formula referencing this name evaluates to, with the
        // old formula definition left dangling in the model. Defining the range explicitly
        // supersedes any previous formula-kind definition of the same name.
        NamedFormulas.Remove(name);
    }

    /// <summary>Remove a named range. Returns true if found and removed.</summary>
    public bool RemoveNamedRange(string name)
    {
        NamedRangeMetadataByName.Remove(name);
        return NamedRanges.Remove(name);
    }

    /// <summary>Try to get a named range. Returns false if not found.</summary>
    public bool TryGetNamedRange(string name, out GridRange range) =>
        NamedRanges.TryGetValue(name, out range);

    /// <summary>Try to get Excel-style metadata for a named range.</summary>
    public bool TryGetNamedRangeMetadata(string name, out NamedRangeMetadata metadata) =>
        NamedRangeMetadataByName.TryGetValue(name, out metadata!);

    // ── Sheet-scoped defined name API ─────────────────────────────────────────

    /// <summary>
    /// Sheet-scoped named ranges. Keyed by (name, sheetId). Only populated when a name has
    /// explicit sheet scope (XLSX localSheetId). Use <see cref="TryGetNamedRange(string,SheetId,out GridRange)"/>
    /// for sheet-scope-aware resolution; direct access is for serialization/inspection only.
    /// </summary>
    public IReadOnlyDictionary<(string Name, SheetId Sheet), GridRange> ScopedNamedRanges =>
        _scopedNamedRanges is not null
            ? _scopedNamedRanges
            : EmptyScopedRanges;

    /// <summary>
    /// Sheet-scoped named formulas. Keyed by (name, sheetId).
    /// </summary>
    public IReadOnlyDictionary<(string Name, SheetId Sheet), string> ScopedNamedFormulas =>
        _scopedNamedFormulas is not null
            ? _scopedNamedFormulas
            : EmptyScopedFormulas;

    private static readonly Dictionary<(string, SheetId), GridRange> EmptyScopedRanges =
        new(ScopedNameKeyComparer.Instance);
    private static readonly Dictionary<(string, SheetId), string> EmptyScopedFormulas =
        new(ScopedNameKeyComparer.Instance);

    /// <summary>
    /// Define or replace a sheet-scoped named range. Sheet-scoped names take precedence
    /// over a same-named workbook-global name when resolving formulas on that sheet.
    /// </summary>
    public void DefineNamedRange(string name, GridRange range, NamedRangeMetadata? metadata, SheetId scopeSheetId)
    {
        var error = ValidateNamedRangeName(name);
        if (error is not null)
            throw new ArgumentException(error, nameof(name));

        var key = (name, scopeSheetId);
        _scopedNamedRanges ??= new Dictionary<(string, SheetId), GridRange>(ScopedNameKeyComparer.Instance);
        _scopedNamedRanges[key] = range;
        _scopedNamedRangeMetadata ??= new Dictionary<(string, SheetId), NamedRangeMetadata>(ScopedNameKeyComparer.Instance);
        _scopedNamedRangeMetadata[key] = metadata ?? NamedRangeMetadata.WorkbookScope;

        // Same cross-kind-uniqueness invariant as the workbook-global overload above: a
        // sheet-scoped name cannot simultaneously be a range and a formula, or the formula
        // evaluator's range-first resolution (see IsSheetScopedName / EvaluateNamedRange in
        // FormulaEvaluator.References.cs) silently strands the formula definition unreachable.
        _scopedNamedFormulas?.Remove(key);
    }

    /// <summary>
    /// Define or replace a sheet-scoped named formula.
    /// </summary>
    public void DefineNamedFormula(string name, string formulaText, SheetId scopeSheetId)
    {
        var error = ValidateNamedRangeName(name);
        if (error is not null)
            throw new ArgumentException(error, nameof(name));

        var key = (name, scopeSheetId);
        _scopedNamedFormulas ??= new Dictionary<(string, SheetId), string>(ScopedNameKeyComparer.Instance);
        _scopedNamedFormulas[key] = formulaText;

        // Mirror image of the guard in DefineNamedRange(..., scopeSheetId): defining a name as a
        // formula supersedes any previous range-kind definition of the same (name, scope) key.
        if (_scopedNamedRanges is not null && _scopedNamedRanges.Remove(key))
            _scopedNamedRangeMetadata?.Remove(key);
    }

    /// <summary>
    /// Try to get a named range with sheet-scope-first precedence. When <paramref name="contextSheetId"/>
    /// is provided, a sheet-scoped name for that sheet takes priority over the workbook-global name.
    /// Returns false if neither a scoped nor a global name is found.
    /// </summary>
    public bool TryGetNamedRange(string name, SheetId contextSheetId, out GridRange range)
    {
        if (_scopedNamedRanges is not null &&
            _scopedNamedRanges.TryGetValue((name, contextSheetId), out range))
            return true;

        return NamedRanges.TryGetValue(name, out range);
    }

    /// <summary>
    /// Try to get a named formula text with sheet-scope-first precedence.
    /// Returns null when neither a scoped nor a global formula text is found.
    /// </summary>
    public string? TryGetNamedFormulaText(string name, SheetId contextSheetId)
    {
        if (_scopedNamedFormulas is not null &&
            _scopedNamedFormulas.TryGetValue((name, contextSheetId), out var scoped))
            return scoped;

        return NamedFormulas.TryGetValue(name, out var global) ? global : null;
    }

    /// <summary>
    /// Try to get Excel-style metadata for a sheet-scoped named range.
    /// Returns false (and WorkbookScope sentinel) when not found.
    /// </summary>
    public bool TryGetScopedNamedRangeMetadata(string name, SheetId scopeSheetId, out NamedRangeMetadata metadata)
    {
        if (_scopedNamedRangeMetadata is not null &&
            _scopedNamedRangeMetadata.TryGetValue((name, scopeSheetId), out metadata!))
            return true;

        metadata = NamedRangeMetadata.WorkbookScope;
        return false;
    }

    /// <summary>Remove a sheet-scoped named range. Returns true if found and removed.</summary>
    public bool RemoveScopedNamedRange(string name, SheetId scopeSheetId)
    {
        if (_scopedNamedRanges is null) return false;
        var key = (name, scopeSheetId);
        _scopedNamedRangeMetadata?.Remove(key);
        return _scopedNamedRanges.Remove(key);
    }

    /// <summary>Remove a workbook-global named formula. Returns true if found and removed.</summary>
    public bool RemoveNamedFormula(string name) => NamedFormulas.Remove(name);

    /// <summary>Remove a sheet-scoped named formula. Returns true if found and removed.</summary>
    public bool RemoveScopedNamedFormula(string name, SheetId scopeSheetId) =>
        _scopedNamedFormulas is not null && _scopedNamedFormulas.Remove((name, scopeSheetId));

    // ── Keyed equality for (string, SheetId) dictionary keys ─────────────────

    private sealed class ScopedNameKeyComparer : IEqualityComparer<(string Name, SheetId Sheet)>
    {
        public static readonly ScopedNameKeyComparer Instance = new();

        public bool Equals((string Name, SheetId Sheet) x, (string Name, SheetId Sheet) y) =>
            string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) && x.Sheet.Equals(y.Sheet);

        public int GetHashCode((string Name, SheetId Sheet) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name), obj.Sheet.GetHashCode());
    }

    public Workbook(string name = "Untitled")
    {
        Id = WorkbookId.New();
        Name = name;
    }

    public Workbook(string name, CellStyle defaultStyle)
        : this(name)
    {
        ArgumentNullException.ThrowIfNull(defaultStyle);

        var clone = defaultStyle.Clone();
        _styles[0] = clone;
        _styleIndex.Clear();
        _styleIndex[clone] = 0;
    }

    /// <summary>Add a new sheet with the given name. Returns the new sheet.</summary>
    public Sheet AddSheet(string name)
    {
        EnsureCanUseSheetName(name);
        var sheet = new Sheet(SheetId.New(), name);
        _sheets.Add(sheet);
        _sheetById[sheet.Id] = sheet;
        return sheet;
    }

    /// <summary>Insert a sheet at a specific position.</summary>
    public Sheet InsertSheet(int index, string name)
    {
        EnsureCanUseSheetName(name);
        var sheet = new Sheet(SheetId.New(), name);
        _sheets.Insert(index, sheet);
        _sheetById[sheet.Id] = sheet;
        return sheet;
    }

    /// <summary>Reinsert an existing sheet instance at a specific position.</summary>
    public void InsertSheet(int index, Sheet sheet)
    {
        EnsureCanUseSheetName(sheet.Name, sheet.Id);
        _sheets.Insert(index, sheet);
        _sheetById[sheet.Id] = sheet;
    }

    /// <summary>
    /// Return an XLSX-compatible structural validation error for a sheet name, or
    /// <see langword="null"/> when the name satisfies all structural constraints.
    /// This check does NOT test for duplicate names within any particular workbook.
    /// </summary>
    public static string? ValidateSheetNameStructure(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Sheet name is invalid: it cannot be blank.";

        if (name.Length > 31)
            return "Sheet name is invalid: it cannot exceed 31 characters.";

        if (ContainsInvalidSheetNameCharacter(name))
            return "Sheet name is invalid: it cannot contain : \\ / ? * [ or ].";

        if (name.StartsWith('\'') || name.EndsWith('\''))
            return "Sheet name is invalid: it cannot begin or end with an apostrophe.";

        return null;
    }

    /// <summary>Return an XLSX-compatible validation error for a sheet name, or null when valid.</summary>
    public string? ValidateSheetName(string name, SheetId? exceptSheetId = null)
    {
        var structuralError = ValidateSheetNameStructure(name);
        if (structuralError is not null)
            return structuralError;

        if (_sheets.Any(s => s.Id != exceptSheetId &&
                             string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            return $"A sheet named '{name}' already exists.";

        return null;
    }

    private void EnsureCanUseSheetName(string name, SheetId? exceptSheetId = null)
    {
        var error = ValidateSheetName(name, exceptSheetId);
        if (error is not null)
            throw new ArgumentException(error, nameof(name));
    }

    /// <summary>Return an XLSX-compatible validation error for a named range name, or null when valid.</summary>
    public string? ValidateNamedRangeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Named range name is invalid: it cannot be blank.";

        if (name.Length > 255)
            return "Named range name is invalid: it cannot exceed 255 characters.";

        if (!IsValidNamedRangeStart(name[0]) || name.Skip(1).Any(ch => !IsValidNamedRangeChar(ch)))
            return "Named range name is invalid: use letters, numbers, underscores, and periods; start with a letter or underscore (or a backslash, for legacy macro-name compatibility).";

        if (IsReservedToken(name))
            return "Named range name is invalid: 'C', 'c', 'R', and 'r' are reserved single-letter names.";

        if (HasReservedExcelPrefix(name))
            return "Named range name is invalid: names starting with '_xlnm.' or '_xlchart.' are reserved for Excel's built-in defined names.";

        if (CellAddress.TryParse(name, SheetId.New(), out _) || IsR1C1Reference(name))
            return "Named range name is invalid: it cannot look like a cell reference.";

        return null;
    }

    // Excel reserves the "_xlnm." prefix for its own built-in defined names (Print_Area,
    // Print_Titles, _FilterDatabase, Criteria, Database, Extract, Consolidate_Area, etc. — see
    // ECMA-376 ST_DefinedNames) and "_xlchart." for chart-sheet-scoped built-ins; the New Name /
    // Name Manager dialogs refuse to let a user create an ordinary name that impersonates that
    // namespace. FreeX.Core.IO's XlsxNamedRangeMapper.IsExcelReservedDefinedName treats ANY name
    // with either prefix as reserved/Excel-internal at IO time and unconditionally skips emitting
    // a <definedName> element for it on save (and skips loading one on read) — so without this
    // guard here, a name like "_xlnm.Foo" could be created and used live in formulas but would be
    // silently and permanently dropped on the very next save. Matches
    // DefinedNameValidator.Validate used by the Avalonia shell.
    private static bool HasReservedExcelPrefix(string name) =>
        name.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("_xlchart.", StringComparison.OrdinalIgnoreCase);

    // Excel reserves the single-letter names "C"/"c" (current column) and "R"/"r" (current row)
    // as defined-name identifiers; they cannot be used even though they otherwise satisfy the
    // structural naming rules. Matches DefinedNameValidator.IsReservedToken used by the Avalonia shell.
    private static bool IsReservedToken(string name) =>
        name.Length == 1 && (name[0] is 'R' or 'r' or 'C' or 'c');

    // Excel allows a defined name's first character to be a letter, underscore, or
    // backslash ('\') — the backslash form exists for Lotus 1-2-3 macro-key compatibility
    // (e.g. "\P") and still appears in real-world xls->xlsx converted workbooks. A backslash
    // is only valid as the leading character, never elsewhere in the name.
    private static bool IsValidNamedRangeStart(char ch) =>
        char.IsLetter(ch) || ch == '_' || ch == '\\';

    private static bool IsValidNamedRangeChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_' || ch == '.';

    private static bool IsR1C1Reference(string name)
    {
        if (name.Length < 4 || char.ToUpperInvariant(name[0]) != 'R')
            return false;

        var cIndex = name.IndexOf("C", 1, StringComparison.OrdinalIgnoreCase);
        if (cIndex <= 1 || cIndex == name.Length - 1)
            return false;

        return uint.TryParse(name[1..cIndex], out var row) &&
               uint.TryParse(name[(cIndex + 1)..], out var col) &&
               row is >= 1 and <= CellAddress.MaxRow &&
               col is >= 1 and <= CellAddress.MaxCol;
    }

    /// <summary>Remove a sheet by its ID. Returns true if found and removed.</summary>
    public bool RemoveSheet(SheetId sheetId)
    {
        var idx = FindSheetIndex(sheetId);
        if (idx < 0) return false;
        _sheets.RemoveAt(idx);
        _sheetById.Remove(sheetId);
        RemoveNamedRangesForSheet(sheetId);
        AdjustWorkbookViewSheetIndexes(idx);
        return true;
    }

    private int FindSheetIndex(SheetId sheetId)
    {
        for (var index = 0; index < _sheets.Count; index++)
        {
            if (_sheets[index].Id == sheetId)
                return index;
        }

        return -1;
    }

    private void RemoveNamedRangesForSheet(SheetId sheetId)
    {
        foreach (var (name, range) in NamedRanges.ToList())
        {
            if (range.Start.Sheet == sheetId || range.End.Sheet == sheetId)
            {
                // Real Excel does not delete a defined name when the sheet it refers to is
                // removed — it keeps the name in the Name Manager and rewrites RefersTo to
                // "#REF!" so it remains visible/repairable (e.g. via the "Names with Errors"
                // filter). Mirror the same #REF! conversion already used for row/column-shift
                // deletions that fully consume a name's range (see
                // RowColumnShiftHelpers.NamedRanges.ConvertNamedRangeToRefError) instead of
                // dropping the dictionary entry outright.
                //
                // Deliberately do NOT call RemoveNamedRange here: it also removes the entry
                // from NamedRangeMetadataByName, which would permanently discard the name's
                // Hidden flag and Comment. Excel preserves every Name-Manager property except
                // the range text when a referenced sheet is deleted, so only the NamedRanges
                // entry is dropped — the metadata stays keyed by name and is picked up by
                // XlsxNamedRangeMapper for the resulting NamedFormulas-backed "#REF!" entry.
                NamedRanges.Remove(name);
                NamedFormulas[name] = "#REF!";
            }
        }

        // Sheet-scoped names: a name whose SCOPE is the deleted sheet has no sheet left to be
        // scoped to and is removed entirely (Excel drops a sheet-local name when its own sheet
        // is deleted). A name scoped to a different, surviving sheet whose TARGET range points
        // at the deleted sheet (e.g. a Sheet1-scoped name referring to Sheet2!$A$1, with Sheet2
        // being deleted) keeps its Name Manager entry, converted to a "#REF!" formula the same
        // way as the workbook-global case above.
        if (_scopedNamedRanges is not null)
        {
            foreach (var (key, scopedRange) in _scopedNamedRanges.ToList())
            {
                if (key.Sheet == sheetId)
                {
                    _scopedNamedRanges.Remove(key);
                    _scopedNamedRangeMetadata?.Remove(key);
                }
                else if (scopedRange.Start.Sheet == sheetId || scopedRange.End.Sheet == sheetId)
                {
                    // As above: preserve the scoped name's Hidden/Comment metadata across the
                    // #REF! conversion by removing only the range entry, not the metadata
                    // entry keyed by (name, scope sheet). RemoveScopedNamedRange would drop both.
                    _scopedNamedRanges.Remove(key);
                    DefineNamedFormula(key.Name, "#REF!", key.Sheet);
                }
            }
        }

        if (_scopedNamedFormulas is not null)
        {
            foreach (var key in _scopedNamedFormulas.Keys.Where(k => k.Sheet == sheetId).ToList())
                _scopedNamedFormulas.Remove(key);
        }
    }

    private void AdjustWorkbookViewSheetIndexes(int removedIndex)
    {
        ActiveSheetIndex = AdjustSheetIndexAfterRemoval(ActiveSheetIndex, removedIndex);
        FirstVisibleSheetIndex = AdjustSheetIndexAfterRemoval(FirstVisibleSheetIndex, removedIndex);
    }

    private int? AdjustSheetIndexAfterRemoval(int? sheetIndex, int removedIndex)
    {
        if (sheetIndex is null)
            return null;

        if (_sheets.Count == 0)
            return null;

        if (sheetIndex.Value > removedIndex)
            return Math.Min(sheetIndex.Value - 1, _sheets.Count - 1);

        if (sheetIndex.Value == removedIndex)
            return Math.Min(removedIndex, _sheets.Count - 1);

        return Math.Min(sheetIndex.Value, _sheets.Count - 1);
    }

    /// <summary>Get a sheet by ID, or null if not found.</summary>
    public Sheet? GetSheet(SheetId sheetId)
    {
        _sheetById.TryGetValue(sheetId, out var sheet);
        return sheet;
    }

    /// <summary>Get a sheet by name (case-insensitive), or null if not found.</summary>
    public Sheet? GetSheet(string name)
    {
        return _sheets.Find(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Get a sheet by 0-based index.</summary>
    public Sheet GetSheetAt(int index) => _sheets[index];

    /// <summary>Number of sheets.</summary>
    public int SheetCount => _sheets.Count;

    /// <summary>
    /// Register a style. If a structurally identical style already exists, returns its <see cref="StyleId"/>.
    /// Otherwise appends the style and returns a new <see cref="StyleId"/>.
    /// </summary>
    public StyleId RegisterStyle(CellStyle style)
    {
        if (_styleIndex.TryGetValue(style, out var idx))
            return new StyleId(idx);

        var clone = style.Clone();
        var newIdx = _styles.Count;
        _styles.Add(clone);
        _styleIndex[clone] = newIdx;
        return new StyleId(newIdx);
    }

    /// <summary>
    /// Get a style by id. Returns the default style if <paramref name="id"/> is out of range.
    /// The returned instance is a defensive copy so registered style keys remain immutable.
    /// </summary>
    public CellStyle GetStyle(StyleId id)
    {
        int idx = id.Value;
        return (idx >= 0 && idx < _styles.Count ? _styles[idx] : _styles[0]).Clone();
    }

    /// <summary>Total number of registered styles.</summary>
    public int StyleCount => _styles.Count;

    /// <summary>Reorder a sheet from one position to another.</summary>
    public void MoveSheet(int fromIndex, int toIndex)
    {
        var activeSheetId = GetSheetIdForWorkbookViewIndex(ActiveSheetIndex);
        var firstVisibleSheetId = GetSheetIdForWorkbookViewIndex(FirstVisibleSheetIndex);
        var sheet = _sheets[fromIndex];
        _sheets.RemoveAt(fromIndex);
        _sheets.Insert(toIndex, sheet);
        ActiveSheetIndex = GetWorkbookViewIndexForSheetId(activeSheetId);
        FirstVisibleSheetIndex = GetWorkbookViewIndexForSheetId(firstVisibleSheetId);
    }

    private SheetId? GetSheetIdForWorkbookViewIndex(int? sheetIndex)
    {
        if (sheetIndex is not { } index || index < 0 || index >= _sheets.Count)
            return null;

        return _sheets[index].Id;
    }

    private int? GetWorkbookViewIndexForSheetId(SheetId? sheetId)
    {
        if (sheetId is null)
            return null;

        var index = FindSheetIndex(sheetId.Value);
        return index < 0 ? null : index;
    }
}

public sealed record NamedRangeMetadata(string Scope, string Comment, bool Hidden = false)
{
    public static NamedRangeMetadata WorkbookScope { get; } = new("Workbook", "");
}

public sealed record WorkbookCustomView(
    string Name,
    IReadOnlyList<WorksheetCustomViewState> Sheets,
    string? Id = null,
    bool IncludePrintSettings = true,
    bool IncludeHiddenRowsColumnsAndFilterSettings = true,
    int? ActiveSheetIndex = null);

public sealed record WorkbookScenario(
    string Name,
    IReadOnlyList<ScenarioCellValue> ChangingCells,
    string? Comment = null,
    bool Hidden = false,
    bool Locked = false,
    string? User = null);

public sealed record ScenarioCellValue(CellAddress Address, ScalarValue Value);

public sealed record WorksheetCustomViewState(
    string SheetName,
    WorksheetViewMode ViewMode,
    uint FrozenRows,
    uint FrozenCols,
    uint? SplitRow,
    uint? SplitColumn,
    bool ShowGridlines = true,
    bool ShowHeadings = true,
    bool ShowRulers = true,
    int ZoomPercent = 100,
    bool ShowFormulas = false,
    uint? ActiveRow = null,
    uint? ActiveCol = null,
    uint? ViewTopRow = null,
    uint? ViewLeftCol = null,
    /// <summary>
    /// Rows hidden by the user (Sheet.HiddenRows) at capture time. Only populated/applied when the
    /// owning <see cref="WorkbookCustomView.IncludeHiddenRowsColumnsAndFilterSettings"/> is true;
    /// null when that option was off (nothing captured, applying the view leaves current
    /// hidden-row state untouched).
    /// </summary>
    IReadOnlyList<uint>? HiddenRows = null,
    /// <summary>Columns hidden by the user (Sheet.HiddenCols) at capture time. See <see cref="HiddenRows"/>.</summary>
    IReadOnlyList<uint>? HiddenCols = null,
    /// <summary>Rows hidden by an active AutoFilter (Sheet.FilterHiddenRows) at capture time. See <see cref="HiddenRows"/>.</summary>
    IReadOnlyList<uint>? FilterHiddenRows = null,
    /// <summary>Worksheet-level AutoFilter definition (Sheet.AutoFilter) at capture time. See <see cref="HiddenRows"/>.</summary>
    WorksheetAutoFilterModel? AutoFilter = null,
    /// <summary>
    /// Print areas (Sheet.PrintAreas) at capture time. Only populated/applied when the owning
    /// <see cref="WorkbookCustomView.IncludePrintSettings"/> is true; null when that option was off.
    /// An empty list means "no print area configured" (print the used range) as captured.
    /// </summary>
    IReadOnlyList<GridRange>? PrintAreas = null,
    /// <summary>Page orientation (Sheet.PageOrientation) at capture time. See <see cref="PrintAreas"/>.</summary>
    WorksheetPageOrientation? PageOrientation = null,
    /// <summary>Paper size (Sheet.PaperSize) at capture time. See <see cref="PrintAreas"/>.</summary>
    WorksheetPaperSize? PaperSize = null,
    /// <summary>Raw OOXML paper-size code (Sheet.PaperSizeCode) at capture time. See <see cref="PrintAreas"/>.</summary>
    int? PaperSizeCode = null,
    /// <summary>Page margins (Sheet.PageMargins) at capture time. See <see cref="PrintAreas"/>.</summary>
    WorksheetPageMargins? PageMargins = null,
    /// <summary>Header margin in inches (Sheet.HeaderMargin) at capture time. See <see cref="PrintAreas"/>.</summary>
    double? HeaderMargin = null,
    /// <summary>Footer margin in inches (Sheet.FooterMargin) at capture time. See <see cref="PrintAreas"/>.</summary>
    double? FooterMargin = null,
    /// <summary>Whether gridlines print (Sheet.PrintGridlines) at capture time. See <see cref="PrintAreas"/>.</summary>
    bool? PrintGridlines = null,
    /// <summary>Whether row/column headings print (Sheet.PrintHeadings) at capture time. See <see cref="PrintAreas"/>.</summary>
    bool? PrintHeadings = null,
    /// <summary>Print scaling (Sheet.ScaleToFit) at capture time. See <see cref="PrintAreas"/>.</summary>
    WorksheetScaleToFit? ScaleToFit = null,
    /// <summary>Fit-to-page flag (Sheet.FitToPage) at capture time. See <see cref="PrintAreas"/>.</summary>
    bool? FitToPage = null);
