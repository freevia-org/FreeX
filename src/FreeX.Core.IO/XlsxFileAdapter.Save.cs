using System.IO;
using System.Xml;
using ClosedXML.Excel;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    /// <summary>
    /// Saves a workbook to the given stream and returns any non-fatal warnings collected during
    /// the save (e.g. individual named ranges or data-validation rules that could not be serialized).
    /// The file is always written; warnings indicate partial data loss.
    /// </summary>
    public XlsxSaveResult SaveWithWarnings(Workbook workbook, Stream stream)
    {
        var warnings = new List<string>();
        SaveCore(workbook, stream, warnings, preserveVbaProject: false);
        return warnings.Count == 0 ? XlsxSaveResult.Clean : new XlsxSaveResult(warnings.AsReadOnly());
    }

    /// <inheritdoc/>
    public void Save(Workbook workbook, Stream stream)
    {
        SaveCore(workbook, stream, warnings: null, preserveVbaProject: false);
    }

    // R70-io-vba-6-1: internal entry point used ONLY by the macro-enabled save adapters
    // (XlsmFileAdapter, XltmFileAdapter). Those adapters build their .xlsm/.xltm package by
    // delegating to this adapter's own save pipeline and then flipping the workbook content-type
    // in a post-process step, so they need this save to PRESERVE a loaded workbook's
    // xl/vbaProject.bin (and its relationship/content-type) rather than drop it. Every other
    // caller of Save/SaveWithWarnings targets a plain, non-macro package (.xlsx/.xltx) and must
    // DROP the VBA project -- Excel does the same (with a user-facing warning) when a
    // macro-enabled workbook is saved as a plain format.
    internal void SavePreservingVbaProject(Workbook workbook, Stream stream)
    {
        SaveCore(workbook, stream, warnings: null, preserveVbaProject: true);
    }

    // R123-io-xlsm-save-warnings: the warnings-collecting counterpart to
    // SavePreservingVbaProject, used by XlsmFileAdapter/XltmFileAdapter so a per-item save
    // failure (a comment, hyperlink, merged region, named range, or data-validation rule that
    // could not be serialized) on a macro-enabled workbook/template is reported to the user the
    // same way it already is for a plain .xlsx save via SaveWithWarnings, instead of being
    // silently swallowed because the only entry point those adapters had access to
    // (SavePreservingVbaProject) always passed warnings: null.
    internal XlsxSaveResult SaveWithWarningsPreservingVbaProject(Workbook workbook, Stream stream)
    {
        var warnings = new List<string>();
        SaveCore(workbook, stream, warnings, preserveVbaProject: true);
        return warnings.Count == 0 ? XlsxSaveResult.Clean : new XlsxSaveResult(warnings.AsReadOnly());
    }

    private void SaveCore(Workbook workbook, Stream stream, List<string>? warnings, bool preserveVbaProject)
    {
        // Serialize with loads/other saves: the full-save path builds a ClosedXML XLWorkbook, which
        // shares process-global static state with the load path.  The cheap patch/source-copy paths
        // don't touch ClosedXML, but gating the whole method keeps the rule simple and the cost is
        // negligible (saves are user-initiated and brief on the patch path).  See ClosedXmlGate.
        lock (ClosedXmlGate)
        {
            SaveCoreUnlocked(workbook, stream, warnings, preserveVbaProject);
        }
    }

    private void SaveCoreUnlocked(Workbook workbook, Stream stream, List<string>? warnings, bool preserveVbaProject)
    {
        LastSaveDiagnostics = XlsxSaveDiagnostics.NotRun;
        string? currentModelFingerprint = null;
        var hasSourcePackage = SourcePackages.TryGetValue(workbook, out var sourcePackage);

        // R70-io-vba-6-1: a save that must DROP the source's VBA project (macro-enabled source,
        // plain target) can never take either fast path below -- both replay the ORIGINAL source
        // package bytes verbatim (xl/vbaProject.bin, the macroEnabled content-type, and all) --
        // and must instead go through the full ClosedXML-rebuild + source-package-preservation
        // path further down, where PreserveSourcePackageParts excludes the VBA project's parts and
        // MergeContentTypes leaves the plain spreadsheetml content-type ClosedXML wrote in place.
        // Once that save completes, the new source-package snapshot it captures is VBA-free, so
        // subsequent unchanged saves of the same workbook safely resume using the fast paths.
        var mustDropVbaProject = !preserveVbaProject && hasSourcePackage && workbook.HasVbaProjectPackage;
        var modelUnchanged = hasSourcePackage && sourcePackage!.Matches(workbook, out currentModelFingerprint);

        if (modelUnchanged && !mustDropVbaProject)
        {
            sourcePackage!.CopyTo(stream);
            LastSaveDiagnostics = XlsxSaveDiagnostics.SourceCopy("model_unchanged");
            return;
        }

        var patchDiagnostics = mustDropVbaProject
            ? XlsxSaveDiagnostics.FullSave("vba_project_drop_requires_full_save")
            : XlsxSaveDiagnostics.FullSave("patch_not_attempted");
        if (sourcePackage is not null && !mustDropVbaProject)
        {
            bool patchSucceeded;
            try
            {
                patchSucceeded = sourcePackage.TrySavePatchedCellValues(
                    workbook,
                    stream,
                    ref currentModelFingerprint,
                    out patchDiagnostics);
            }
            catch (ArgumentException ex) when (IsXmlSerializationCharacterFailure(ex))
            {
                // Patch-path XML serialisation failed due to a character that cannot be represented
                // in XML (e.g. a control character that slipped through escaping).  Fall back to
                // the full ClosedXML save so the user's data is never lost.
                System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Patch-save XML error, falling back to full save: {ex.Message}");
                patchDiagnostics = XlsxSaveDiagnostics.FullSave("patch_xml_serialization_error");
                patchSucceeded = false;
            }
            catch (XmlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Patch-save XML error, falling back to full save: {ex.Message}");
                patchDiagnostics = XlsxSaveDiagnostics.FullSave("patch_xml_serialization_error");
                patchSucceeded = false;
            }

            if (patchSucceeded)
            {
                XlsxNamedRangeMapper.SaveToPackage(workbook, stream, warnings);
                sourcePackage.RestoreWorkbookDefinedNames(stream, workbook);
                LastSaveDiagnostics = patchDiagnostics;
                return;
            }
        }

        LastSaveDiagnostics = sourcePackage is null
            ? XlsxSaveDiagnostics.FullSave("no_source_package")
            : patchDiagnostics;

        using var xlWorkbook = new XLWorkbook();
        // F4: put ClosedXML into the workbook's date system so it writes the correct on-disk serial
        // for date cells (1904-relative when date1904="1"). Without this, ClosedXML always emits a
        // 1900-epoch serial while post-processing stamps date1904="1", so a reload (ClosedXML honors
        // date1904 on read) shifts every date by the 1462-day epoch difference. MapValueInverse hands
        // ClosedXML a true calendar DateTime, so ClosedXML now serializes it 1904-consistently.
        xlWorkbook.Use1904DateSystem = workbook.Uses1904DateSystem;
        XlsxClosedXmlCellMapper.ApplyStyle(xlWorkbook.Style, workbook.GetStyle(StyleId.Default));
        xlWorkbook.CalculateMode = workbook.CalculationMode == WorkbookCalculationMode.Manual
            ? XLCalculateMode.Manual
            : XLCalculateMode.Auto;
        var styleCache = new Dictionary<StyleId, CellStyle>(workbook.StyleCount);
        // Per-save cache: StyleId → boxed XLStyleValue captured after the first application.
        // Subsequent cells with the same StyleId are styled in one SetStyle call instead of
        // the ~15 individual ClosedXML setter calls that ApplyStyle performs.
        var xlStyleValueCache = XlCellSetStyleValueAction is not null && XlCellStyleValueAccessor is not null
            ? new Dictionary<StyleId, object>(workbook.StyleCount)
            : null;

        foreach (var sheet in workbook.Sheets)
        {
            var xlSheet = xlWorkbook.Worksheets.Add(sheet.Name);
            XlsxClosedXmlCellMapper.ApplyStyle(xlSheet.Style, workbook.GetStyle(StyleId.Default));
            xlSheet.Visibility = sheet.IsVeryHidden
                ? XLWorksheetVisibility.VeryHidden
                : sheet.IsHidden ? XLWorksheetVisibility.Hidden : XLWorksheetVisibility.Visible;
            // Prefer the theme-color reference over the baked RGB when present, mirroring the
            // font/fill/border save path (see R123-tab-theme-color-1) so a theme-relative tab color
            // round-trips as <tabColor theme="…" tint="…"/> instead of being downgraded to a literal
            // <tabColor rgb="…"/> that no longer follows the workbook theme in real Excel.
            if (sheet.TabThemeColor is { } tabThemeColor)
                xlSheet.TabColor = XlsxClosedXmlCellMapper.ToXLColor(tabThemeColor);
            else if (sheet.TabColor is { } tabColor)
                xlSheet.TabColor = XLColor.FromArgb(tabColor.R, tabColor.G, tabColor.B);

            // R136-io-worksheet-props-col-row-default-style: whole-column/-row default styles must be
            // applied BEFORE any per-cell style below. ClosedXML's IXLColumn.Style/IXLRow.Style setter
            // propagates onto every cell already in that column/row (verified: setting it after an
            // explicit per-cell style silently overwrites that cell's style), so applying it first —
            // column before row, since row must win at their intersection per Excel's cell > row >
            // column precedence, and each subsequent per-cell ApplyStyleFast/ApplyStyleOnlySeedCells
            // call below naturally overrides it for any cell that actually has its own style — is what
            // keeps every level of the precedence chain intact instead of the row/column default
            // stomping real cell-level formatting.
            foreach (var (colNum, columnStyleId) in sheet.ColumnStyles)
            {
                if (!IsValidWorksheetColumn(colNum))
                    continue;

                var columnStyle = GetCachedStyle(workbook, styleCache, columnStyleId);
                if (!columnStyle.Equals(CellStyle.Default))
                    XlsxClosedXmlCellMapper.ApplyStyle(xlSheet.Column((int)colNum).Style, columnStyle);
            }

            foreach (var (rowNum, rowStyleId) in sheet.RowStyles)
            {
                if (!IsValidWorksheetRow(rowNum))
                    continue;

                var rowStyle = GetCachedStyle(workbook, styleCache, rowStyleId);
                if (!rowStyle.Equals(CellStyle.Default))
                    XlsxClosedXmlCellMapper.ApplyStyle(xlSheet.Row((int)rowNum).Style, rowStyle);
            }

            // Cells claimed as non-anchor members of an array-formula range written below (via
            // FormulaArrayA1 over the full extent). These are skipped when the outer loop reaches
            // them so their provisional cached scalar (loaded into _cells by SetProvisionalSpillCell)
            // does not overwrite the array-formula member cell ClosedXML just wrote for that address.
            HashSet<(uint Row, uint Col)>? arrayMemberCellsWritten = null;

            foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
            {
                // Skip blank cells that carry no style
                if (cell.Value is BlankValue && !cell.HasFormula && cell.StyleId == StyleId.Default)
                    continue;
                if (!IsValidWorksheetRow(row) || !IsValidWorksheetColumn(col))
                    continue;

                var xlCell = xlSheet.Cell((int)row, (int)col);

                // A non-anchor member of an array-formula range already written below via
                // FormulaArrayA1 over the full extent. Skip re-writing its value/formula (that would
                // stomp the array-formula member cell ClosedXML just wrote for this address), but
                // still apply its style like any other occupied cell.
                var isHandledArrayMember = arrayMemberCellsWritten is not null && arrayMemberCellsWritten.Contains((row, col));

                if (isHandledArrayMember)
                {
                    // no-op: value/formula already represented by the array range write. ClosedXML
                    // never gives a non-anchor array-range member cell a cached value of its own (it
                    // has no way to evaluate the formula), so the member cell's <v> is restored
                    // post-save, directly in the generated XML, by
                    // XlsxWorksheetFormulaCachedValueWriter (see R86-io-shared-array-formula-5-2) --
                    // setting it here via ClosedXML's object model does not work: any read of
                    // IXLCell.Value on a cell tied to a Formula triggers ClosedXML's OWN calc engine
                    // (XLCell.Evaluate), which recomputes and overwrites it with ClosedXML's (usually
                    // wrong/blank) evaluation of the array formula before the workbook is serialized.
                }
                else if (cell.HasFormula)
                {
                    var formula = XlsxClosedXmlCellMapper.NormalizeFormulaText(cell.FormulaText!);
                    var anchorAddr = new CellAddress(sheet.Id, row, col);
                    // Prefer the live spill extent (populated by RecalcEngine.SetSpillRange once the
                    // workbook has been recalculated), but fall back to TryGetArrayExtent so an
                    // unrecalculated CSE/dynamic array formula — which only exists as "provisional
                    // spill cells" registered by the loader (Sheet._provisionalSpillCells) — is still
                    // recognised and its array-ness preserved on a full (ClosedXML) save. Without this
                    // fallback, TryGetSpillExtent alone (which only consults _spillAnchors) misses any
                    // array formula that was loaded but never recalculated, and the anchor gets written
                    // as a plain single-cell formula while its member cells are dropped to dead statics.
                    uint spillRows = 0, spillCols = 0;
                    var hasLiveSpillExtent = cell.ArrayMode == FormulaArrayMode.Dynamic &&
                        sheet.TryGetSpillExtent(anchorAddr, out spillRows, out spillCols);

                    // R124-io-spill-member-save-stale-extent: a live Sheet._spillAnchors registration
                    // can go stale without ever being invalidated. Not every path that writes a literal
                    // directly into a non-anchor spill member is guaranteed to trigger a recalculation
                    // of the owning anchor before Save runs — e.g. WorkbookCellEditService's Manual
                    // -calculation-mode handling (RecalculateFreshlyEnteredFormulasOnce) only recalculates
                    // affected cells that are THEMSELVES formulas, so typing a plain literal over a spill
                    // member (never a formula cell) calls RecalcEngine.Recalculate for zero cells and the
                    // anchor's stale pre-edit extent survives untouched all the way to Save. Trusting that
                    // stale extent here would fold the member's address into the anchor's FormulaArrayA1
                    // range below (see arrayMemberCellsWritten), and XlsxFileAdapter.cs's loader would
                    // register it as an anchor-owned provisional spill cell on reload — transparently
                    // overwriteable by the anchor's next recalculation (an ordinary F9) per
                    // Sheet.IsSpillBlocked's "provisional cells never block their own anchor" rule, so the
                    // user's typed-over value would be silently erased with no #SPILL! warning ever shown.
                    // Deliberately NOT calling Sheet.IsSpillBlocked here: that method is written to run
                    // AFTER RecalcEngine has already called ClearSpillRange(anchor) for this very pass, so
                    // it (correctly, for its own caller) treats ANY leftover _spillValues entry as a
                    // cross-anchor collision. Calling it here, before any such clear, would make it see
                    // this anchor's own still-registered, perfectly healthy _spillValues entries and
                    // report every ordinary untouched spill as "blocked". HasIndependentMemberOverride
                    // below asks the narrower, correct-for-this-context question: did some entry land in
                    // Sheet._cells (the ONLY place a direct SetCell/EditCellsCommand write can land) at a
                    // member address, carrying real content (not just formatting)? A live spill's own
                    // members never have a _cells entry — SetSpillRange stores non-anchor values purely in
                    // the separate _spillValues overlay and, on every respill, actively removes any
                    // leftover _cells entry it finds for its own provisional members — so a _cells hit
                    // here can only mean independent content was written after the spill was established.
                    var staleBlockedExtent = hasLiveSpillExtent &&
                        HasIndependentMemberOverride(sheet, anchorAddr, spillRows, spillCols);
                    var hasExtent = hasLiveSpillExtent && !staleBlockedExtent;
                    if (staleBlockedExtent)
                    {
                        spillRows = 0;
                        spillCols = 0;
                    }

                    if (!hasExtent && !hasLiveSpillExtent && cell.ArrayMode == FormulaArrayMode.Dynamic &&
                        sheet.TryGetArrayExtent(anchorAddr, out var arrayAnchor, out var arrayRows, out var arrayCols) &&
                        arrayAnchor.Row == row && arrayAnchor.Col == col)
                    {
                        hasExtent = true;
                        spillRows = arrayRows;
                        spillCols = arrayCols;
                    }

                    if (hasExtent &&
                        (long)spillRows * spillCols > 1 &&
                        IsValidWorksheetRow(row + spillRows - 1) && IsValidWorksheetColumn(col + spillCols - 1))
                    {
                        // A dynamic array (or legacy CSE array) that spills/covers a range is written as
                        // an array formula over its full extent, so it reloads as Dynamic (spilling) —
                        // or, for CSE arrays, at least keeps every member cell tied to the shared formula
                        // — instead of being mis-detected as a legacy implicit-intersection (plain) formula.
                        xlSheet.Range((int)row, (int)col, (int)(row + spillRows - 1), (int)(col + spillCols - 1))
                            .FormulaArrayA1 = formula;

                        // Remember every non-anchor member address so the outer loop's later visit to
                        // that occupied cell (a provisional cached value loaded by the XLSX reader)
                        // does not stomp the array-formula cell ClosedXML just wrote there.
                        if (spillRows > 1 || spillCols > 1)
                        {
                            arrayMemberCellsWritten ??= [];
                            for (var r = 0u; r < spillRows; r++)
                                for (var c = 0u; c < spillCols; c++)
                                {
                                    if (r == 0 && c == 0) continue;
                                    arrayMemberCellsWritten.Add((row + r, col + c));
                                }
                        }
                    }
                    else if (hasExtent || staleBlockedExtent ||
                             (cell.ArrayMode == FormulaArrayMode.Dynamic && cell.Value is ErrorValue { Code: "#SPILL!" }))
                    {
                        // Either (a) a known 1x1 dynamic-array/CSE array result — hasExtent is true but
                        // the extent is exactly 1x1 (e.g. UNIQUE() collapsing to a single equal value),
                        // so the multi-cell branch above didn't fire — or (b) a dynamic-array formula
                        // that is currently #SPILL!-blocked (RecalcEngine clears its spill range and
                        // leaves no _spillAnchors/_provisional entry for TryGetSpillExtent/
                        // TryGetArrayExtent above to find, so hasExtent is false here even though the
                        // formula is still array-shaped) — or (c) staleBlockedExtent: a registered
                        // _spillAnchors extent that HasIndependentMemberOverride just proved is no
                        // longer accurate (R124-io-spill-member-save-stale-extent above) even though the anchor's own
                        // cached cell.Value has not yet caught up to #SPILL! because nothing has
                        // recalculated it since the blocking write. Writing any of these as a plain
                        // xlCell.FormulaA1 would lose its array-ness entirely (no t="array" ref at all),
                        // and XlsxFileAdapter.cs's loader demotes any reloaded formula without
                        // HasArrayFormula to legacy Implicit mode permanently — so the identity would
                        // never re-spill again after an edit / once the blocker is removed. Write it as
                        // a single-cell array formula (t="array" ref=anchor) instead: that keeps
                        // HasArrayFormula true on reload (ArrayMode stays Dynamic), so the next recalc
                        // correctly re-evaluates via EvaluateSpilling and re-spills as needed. Crucially,
                        // this does NOT fold the blocking member's address into the anchor's declared
                        // ref, so the outer per-cell loop still visits it as an ordinary occupied cell —
                        // it round-trips as independent content, not an anchor-owned provisional spill
                        // cell that a later recalculation could mistake for overwriteable spill output.
                        xlSheet.Range((int)row, (int)col, (int)row, (int)col).FormulaArrayA1 = formula;
                    }
                    else
                    {
                        xlCell.FormulaA1 = formula;
                    }
                }
                else if (cell.Value is TextValue &&
                         sheet.RichTextRuns.TryGetValue(new CellAddress(sheet.Id, row, col), out var richRuns) &&
                         richRuns is { Count: > 0 })
                {
                    // Full-save rich-text path: write runs via ClosedXML's IXLRichText API so
                    // that per-run formatting (bold, subscript, color, …) is preserved in the
                    // shared-string table.  The patch-save path is mutually exclusive (it exits
                    // early at line 80-84 before this ClosedXML block is ever reached).
                    ApplyRichTextRuns(xlCell, richRuns);
                }
                else if (cell.Value is not BlankValue)
                {
                    xlCell.Value = XlsxClosedXmlCellMapper.MapValueInverse(cell.Value, workbook.Uses1904DateSystem);
                }

                if (cell.StyleId != StyleId.Default)
                {
                    var style = GetCachedStyle(workbook, styleCache, cell.StyleId);
                    if (!style.Equals(CellStyle.Default))
                        ApplyStyleFast(xlCell, style, cell.StyleId, xlStyleValueCache);
                }

                if (cell.QuotePrefix)
                    XlsxClosedXmlCellMapper.ApplyQuotePrefix(xlCell, true);
            }

            ApplyStyleOnlySeedCells(workbook, styleCache, xlStyleValueCache, xlSheet, sheet);

            foreach (var (rowNum, height) in sheet.RowHeights)
            {
                if (IsValidWorksheetRow(rowNum) && double.IsFinite(height) && height > 0)
                    xlSheet.Row((int)rowNum).Height = height * (72.0 / 96.0);
            }

            foreach (var rowNum in sheet.HiddenRows)
            {
                if (IsValidWorksheetRow(rowNum))
                    xlSheet.Row((int)rowNum).Hide();
            }

            foreach (var rowNum in sheet.FilterHiddenRows)
            {
                if (IsValidWorksheetRow(rowNum))
                    xlSheet.Row((int)rowNum).Hide();
            }

            foreach (var (rowNum, level) in sheet.RowOutlineLevels)
            {
                if (IsValidWorksheetRow(rowNum))
                    xlSheet.Row((int)rowNum).OutlineLevel = level;
            }

            // Hide the detail rows a collapsed group summarizes. Do NOT call Collapse() here --
            // ClosedXML's Collapse() stamps both hidden="1" AND collapsed="1" on the same row, which
            // would spuriously mark every interior hidden detail row as if it were itself the
            // group's visible outline anchor. The anchor row (see CollapsedAnchorRows below) is the
            // one that actually carries collapsed="1" in Excel's own output
            // (R35-deferred-collapse-anchor-1).
            foreach (var rowNum in sheet.GroupHiddenRows)
            {
                if (IsValidWorksheetRow(rowNum))
                    xlSheet.Row((int)rowNum).Hide();
            }

            // Mark the visible anchor (subtotal/summary) row of each collapsed outline group with
            // Excel's collapsed="1" outline marker. Collapse() is the only ClosedXML API that can set
            // the collapsed flag, but it also hides the row; Unhide() afterward clears that spurious
            // hidden flag so the anchor stays visible -- unless the same row is ALSO a genuinely
            // hidden row for ANY reason (an outer group's detail row, a manually-hidden row, or a
            // filter-hidden row), in which case both flags are correct and it must stay hidden.
            // Checking GroupHiddenRows alone would wrongly resurrect an anchor row the user also
            // hid manually or that a filter hides (R75-commands-outline-group-4-1).
            foreach (var rowNum in sheet.CollapsedAnchorRows)
            {
                if (!IsValidWorksheetRow(rowNum))
                    continue;
                var xlRow = xlSheet.Row((int)rowNum);
                xlRow.Collapse();
                if (!sheet.GroupHiddenRows.Contains(rowNum) &&
                    !sheet.HiddenRows.Contains(rowNum) &&
                    !sheet.FilterHiddenRows.Contains(rowNum))
                {
                    xlRow.Unhide();
                }
            }

            foreach (var (colNum, width) in sheet.ColumnWidths)
            {
                if (IsValidWorksheetColumn(colNum) && double.IsFinite(width) && width > 0)
                    xlSheet.Column((int)colNum).Width = width;
            }

            foreach (var colNum in sheet.HiddenCols)
            {
                if (IsValidWorksheetColumn(colNum))
                    xlSheet.Column((int)colNum).Hide();
            }

            foreach (var (colNum, level) in sheet.ColOutlineLevels)
            {
                if (IsValidWorksheetColumn(colNum))
                    xlSheet.Column((int)colNum).OutlineLevel = level;
            }

            // See the row-side handling above (R35-deferred-collapse-anchor-1): hide detail columns
            // without Collapse() so they don't get a spurious collapsed="1".
            foreach (var colNum in sheet.GroupHiddenCols)
            {
                if (IsValidWorksheetColumn(colNum))
                    xlSheet.Column((int)colNum).Hide();
            }

            // Mark the visible anchor column of each collapsed outline group with collapsed="1",
            // un-hiding afterward unless that same column is also hidden for ANY reason -- a
            // genuinely hidden detail column of an outer group, or a manually-hidden column.
            // Checking GroupHiddenCols alone would wrongly resurrect an anchor column the user also
            // hid manually (R75-commands-outline-group-4-1).
            foreach (var colNum in sheet.CollapsedAnchorCols)
            {
                if (!IsValidWorksheetColumn(colNum))
                    continue;
                var xlCol = xlSheet.Column((int)colNum);
                xlCol.Collapse();
                if (!sheet.GroupHiddenCols.Contains(colNum) &&
                    !sheet.HiddenCols.Contains(colNum))
                {
                    xlCol.Unhide();
                }
            }

            foreach (var (address, commentText) in sheet.Comments)
            {
                try
                {
                    var xlComment = xlSheet.Cell((int)address.Row, (int)address.Col)
                        .CreateComment();
                    // GAP 1: preserve the note author stored in CommentAuthors; fall back to
                    // empty string so ClosedXML doesn't silently default to the OS username.
                    if (sheet.CommentAuthors.TryGetValue(address, out var author) &&
                        !string.IsNullOrEmpty(author))
                    {
                        xlComment.Author = author;
                    }
                    else
                    {
                        xlComment.Author = string.Empty;
                    }
                    xlComment.AddText(commentText);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Skipping comment save for sheet '{sheet.Name}' cell '{address}': {ex.Message}");
                    warnings?.Add($"[comment] Comment at '{sheet.Name}!{address}' could not be saved and was skipped.");
                }
            }

            // R93-threaded-comment-extLst: real Excel always pairs a threaded comment with a
            // legacy xl/comments*.xml "compatibility shim" entry (author "tc={thread id}", text
            // starting with the fixed "[Threaded comment]" banner) so older Excel builds -- and any
            // reader that only understands the classic comment/VML model -- still show SOME note
            // on the cell instead of nothing at all. XlsxWorksheetThreadedCommentMapper.Save only
            // ever writes the MODERN xl/threadedComments/*.xml part; XlsxLegacyCommentPreserver only
            // PRESERVES a shim that already existed in a source package on a load-then-save round
            // trip. Neither one ever CREATES the shim for a thread authored fresh in FreeX, so
            // saving a brand-new threaded comment silently produced a file where every non-Excel
            // (or older-Excel) reader saw no comment indicator at all on that cell. Writing it here,
            // through ClosedXML's own CreateComment() alongside the legacy Comments loop above, gets
            // ClosedXML to emit the matching comments1.xml entry AND its VML note shape automatically
            // (the same way it already does for a genuine Sheet.Comments note) -- and on every
            // subsequent load-then-save round trip, XlsxLegacyCommentPreserver's existing shim
            // preservation/reconciliation logic (IsLegacyThreadedCommentShimEntry) takes over and
            // keeps it in sync with the thread instead of this fresh write.
            foreach (var (address, comment) in sheet.ThreadedComments)
            {
                if (sheet.Comments.ContainsKey(address))
                    continue; // a cell is never both an independent legacy note and a thread

                // A thread already carrying an id was loaded from (or already saved to) a source
                // package: XlsxLegacyCommentPreserver.Preserve owns keeping its legacy shim in sync
                // from there on (IsLegacyThreadedCommentShimEntry/TryResolveShiftedThreadedCommentAddress).
                // Writing another fresh ClosedXML comment/VML shape for it here as well would leave
                // TWO legacy comment/VML parts (ClosedXML's brand-new one plus Preserve's reconciled
                // source copy) once the post-processing pass runs -- only a thread with no id yet
                // (never saved before) needs ITS FIRST shim minted through ClosedXML.
                if (comment.Id is not null)
                    continue;

                try
                {
                    var threadId = XlsxWorksheetThreadedCommentMapper.ResolveThreadId(sheet, address, comment);
                    var xlShimComment = xlSheet.Cell((int)address.Row, (int)address.Col)
                        .CreateComment();
                    xlShimComment.Author = $"tc={threadId}";
                    xlShimComment.AddText(
                        "[Threaded comment]\n\nYour version of Excel allows you to read this threaded comment; " +
                        "however, any edits made to it will get removed if the file is opened in a newer version " +
                        "of Excel. Learn more: https://go.microsoft.com/fwlink/?linkid=870924\n\nComment:\n    " +
                        comment.Text);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Skipping threaded-comment legacy shim for sheet '{sheet.Name}' cell '{address}': {ex.Message}");
                    warnings?.Add($"[comment] Legacy compatibility note for the threaded comment at '{sheet.Name}!{address}' could not be saved and was skipped.");
                }
            }

            // Save merged regions BEFORE hyperlinks: ClosedXML's Range.Merge() clears every
            // non-anchor cell of the merged region (including anything just assigned to it), so a
            // hyperlink written to a non-anchor cell first (the shape ClosedXML's own loader
            // produces for a real Excel range hyperlink -- one hyperlink object per cell in the
            // range) would be silently wiped out the moment the region is merged. Applying the
            // merge first means the hyperlink loop below is writing to the already-merged
            // worksheet, so every cell's hyperlink assignment survives.
            foreach (var region in sheet.MergedRegions)
            {
                try
                {
                    var rangeStr = $"{CellAddress.NumberToColumnName(region.Start.Col)}{region.Start.Row}" +
                                   $":{CellAddress.NumberToColumnName(region.End.Col)}{region.End.Row}";
                    xlSheet.Range(rangeStr).Merge();
                }
                catch (Exception ex)
                {
                    var regionDesc = $"{CellAddress.NumberToColumnName(region.Start.Col)}{region.Start.Row}:{CellAddress.NumberToColumnName(region.End.Col)}{region.End.Row}";
                    System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Skipping merged-region save for sheet '{sheet.Name}' region '{regionDesc}': {ex.Message}");
                    warnings?.Add($"[merged-region] Merged region '{regionDesc}' on sheet '{sheet.Name}' could not be saved and was skipped.");
                }
            }

            foreach (var (address, target) in sheet.Hyperlinks)
            {
                try
                {
                    sheet.HyperlinkMetadata.TryGetValue(address, out var metadata);
                    var xlCell = xlSheet.Cell((int)address.Row, (int)address.Col);
                    xlCell.SetHyperlink(CreateXlsxHyperlink(target, metadata));

                    // SetHyperlink replaces the cell font with ClosedXML's Hyperlink style; restore the
                    // modelled font so explicit colours and underline choices round-trip.
                    var styledCell = sheet.GetCell(address);
                    if (styledCell is not null && styledCell.StyleId != StyleId.Default)
                    {
                        var style = GetCachedStyle(workbook, styleCache, styledCell.StyleId);
                        if (!style.Equals(CellStyle.Default))
                            XlsxClosedXmlCellMapper.ApplyHyperlinkFontOverride(xlCell, style);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[XlsxFileAdapter] Skipping hyperlink save for sheet '{sheet.Name}' cell '{address}' target '{target}': {ex.Message}");
                    warnings?.Add($"[hyperlink] Hyperlink at '{sheet.Name}!{address}' to '{target}' could not be saved and was skipped.");
                }
            }

            var frozenRows = ValidFrozenRowsOrZero(sheet.FrozenRows);
            var frozenCols = ValidFrozenColumnsOrZero(sheet.FrozenCols);
            if (frozenRows > 0 || frozenCols > 0)
                xlSheet.SheetView.Freeze((int)frozenRows, (int)frozenCols);

            if (sheet.PrintAreas.Count > 0)
            {
                xlSheet.PageSetup.PrintAreas.Clear();
                foreach (var printArea in sheet.PrintAreas)
                {
                    xlSheet.PageSetup.PrintAreas.Add(
                        (int)printArea.Start.Row,
                        (int)printArea.Start.Col,
                        (int)printArea.End.Row,
                        (int)printArea.End.Col);
                }
            }

            var pageOrientation = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PageOrientation, WorksheetPageOrientation.Portrait);
            var paperSize = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PaperSize, WorksheetPaperSize.A4);
            var pageMargins = XlsxWorksheetValueSanitizer.ValidPageMarginsOrDefault(sheet.PageMargins, WorksheetPageMargins.Narrow);
            var headerMargin = XlsxWorksheetValueSanitizer.NonNegativeFiniteOrDefault(sheet.HeaderMargin, 0.3);
            var footerMargin = XlsxWorksheetValueSanitizer.NonNegativeFiniteOrDefault(sheet.FooterMargin, 0.3);
            var scaleToFit = XlsxWorksheetValueSanitizer.ValidScaleToFitOrDefault(sheet.ScaleToFit, WorksheetScaleToFit.Default);
            var pageOrder = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PageOrder, WorksheetPageOrder.DownThenOver);
            var printErrorValue = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PrintErrorValue, WorksheetPrintErrorValue.Displayed);
            var printComments = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.PrintComments, WorksheetPrintComments.None);

            xlSheet.PageSetup.PageOrientation = pageOrientation == WorksheetPageOrientation.Landscape
                ? XLPageOrientation.Landscape
                : XLPageOrientation.Portrait;
            // Emit the raw OOXML paper-size code to preserve non-Letter/A4/Legal sizes.
            // When PaperSizeCode is a known OOXML code (Letter/A4/Legal/A3/etc.), the enum
            // is authoritative so dialog changes always take effect.  When PaperSizeCode is
            // an exotic/unknown code not in the enum map, preserve it verbatim.
            var paperSizeCode = sheet.PaperSizeCode > 0
                                && !PaperSizeCodes.TryGetEnum(sheet.PaperSizeCode, out _)
                ? sheet.PaperSizeCode                   // exotic code: preserve as-is
                : PaperSizeCodes.GetCode(paperSize);    // known code: derive from enum
            xlSheet.PageSetup.PaperSize = (XLPaperSize)paperSizeCode;
            xlSheet.PageSetup.Margins.Left = pageMargins.Left;
            xlSheet.PageSetup.Margins.Right = pageMargins.Right;
            xlSheet.PageSetup.Margins.Top = pageMargins.Top;
            xlSheet.PageSetup.Margins.Bottom = pageMargins.Bottom;
            xlSheet.PageSetup.Margins.Header = headerMargin;
            xlSheet.PageSetup.Margins.Footer = footerMargin;
            xlSheet.PageSetup.ShowGridlines = sheet.PrintGridlines;
            xlSheet.PageSetup.ShowRowAndColumnHeadings = sheet.PrintHeadings;
            xlSheet.PageSetup.CenterHorizontally = sheet.CenterHorizontallyOnPage;
            xlSheet.PageSetup.CenterVertically = sheet.CenterVerticallyOnPage;
            xlSheet.PageSetup.PageOrder = pageOrder == WorksheetPageOrder.OverThenDown
                ? XLPageOrderValues.OverThenDown
                : XLPageOrderValues.DownThenOver;
            if (sheet.FirstPageNumber is { } firstPageNumber && firstPageNumber > 0)
                xlSheet.PageSetup.FirstPageNumber = firstPageNumber;
            xlSheet.PageSetup.BlackAndWhite = sheet.PrintBlackAndWhite;
            xlSheet.PageSetup.DraftQuality = sheet.PrintDraftQuality;
            if (sheet.PrintQualityDpi is { } printQualityDpi && printQualityDpi > 0)
            {
                xlSheet.PageSetup.HorizontalDpi = printQualityDpi;
                xlSheet.PageSetup.VerticalDpi = sheet.PrintQualityVerticalDpi is { } verticalDpi && verticalDpi > 0
                    ? verticalDpi
                    : printQualityDpi;
            }
            else if (sheet.PrintQualityVerticalDpi is { } verticalDpi && verticalDpi > 0)
            {
                xlSheet.PageSetup.VerticalDpi = verticalDpi;
            }

            xlSheet.PageSetup.PrintErrorValue = XlsxWorksheetPageSetupMapper.ToPrintErrorValue(printErrorValue);
            xlSheet.PageSetup.ShowComments = XlsxWorksheetPageSetupMapper.ToPrintComments(printComments);
            xlSheet.PageSetup.DifferentFirstPageOnHF = sheet.DifferentFirstPageHeaderFooter;
            xlSheet.PageSetup.DifferentOddEvenPagesOnHF = sheet.DifferentOddEvenHeaderFooter;
            xlSheet.PageSetup.ScaleHFWithDocument = sheet.HeaderFooterScaleWithDocument;
            xlSheet.PageSetup.AlignHFWithMargins = sheet.HeaderFooterAlignWithMargins;
            XlsxWorksheetPageSetupMapper.SetHeaderFooter(
                xlSheet.PageSetup.Header,
                sheet.PageHeader,
                sheet.FirstPageHeader,
                sheet.EvenPageHeader,
                sheet.DifferentFirstPageHeaderFooter,
                sheet.DifferentOddEvenHeaderFooter);
            XlsxWorksheetPageSetupMapper.SetHeaderFooter(
                xlSheet.PageSetup.Footer,
                sheet.PageFooter,
                sheet.FirstPageFooter,
                sheet.EvenPageFooter,
                sheet.DifferentFirstPageHeaderFooter,
                sheet.DifferentOddEvenHeaderFooter);
            if (scaleToFit.ScalePercent is { } scalePercent)
                xlSheet.PageSetup.Scale = scalePercent;
            else if (scaleToFit.FitToPagesWide.HasValue || scaleToFit.FitToPagesTall.HasValue)
                // A null axis means "automatic/unbounded" (e.g. fitToHeight=0 loaded as null at line
                // 750-753 above: "fit all columns on one page, as many pages tall as needed"). Pass 0
                // — not 1 — for the unset axis so it round-trips as unbounded instead of being coerced
                // into an explicit 1-page cap (which would wrongly shrink a tall/wide report onto a
                // single page). ClosedXML's own load path treats a <= 0 PagesWide/PagesTall as unset
                // (see line 750-753), so writing 0 here is the correct inverse of that read.
                xlSheet.PageSetup.FitToPages(scaleToFit.FitToPagesWide ?? 0, scaleToFit.FitToPagesTall ?? 0);
            if (sheet.PrintTitleRows is { } titleRows && IsValidRepeatRange(titleRows, CellAddress.MaxRow))
                xlSheet.PageSetup.SetRowsToRepeatAtTop((int)titleRows.Start, (int)titleRows.End);
            if (sheet.PrintTitleColumns is { } titleColumns && IsValidRepeatRange(titleColumns, CellAddress.MaxCol))
                xlSheet.PageSetup.SetColumnsToRepeatAtLeft((int)titleColumns.Start, (int)titleColumns.End);
            foreach (var rowBreak in sheet.RowPageBreaks)
                if (rowBreak is >= 2 and <= CellAddress.MaxRow)
                    xlSheet.PageSetup.AddHorizontalPageBreak((int)rowBreak);
            foreach (var columnBreak in sheet.ColumnPageBreaks)
                if (columnBreak is >= 2 and <= CellAddress.MaxCol)
                    xlSheet.PageSetup.AddVerticalPageBreak((int)columnBreak);

            if (sheet.IsProtected)
            {
                if (string.IsNullOrEmpty(sheet.ProtectionPassword))
                    xlSheet.Protect(XLProtectionAlgorithm.Algorithm.SimpleHash);
                else
                    xlSheet.Protect(sheet.ProtectionPassword, XLProtectionAlgorithm.Algorithm.SimpleHash);
            }

            // Save CellValue conditional format rules back to XLSX
            XlsxConditionalFormatClosedXmlMapper.Save(sheet, xlSheet);

            // Save data validation rules back to XLSX. Sheets with native validation metadata are
            // emitted in the worksheet XML post-processing pass to avoid duplicate ClosedXML work.
            if (!XlsxDataValidationNativeMetadataMapper.HasNativeMetadata(sheet))
            {
                XlsxDataValidationClosedXmlMapper.Save(sheet, xlSheet, warnings);
            }
        }

        // Save named ranges (per-item isolation is inside the mapper)
        XlsxNamedRangeMapper.Save(workbook, xlWorkbook, warnings);

        if (CanSavePackageInPlace(stream))
        {
            stream.Position = 0;
            stream.SetLength(0);
            xlWorkbook.SaveAs(stream);
            ApplyPackagePostProcessing(
                workbook,
                stream,
                currentModelFingerprint,
                removeSourceCalcChain: patchDiagnostics.InvalidatesCalcChain,
                preserveVbaProject: preserveVbaProject);
            sourcePackage?.RestoreWorkbookDefinedNames(stream, workbook);
            stream.Position = stream.Length;
            return;
        }

        using var packageStream = new MemoryStream();
        xlWorkbook.SaveAs(packageStream);
        ApplyPackagePostProcessing(
            workbook,
            packageStream,
            currentModelFingerprint,
            removeSourceCalcChain: patchDiagnostics.InvalidatesCalcChain,
            preserveVbaProject: preserveVbaProject);
        sourcePackage?.RestoreWorkbookDefinedNames(packageStream, workbook);
        packageStream.Position = 0;
        packageStream.CopyTo(stream);
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);
    }

    private static bool CanSavePackageInPlace(Stream stream) =>
        stream.CanRead && stream.CanWrite && stream.CanSeek;

    /// <summary>
    /// R124-io-spill-member-save-stale-extent: true if any non-anchor cell within the given
    /// <paramref name="anchor"/>-rooted extent carries genuine independent content (a formula, or a
    /// value other than blank) directly in <see cref="Sheet.GetCell(CellAddress)"/> — i.e. content
    /// that could only have gotten there via a direct write (SetCell/EditCellsCommand and friends),
    /// never via the anchor's own <see cref="Sheet.SetSpillRange"/> (which stores non-anchor spill
    /// values purely in Sheet's separate spill-value overlay, and actively removes any leftover
    /// provisional <c>_cells</c> entry for its own members on every respill). A style-only cell
    /// (blank value, no formula, kept alive only to carry a StyleId) does not count — matching
    /// Sheet.IsSpillBlocked's own "formatting is not content" rule — so formatting a spill member
    /// without typing into it does not trip this check.
    /// </summary>
    private static bool HasIndependentMemberOverride(Sheet sheet, CellAddress anchor, uint rows, uint cols)
    {
        for (var r = 0u; r < rows; r++)
            for (var c = 0u; c < cols; c++)
            {
                if (r == 0 && c == 0) continue; // the anchor cell itself is expected to be occupied
                var occupant = sheet.GetCell(anchor.Row + r, anchor.Col + c);
                if (occupant is not null && (occupant.HasFormula || occupant.Value is not BlankValue))
                    return true;
            }
        return false;
    }

    private static CellStyle GetCachedStyle(
        Workbook workbook,
        Dictionary<StyleId, CellStyle> styleCache,
        StyleId styleId)
    {
        if (!styleCache.TryGetValue(styleId, out var style))
        {
            style = workbook.GetStyle(styleId);
            styleCache.Add(styleId, style);
        }

        return style;
    }

    private static void ApplyStyleOnlySeedCells(
        Workbook workbook,
        Dictionary<StyleId, CellStyle> styleCache,
        Dictionary<StyleId, object>? xlStyleValueCache,
        IXLWorksheet xlSheet,
        Sheet sheet)
    {
        if (!sheet.HasStyleOnlyCells)
            return;

        foreach (var seed in XlsxStyleOnlyCellWriter.GetSeedCells(sheet))
        {
            var style = GetCachedStyle(workbook, styleCache, seed.StyleId);
            if (style.Equals(CellStyle.Default))
                continue;

            var xlCell = xlSheet.Cell((int)seed.Row, (int)seed.Col);
            ApplyStyleFast(xlCell, style, seed.StyleId, xlStyleValueCache);
        }
    }

    /// <summary>
    /// Applies <paramref name="style"/> to <paramref name="xlCell"/> using a fast path when the
    /// ClosedXML <c>XLStyleValue</c> for this <paramref name="styleId"/> has been cached from a
    /// previous application: calls <c>SetStyle(XLStyleValue, propagate: false)</c> in one
    /// operation instead of ~15 individual property setters.  Falls back to the full
    /// <see cref="XlsxClosedXmlCellMapper.ApplyStyle"/> call on the first encounter and captures
    /// the resulting <c>XLStyleValue</c> for subsequent cells.
    /// </summary>
    private static void ApplyStyleFast(
        IXLCell xlCell,
        CellStyle style,
        StyleId styleId,
        Dictionary<StyleId, object>? xlStyleValueCache)
    {
        if (xlStyleValueCache is not null)
        {
            if (xlStyleValueCache.TryGetValue(styleId, out var cachedXlStyleValue))
            {
                // Fast path: replay cached XLStyleValue in a single SetStyle call.
                XlCellSetStyleValueAction!(xlCell, cachedXlStyleValue);
                return;
            }
        }

        // Slow path (first cell for this StyleId): apply via individual setters and, if the
        // fast-path delegates are available, capture the resulting XLStyleValue for reuse.
        XlsxClosedXmlCellMapper.ApplyStyle(xlCell, style);

        if (xlStyleValueCache is not null)
        {
            var captured = XlCellStyleValueAccessor!(xlCell);
            if (captured is not null)
                xlStyleValueCache[styleId] = captured;
        }
    }

    /// <summary>
    /// Writes per-run rich-text formatting on <paramref name="xlCell"/> via ClosedXML's
    /// <see cref="IXLRichText"/> API.  Used by the full-save (ClosedXML) path only — the
    /// patch-save path exits early before this code is reached, so there is no double-apply risk.
    /// </summary>
    /// <remarks>
    /// ClosedXML serialises rich-text cells as shared strings (<c>t="s"</c>).  On reload
    /// <see cref="XlsxRichRunLoader"/> reads both inline-string and shared-string sources, so the
    /// round-trip is fully transparent to the rest of the stack.
    ///
    /// Limitation: ClosedXML materialises default font properties (Calibri 11pt black) into every
    /// run even when the model run has <c>null</c> (inherit-from-cell).  After a full-save the
    /// reloaded run will carry explicit FontName/FontSize/FontColor values instead of <c>null</c>.
    /// Theme and indexed colors ARE round-tripped faithfully via
    /// <c>XLColor.FromTheme</c>/<c>XLColor.FromIndex</c>.
    /// </remarks>
    private static void ApplyRichTextRuns(
        IXLCell xlCell,
        IReadOnlyList<CellTextRun> runs)
    {
        var richText = xlCell.CreateRichText();

        foreach (var run in runs)
        {
            var xlRun = richText.AddText(run.Text);

            // Only set properties that are explicitly specified in the model run (non-null).
            // Null means "inherit from cell style" — ClosedXML will use its workbook default
            // when unset, which is the closest approximation available.
            if (run.Bold is { } bold)
                xlRun.Bold = bold;

            if (run.Italic is { } italic)
                xlRun.Italic = italic;

            if (run.Underline is { } underline)
                // R32: preserve double/double-accounting underline instead of always
                // downgrading to Single (mirrors CellStyle.DoubleUnderline for whole-cell fonts).
                xlRun.Underline = underline
                    ? run.DoubleUnderline == true
                        ? XLFontUnderlineValues.Double
                        : XLFontUnderlineValues.Single
                    : XLFontUnderlineValues.None;

            if (run.Strikethrough is { } strike)
                xlRun.Strikethrough = strike;

            if (run.FontName is { } fontName)
                xlRun.FontName = fontName;

            if (run.FontSize is { } fontSize)
                xlRun.FontSize = fontSize;

            if (run.FontColor is { } runColor)
                xlRun.FontColor = MapRunColorToXLColor(runColor);

            // R32: charset/family — underlying enum values match the raw OOXML numeric codes,
            // so a direct cast round-trips faithfully (e.g. charset=128 -> ShiftJIS).
            if (run.Charset is { } charset)
                xlRun.FontCharSet = (XLFontCharSet)charset;

            if (run.Family is { } family)
                xlRun.FontFamilyNumbering = (XLFontFamilyNumberingValues)family;

            if (run.Scheme is { } scheme)
                xlRun.FontScheme = scheme switch
                {
                    "major" => XLFontScheme.Major,
                    "minor" => XLFontScheme.Minor,
                    _       => XLFontScheme.None,
                };

            xlRun.VerticalAlignment = run.VertAlign switch
            {
                CellTextRunVertAlign.Superscript => XLFontVerticalTextAlignmentValues.Superscript,
                CellTextRunVertAlign.Subscript   => XLFontVerticalTextAlignmentValues.Subscript,
                _                                => XLFontVerticalTextAlignmentValues.Baseline,
            };
        }
    }

    /// <summary>
    /// Converts a <see cref="CellRunColor"/> to an <see cref="XLColor"/>, preserving theme
    /// and indexed references so they survive the round-trip without being flattened to RGB.
    /// </summary>
    /// <remarks>
    /// ClosedXML cannot express <c>&lt;color auto="1"/&gt;</c>.  For <see cref="CellRunColorKind.Auto"/>,
    /// we intentionally use <c>FromArgb(0,0,0,0)</c> (fully-transparent black, <c>rgb="00000000"</c>)
    /// as a sentinel value — it is an impossible color in real OOXML (alpha=0 is never written by Excel)
    /// so a post-processing pass in <see cref="ApplyPackagePostProcessing"/> can safely replace every
    /// <c>rgb="00000000"</c> in the shared-strings part with <c>auto="1"</c>, restoring the correct
    /// round-trip semantics without ambiguity.
    /// </remarks>
    private static XLColor MapRunColorToXLColor(CellRunColor color) => color.Kind switch
    {
        CellRunColorKind.Theme =>
            color.Tint is { } tint && Math.Abs(tint) > 0.000001
                ? XLColor.FromTheme((XLThemeColor)color.ThemeIndex, tint)
                : XLColor.FromTheme((XLThemeColor)color.ThemeIndex),
        CellRunColorKind.Indexed =>
            XLColor.FromIndex(color.IndexedIndex),
        CellRunColorKind.Auto =>
            // BX1 sentinel: transparent black is never emitted by Excel for a real color,
            // so the post-processing pass can safely rewrite it to <color auto="1"/>.
            XLColor.FromArgb(0, 0, 0, 0),
        _ => // Rgb
            XLColor.FromArgb(255, color.Rgb.R, color.Rgb.G, color.Rgb.B),
    };

    /// <summary>
    /// True when an <see cref="ArgumentException"/> reports a character that cannot be written to
    /// XML, so the caller can fall back to a full save instead of losing the user's data.
    /// </summary>
    /// <remarks>
    /// Recognized by where it was thrown as well as by wording. The message check alone reads a
    /// framework resource: "invalid character" is the English text, and on a runtime with localized
    /// satellite resources this filter would stop matching, the exception would escape, and the
    /// fallback that exists so a save never fails would silently disappear.
    /// </remarks>
    private static bool IsXmlSerializationCharacterFailure(ArgumentException exception)
    {
        var assembly = exception.TargetSite?.DeclaringType?.Assembly.GetName().Name;
        if (assembly is not null
            && assembly.StartsWith("System.Xml", StringComparison.Ordinal)
            || assembly == "System.Private.Xml")
        {
            return true;
        }

        return exception.Message.Contains("invalid character", StringComparison.OrdinalIgnoreCase);
    }

}
