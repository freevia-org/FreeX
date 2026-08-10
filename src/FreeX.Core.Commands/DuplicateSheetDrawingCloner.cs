using System.Text.RegularExpressions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Clones drawing-layer worksheet content for duplicated sheets.</summary>
internal static class DuplicateSheetDrawingCloner
{
    internal static void CopyDrawingCollections(Sheet source, Sheet copy, SheetId copyId)
    {
        var zOrderIdMap = new Dictionary<DrawingObjectZOrderEntry, DrawingObjectZOrderEntry>();
        foreach (var chart in source.Charts)
        {
            var clonedChart = CloneChart(chart, source.Id, copyId);
            // R106-drawing-object-hyperlink-duplicate-rebase: see the matching comment on
            // RewriteSameSheetHyperlinkTarget below -- without this, a chart's own 'Place in This
            // Document' hyperlink to a cell on its own (duplicated) sheet kept pointing back at the
            // SOURCE sheet instead of following the copy, unlike the equivalent cell hyperlink.
            clonedChart.Hyperlink = RewriteSameSheetHyperlinkTarget(clonedChart.Hyperlink, source.Name, copy.Name);
            copy.Charts.Add(clonedChart);
        }

        // The DataRange remap above (in CloneChart) only handles the GridRange-typed series
        // range. Verbatim (multi-area/unparsable) series formulas, "value from cells" data-label
        // formulas, and custom error-bar range formulas are stored as raw sheet-qualified text
        // and are copied byte-for-byte by CloneChart, so they still literally say the SOURCE
        // sheet's name. Reuse the same RenameSheetOp-based rewriter that SheetCommands' actual
        // Rename Sheet path uses, so a same-sheet reference on the duplicate now points at the
        // copy sheet — matching Excel's Duplicate Sheet behavior for the GridRange DataRange case.
        if (copy.Charts.Count > 0 && !string.Equals(source.Name, copy.Name, StringComparison.Ordinal))
        {
            var renameOp = new RenameSheetOp(source.Name, copy.Name);
            RowColumnShiftHelpers.RewriteChartVerbatimFormulas(copy, renameOp, copy.Name);
            foreach (var chart in copy.Charts)
                RewriteErrorBarRangeFormulas(chart, renameOp, copy.Name);
        }

        foreach (var textBox in source.TextBoxes)
        {
            var cloned = CloneTextBox(textBox, copyId);
            // R106-drawing-object-hyperlink-duplicate-rebase: see RewriteSameSheetHyperlinkTarget.
            cloned.Hyperlink = RewriteSameSheetHyperlinkTarget(cloned.Hyperlink, source.Name, copy.Name);
            copy.TextBoxes.Add(cloned);
            zOrderIdMap[new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, textBox.Id)] =
                new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, cloned.Id);
        }

        foreach (var shape in source.DrawingShapes)
        {
            var cloned = CloneDrawingShape(shape, copyId);
            // R106-drawing-object-hyperlink-duplicate-rebase: see RewriteSameSheetHyperlinkTarget.
            cloned.Hyperlink = RewriteSameSheetHyperlinkTarget(cloned.Hyperlink, source.Name, copy.Name);
            copy.DrawingShapes.Add(cloned);
            zOrderIdMap[new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id)] =
                new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, cloned.Id);
        }

        foreach (var picture in source.Pictures)
        {
            var cloned = ClonePicture(picture, source.Id, source.Name, copy.Name, copyId);
            // R106-drawing-object-hyperlink-duplicate-rebase: see RewriteSameSheetHyperlinkTarget.
            cloned.Hyperlink = RewriteSameSheetHyperlinkTarget(cloned.Hyperlink, source.Name, copy.Name);
            copy.Pictures.Add(cloned);
            zOrderIdMap[new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id)] =
                new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, cloned.Id);
        }

        if (source.DrawingObjectZOrder.Count > 0)
        {
            foreach (var entry in DrawingObjectZOrder.GetNormalizedOrder(source))
            {
                if (zOrderIdMap.TryGetValue(entry, out var clonedEntry))
                    copy.DrawingObjectZOrder.Add(clonedEntry);
            }
        }

        foreach (var sparkline in source.Sparklines)
            copy.Sparklines.Add(CloneSparkline(sparkline, source.Id, copyId));

        foreach (var control in source.FormControls)
            copy.FormControls.Add(CloneFormControl(control, copyId));
    }

    /// <summary>
    /// R103: clones every workbook-level Slicer/Timeline anchored on <paramref name="source"/> (via
    /// <see cref="SlicerModel.SourceSheetName"/> / <see cref="TimelineModel.SourceSheetName"/>) onto
    /// <paramref name="copy"/>. Unlike every other floating-object kind (Charts, TextBoxes,
    /// DrawingShapes, Pictures, Sparklines, FormControls), Slicers/Timelines are NOT part of
    /// <c>Sheet</c>'s own drawing collections -- they live in workbook-level
    /// <see cref="Workbook.Slicers"/>/<see cref="Workbook.Timelines"/>, keyed to a host sheet only
    /// indirectly by name -- so <see cref="CopyDrawingCollections"/> (which only ever sees
    /// <paramref name="source"/>/<paramref name="copy"/>, not the owning <see cref="Workbook"/>) can
    /// never reach them. Without this, Duplicate Sheet / Move-or-Copy silently dropped any slicer or
    /// timeline that was filtering a pivot table (or table) on the duplicated sheet, even though the
    /// pivot table itself is faithfully cloned -- unlike real Excel, which copies the slicer/timeline
    /// along with it.
    /// <para>
    /// <see cref="SlicerModel.Name"/>/<see cref="TimelineModel.Name"/> and
    /// <c>CacheName</c> are given workbook-unique values (mirroring
    /// <c>DuplicateSheetCommand.UniquifyClonedTables</c> for cloned structured tables) so the copy
    /// doesn't collide with the source's slicer/timeline identity, and
    /// <see cref="SlicerModel.PackagePart"/>/<see cref="TimelineModel.PackagePart"/> are left blank so
    /// <c>XlsxSlicerTimelineWriter</c> allocates the clone its own package part on save instead of
    /// aliasing the source's (mirroring <c>IsSourceLoaded = false</c> on <see cref="ClonePicture"/>/
    /// <see cref="CloneTextBox"/>/<see cref="CloneDrawingShape"/> above).
    /// </para>
    /// <see cref="SlicerModel.DrawingAnchor"/>/<see cref="TimelineModel.DrawingAnchor"/> need no
    /// remapping (unlike a chart/shape/picture's sheet-qualified anchor address): it is a bare
    /// column/row-offset pair with no embedded <see cref="SheetId"/>, so it already describes a valid
    /// position on the copy sheet's own drawing layer as-is.
    /// </summary>
    internal static (List<SlicerModel> Slicers, List<TimelineModel> Timelines) CopySlicersAndTimelines(
        Workbook workbook, Sheet source, Sheet copy)
    {
        var clonedSlicers = new List<SlicerModel>();
        var clonedTimelines = new List<TimelineModel>();

        foreach (var slicer in workbook.Slicers
                     .Where(s => string.Equals(s.SourceSheetName, source.Name, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var clone = new SlicerModel
            {
                Name = GenerateUniqueName(workbook.Slicers.Select(s => s.Name), slicer.Name),
                Caption = slicer.Caption,
                CacheName = GenerateUniqueName(workbook.Slicers.Select(s => s.CacheName), slicer.CacheName),
                SourcePivotTableName = slicer.SourcePivotTableName,
                // R133-io-slicer-timeline-multipivot: copy the list rather than aliasing the source
                // slicer's instance (same reasoning as CacheItems below), and rather than leaving it
                // to the property's own `= []` default, which would silently drop every OTHER pivot
                // connection this slicer carries beyond SourcePivotTableName.
                ConnectedPivotTableNames = slicer.ConnectedPivotTableNames.ToList(),
                SourceFieldName = slicer.SourceFieldName,
                StyleName = slicer.StyleName,
                PackagePart = string.Empty,
                DrawingAnchor = slicer.DrawingAnchor,
                DrawingShapeName = slicer.DrawingShapeName,
                ColumnCount = slicer.ColumnCount,
                ShowCaption = slicer.ShowCaption,
                SourceSheetName = copy.Name,
                SourceTableId = slicer.SourceTableId,
                SourceTableColumnId = slicer.SourceTableColumnId,
                // R117-commands-pivot-slicer-growth: CacheItems is now a mutable List<> (a later
                // refresh can append newly-appeared indices to it); copy the list rather than aliasing
                // the source slicer's instance, or a refresh-driven append to one would silently mutate
                // the other's cache items too.
                CacheItems = slicer.CacheItems.ToList(),
                AvailableItems = slicer.AvailableItems,
                SelectionCaptured = slicer.SelectionCaptured
            };
            clone.SelectedItems.AddRange(slicer.SelectedItems);
            workbook.Slicers.Add(clone);
            clonedSlicers.Add(clone);
        }

        foreach (var timeline in workbook.Timelines
                     .Where(t => string.Equals(t.SourceSheetName, source.Name, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var clone = new TimelineModel
            {
                Name = GenerateUniqueName(workbook.Timelines.Select(t => t.Name), timeline.Name),
                Caption = timeline.Caption,
                CacheName = GenerateUniqueName(workbook.Timelines.Select(t => t.CacheName), timeline.CacheName),
                SourcePivotTableName = timeline.SourcePivotTableName,
                ConnectedPivotTableNames = timeline.ConnectedPivotTableNames.ToList(),
                SourceFieldName = timeline.SourceFieldName,
                StyleName = timeline.StyleName,
                StartDate = timeline.StartDate,
                EndDate = timeline.EndDate,
                SelectedStartDate = timeline.SelectedStartDate,
                SelectedEndDate = timeline.SelectedEndDate,
                PackagePart = string.Empty,
                DrawingAnchor = timeline.DrawingAnchor,
                DrawingShapeName = timeline.DrawingShapeName,
                SourceSheetName = copy.Name,
                Level = timeline.Level,
                SelectionLevel = timeline.SelectionLevel,
                ScrollPosition = timeline.ScrollPosition
            };
            workbook.Timelines.Add(clone);
            clonedTimelines.Add(clone);
        }

        return (clonedSlicers, clonedTimelines);
    }

    /// <summary>
    /// Generates a workbook-unique name for a cloned Slicer/Timeline's Name or CacheName, trying
    /// <paramref name="baseName"/> unchanged first (only relevant if a caller ever needs the exact
    /// source name and it happens to already be free), then <c>baseName_2</c>, <c>baseName_3</c>, ...
    /// against <paramref name="existingNames"/> -- mirrors
    /// <c>DuplicateSheetCommand.GenerateUniqueTableName</c>'s numbered-suffix scheme for cloned
    /// structured tables.
    /// </summary>
    private static string GenerateUniqueName(IEnumerable<string> existingNames, string baseName)
    {
        var existing = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (existing.Add(baseName))
            return baseName;

        for (var n = 2; n < 10_000; n++)
        {
            var candidate = $"{baseName}_{n}";
            if (!existing.Contains(candidate))
                return candidate;
        }

        return $"{baseName}_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// R100: rewrites every Table[...] structured reference inside <paramref name="copy"/>'s own
    /// cloned charts (verbatim series value/category/name/bubble-size formulas, "value from cells"
    /// data-label formulas, and custom error-bar range formulas) that named one of the tables
    /// <c>DuplicateSheetCommand.UniquifyClonedTables</c> just renamed on this same sheet, from its
    /// old (source-sheet) name to its new workbook-unique name. A chart series sourced from
    /// "=Table1[Values]" is unparsable as a plain <see cref="GridRange"/> (see
    /// <c>XlsxChartSeriesRangeReader.TryParseFormulaRange</c>), so it lands on the verbatim path
    /// <see cref="CopyDrawingCollections"/> already clones byte-for-byte — without this rewrite the
    /// duplicate's own chart would keep resolving the structured reference to the SOURCE sheet's
    /// still-named table instead of the copy's own renamed one, exactly the gap
    /// <c>DuplicateSheetCommand.RewriteClonedTableReferences</c> already closes for cell formulas
    /// and table self-reference metadata. Called separately from (and after) that method because
    /// <see cref="CopyDrawingCollections"/> clones charts onto <paramref name="copy"/> before the
    /// sheet's table identities are uniquified/renamed.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT reuse <see cref="RowColumnShiftHelpers.RewriteChartVerbatimFormulas(Sheet, RewriteOperation, string)"/>
    /// (the RenameSheetOp path <see cref="CopyDrawingCollections"/> uses above): that helper's
    /// <c>RewriteVerbatimFormula</c> pre-splits the formula text on every top-level unquoted comma
    /// to handle multi-area range unions like "(Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5)" -- but a
    /// structured reference such as "Table1[[#Headers],[Values]]" ALSO contains an unquoted comma
    /// (inside its own bracket pair), so that splitter would cut it into two bogus fragments and
    /// silently corrupt it. A table rename never appears as one area of a multi-area union, so
    /// each formula is instead run directly through <see cref="FormulaRewriter.Rewrite"/> (which
    /// parses the whole expression, so a comma nested inside "[...]" is never mistaken for a
    /// union separator) exactly as <c>DuplicateSheetCommand.RewriteFormulaForTableRenames</c>
    /// already does for cell formulas. The host-sheet-name parameter <see cref="FormulaRewriter.Rewrite"/>
    /// takes is irrelevant for a table rename (<see cref="RenameTableOp"/> matches purely by table
    /// name, with no sheet-qualification concept), so any non-null placeholder is safe to pass.
    /// </remarks>
    internal static void RewriteClonedChartTableReferences(
        Sheet copy, IReadOnlyList<(string OldName, string NewName)> renames)
    {
        if (renames.Count == 0 || copy.Charts.Count == 0)
            return;

        foreach (var chart in copy.Charts)
        {
            if (chart.VerbatimSeriesFormulas is { Count: > 0 } vf)
            {
                for (var i = 0; i < vf.Count; i++)
                {
                    var entry = vf[i];
                    var newVal = RewriteFormulaForTableRenames(entry.ValFormula, renames);
                    var newCat = RewriteFormulaForTableRenames(entry.CatFormula, renames);
                    var newTx = RewriteFormulaForTableRenames(entry.TxFormula, renames);
                    var newBubble = RewriteFormulaForTableRenames(entry.BubbleSizeFormula, renames);
                    if (!string.Equals(newVal, entry.ValFormula, StringComparison.Ordinal) ||
                        !string.Equals(newCat, entry.CatFormula, StringComparison.Ordinal) ||
                        !string.Equals(newTx, entry.TxFormula, StringComparison.Ordinal) ||
                        !string.Equals(newBubble, entry.BubbleSizeFormula, StringComparison.Ordinal))
                    {
                        vf[i] = entry with
                        {
                            ValFormula = newVal,
                            CatFormula = newCat,
                            TxFormula = newTx,
                            BubbleSizeFormula = newBubble
                        };
                    }
                }
            }

            if (chart.SeriesRangeDataLabels is { Count: > 0 } dl)
            {
                for (var i = 0; i < dl.Count; i++)
                {
                    var entry = dl[i];
                    var rewritten = RewriteFormulaForTableRenames(entry.Formula, renames);
                    if (!string.Equals(rewritten, entry.Formula, StringComparison.Ordinal))
                        dl[i] = entry with { Formula = rewritten };
                }
            }

            var newPlus = RewriteFormulaForTableRenames(chart.ErrorBarPlusRangeFormula, renames);
            var newMinus = RewriteFormulaForTableRenames(chart.ErrorBarMinusRangeFormula, renames);
            if (!string.Equals(newPlus, chart.ErrorBarPlusRangeFormula, StringComparison.Ordinal))
                chart.ErrorBarPlusRangeFormula = newPlus;
            if (!string.Equals(newMinus, chart.ErrorBarMinusRangeFormula, StringComparison.Ordinal))
                chart.ErrorBarMinusRangeFormula = newMinus;
        }
    }

    /// <summary>
    /// Runs <paramref name="formulaText"/> through <see cref="FormulaRewriter.Rewrite"/> once per
    /// rename in <paramref name="renames"/> (a sheet can host more than one renamed table) and
    /// returns the fully rewritten text, or the original text unchanged if none of the renames
    /// touched it (or it was null/blank) -- mirroring
    /// <c>DuplicateSheetCommand.RewriteFormulaForTableRenames</c>/<c>RewriteNullableFormulaForTableRenames</c>
    /// exactly, just duplicated here since that method is private to its own file.
    /// </summary>
    private static string? RewriteFormulaForTableRenames(
        string? formulaText, IReadOnlyList<(string OldName, string NewName)> renames)
    {
        if (string.IsNullOrWhiteSpace(formulaText))
            return formulaText;

        string? current = null;
        foreach (var (oldName, newName) in renames)
        {
            var rewritten = FormulaRewriter.Rewrite(current ?? formulaText, new RenameTableOp(oldName, newName), string.Empty);
            if (rewritten is not null)
                current = rewritten;
        }

        return current ?? formulaText;
    }

    /// <summary>
    /// R106-drawing-object-hyperlink-duplicate-rebase: rewrites a drawing object's internal
    /// ('Place in This Document') hyperlink target (<see cref="DrawingObjectHyperlink.Target"/> --
    /// the exact same sheet-qualified-reference shape documented there, e.g. "Sheet1!A1" or
    /// "'Sheet 1'!A1") so a reference that names the sheet being duplicated follows the DUPLICATE
    /// sheet instead of continuing to point back at the SOURCE sheet -- mirroring Sheet.Clone's
    /// identical rebase of a CELL hyperlink's <c>Sheet.Hyperlinks[addr]</c> target (guarded there by
    /// <c>HyperlinkTargetKind.PlaceInThisDocument</c>) and the ConditionalFormat/DataValidation
    /// formula rebase alongside it. Without this, CloneTextBox/CloneDrawingShape/ClonePicture/
    /// CloneChart's verbatim <c>Hyperlink = ...</c> copy left a shape/text box/picture/chart's
    /// self-referencing hyperlink jumping back to the original sheet on every Duplicate Sheet,
    /// unlike the equivalent cell hyperlink right next to it.
    /// <para>
    /// An external ("Existing File or Web Page") hyperlink -- <see cref="DrawingObjectHyperlink.TargetMode"/>
    /// == "External" -- is left completely untouched: only an internal target (TargetMode null, the
    /// OPC default) can possibly be a same-sheet-qualified reference at all.
    /// </para>
    /// <para>
    /// Duplicates (rather than reuses) Sheet.Clone's private <c>RewriteSameSheetQualifiedFormula</c>
    /// text substitution, mirroring <see cref="RewriteFormulaForTableRenames"/> above -- that method
    /// is private to its own file too. The string-literal-skipping machinery
    /// <c>RewriteSameSheetQualifiedFormula</c> layers on top isn't needed here: a hyperlink Target is
    /// never an Excel formula with quoted text runs, just a bare reference or defined name, so a
    /// straight regex substitution is safe.
    /// </para>
    /// </summary>
    private static DrawingObjectHyperlink? RewriteSameSheetHyperlinkTarget(
        DrawingObjectHyperlink? hyperlink, string sourceSheetName, string copySheetName)
    {
        if (hyperlink is null || hyperlink.TargetMode is not null ||
            string.Equals(sourceSheetName, copySheetName, StringComparison.Ordinal))
            return hyperlink;

        var newQualifier = SheetNameFormatter.QuoteIfNeeded(copySheetName) + "!";

        // Already-quoted source qualifier, e.g. 'Sheet 1'!
        var quotedOldQualifier = "'" + sourceSheetName.Replace("'", "''") + "'!";

        // Bare (unquoted) source qualifier, e.g. Sheet1! -- guarded so it can't match a fragment of
        // a longer identifier/qualifier (e.g. a source name of "Sheet1" must not match inside
        // "OtherSheet1!") or re-touch the quoted form already handled above.
        var pattern = "(?<![A-Za-z0-9_.'])" + Regex.Escape(sourceSheetName) + "!";

        var rewritten = hyperlink.Target.Replace(quotedOldQualifier, newQualifier, StringComparison.OrdinalIgnoreCase);
        rewritten = Regex.Replace(rewritten, pattern, _ => newQualifier, RegexOptions.IgnoreCase);

        return string.Equals(rewritten, hyperlink.Target, StringComparison.Ordinal)
            ? hyperlink
            : hyperlink with { Target = rewritten };
    }

    private static FormControlModel CloneFormControl(FormControlModel control, SheetId copyId) =>
        new()
        {
            Kind = control.Kind,
            Name = control.Name,
            Caption = control.Caption,
            ShapeId = control.ShapeId,
            Anchor = RemapRange(control.Anchor, copyId),
            AnchorOffsets = control.AnchorOffsets,
            // LinkedCell/ListFillRange are copied verbatim (not sheet-rewritten), mirroring how
            // Sheet.Clone leaves cell formulas unrewritten (DuplicateSheetCommand's named-range
            // copy): an unqualified reference (the common case, e.g. "$D$3") implicitly means
            // "this control's own hosting sheet" — see RowColumnShiftHelpers.ShiftFormControlRef —
            // so copying it verbatim onto the duplicate correctly follows the control to the copy,
            // matching Excel's Duplicate Sheet behavior for linked form controls.
            LinkedCell = control.LinkedCell,
            ListFillRange = control.ListFillRange,
            IsChecked = control.IsChecked,
            Value = control.Value,
            Min = control.Min,
            Max = control.Max,
            Increment = control.Increment,
            PageChange = control.PageChange,
            SelectedIndex = control.SelectedIndex,
            SelectedText = control.SelectedText
        };

    /// <summary>
    /// R107-cmd-duplicate-sheet-sparkline-cross-sheet-datarange: only remap DataRange/DateAxisRange
    /// onto the copy when they actually point at the sheet being duplicated — a cross-sheet
    /// DataRange (e.g. a Dashboard sparkline sourced from Data!A1:E1) must keep pointing at the
    /// original source sheet, matching Excel's Duplicate Sheet behavior (only same-sheet references
    /// travel with the copy). Mirrors CloneChart's identical DataRange guard above. Location is
    /// always remapped unconditionally: a sparkline's Location is, by definition, always a cell on
    /// the sheet being duplicated (that's what makes it "this sheet's sparkline"), never a
    /// cross-sheet reference.
    /// </summary>
    private static SparklineModel CloneSparkline(SparklineModel sparkline, SheetId sourceSheetId, SheetId copyId) =>
        new()
        {
            DataRange = sparkline.DataRange.Start.Sheet == sourceSheetId
                ? RemapRange(sparkline.DataRange, copyId)
                : sparkline.DataRange,
            Location = RemapAddress(sparkline.Location, copyId),
            Kind = sparkline.Kind,
            GroupId = sparkline.GroupId,
            ShowMarkers = sparkline.ShowMarkers,
            ShowHighPoint = sparkline.ShowHighPoint,
            ShowLowPoint = sparkline.ShowLowPoint,
            ShowFirstPoint = sparkline.ShowFirstPoint,
            ShowLastPoint = sparkline.ShowLastPoint,
            ShowNegativePoints = sparkline.ShowNegativePoints,
            ShowAxis = sparkline.ShowAxis,
            DisplayHidden = sparkline.DisplayHidden,
            RightToLeft = sparkline.RightToLeft,
            SeriesColor = sparkline.SeriesColor,
            NegativeColor = sparkline.NegativeColor,
            AxisColor = sparkline.AxisColor,
            MarkersColor = sparkline.MarkersColor,
            HighPointColor = sparkline.HighPointColor,
            LowPointColor = sparkline.LowPointColor,
            FirstPointColor = sparkline.FirstPointColor,
            LastPointColor = sparkline.LastPointColor,
            LineWeight = sparkline.LineWeight,
            MinAxisType = sparkline.MinAxisType,
            MaxAxisType = sparkline.MaxAxisType,
            ManualMin = sparkline.ManualMin,
            ManualMax = sparkline.ManualMax,
            DisplayEmptyCellsAs = sparkline.DisplayEmptyCellsAs,
            DateAxisRange = sparkline.DateAxisRange is { } dateAxisRange && dateAxisRange.Start.Sheet == sourceSheetId
                ? RemapRange(dateAxisRange, copyId)
                : sparkline.DateAxisRange
        };

    /// <summary>
    /// Also used by <c>PasteTextBoxesCommand</c> (plain-range-copy floating-object carry) --
    /// R92-cmd-paste-floating-objects. Bumped from private to internal for that reuse, mirroring
    /// <see cref="CloneDrawingShape"/> and <see cref="CloneChart"/> above.
    /// </summary>
    internal static TextBoxModel CloneTextBox(TextBoxModel textBox, SheetId copyId) =>
        new()
        {
            Name = textBox.Name,
            Anchor = RemapAddress(textBox.Anchor, copyId),
            AnchorOffsetX = textBox.AnchorOffsetX,
            AnchorOffsetY = textBox.AnchorOffsetY,
            Text = textBox.Text,
            Title = textBox.Title,
            AltText = textBox.AltText,
            Width = textBox.Width,
            Height = textBox.Height,
            RotationDegrees = textBox.RotationDegrees,
            FlipHorizontal = textBox.FlipHorizontal,
            FlipVertical = textBox.FlipVertical,
            IsVisible = textBox.IsVisible,
            HasFill = textBox.HasFill,
            FillColor = textBox.FillColor,
            OutlineColor = textBox.OutlineColor,
            FillThemeColor = textBox.FillThemeColor,
            OutlineThemeColor = textBox.OutlineThemeColor,
            // backlog textbox-6-2: copy the txBody text-formatting fields too -- without this
            // Duplicate Sheet silently stripped a text box's font size/bold/italic/color/alignment
            // even though TextBoxModel now carries them.
            TextFontFamily = textBox.TextFontFamily,
            TextFontSizePoints = textBox.TextFontSizePoints,
            TextBold = textBox.TextBold,
            TextItalic = textBox.TextItalic,
            TextColor = textBox.TextColor,
            TextThemeColor = textBox.TextThemeColor,
            TextHAlign = textBox.TextHAlign,
            TextVAnchor = textBox.TextVAnchor,
            // R127B-clone-editas-parity: mirrors CloneChart's DrawingAnchorKind copy -- without this
            // a oneCellAnchor/absoluteAnchor text box silently reverted to the TwoCell default on
            // every clone (Duplicate Sheet, Ctrl+C/Ctrl+V, paste-carry), reintroducing the original
            // r127 move/resize defect for the copy even though the source object stayed protected.
            DrawingAnchorKind = textBox.DrawingAnchorKind,
            // R97-model-drawing-hyperlink-2-2: carry the object-level hyperlink forward -- without
            // this, a text box's hyperlink was only ever preserved by re-reading it from the SOURCE
            // package keyed by cNvPr@name (XlsxWorksheetDrawingObjectWriter's R95 mechanism), which a
            // duplicate (never present in the source package under its own sheet name) can't reach.
            Hyperlink = textBox.Hyperlink,
            // A source-loaded text box's on-disk part is preserved by keying source drawing parts
            // by sheet NAME (XlsxFileAdapter.SavePostProcessing.GetSourceDrawingPathsBySheet); the
            // duplicate always gets a brand-new sheet name (e.g. "Sheet1 (2)") that is absent from
            // the source package, so no source part is ever mapped to it and the writer's
            // IsSourceLoaded-skipping emission drops it — the text box would be silently dropped on
            // save. Mark the clone as NOT source-loaded so it round-trips through the normal text
            // box writer like any other authored text box, mirroring ClonePicture below.
            IsSourceLoaded = false
        };

    /// <summary>
    /// Also used by <c>DuplicateDrawingObjectCommand</c> (single-object Ctrl+C/Ctrl+V) --
    /// R91-io-clipboard-image-formats-5-1 -- and by <c>PasteShapesCommand</c> (plain-range-copy
    /// floating-object carry) -- R92-cmd-paste-floating-objects. See <see cref="CloneChart"/> for why
    /// this is internal.
    /// </summary>
    internal static DrawingShapeModel CloneDrawingShape(DrawingShapeModel shape, SheetId copyId) =>
        new()
        {
            Name = shape.Name,
            Anchor = RemapAddress(shape.Anchor, copyId),
            AnchorOffsetX = shape.AnchorOffsetX,
            AnchorOffsetY = shape.AnchorOffsetY,
            Kind = shape.Kind,
            Width = shape.Width,
            Height = shape.Height,
            RotationDegrees = shape.RotationDegrees,
            FlipHorizontal = shape.FlipHorizontal,
            FlipVertical = shape.FlipVertical,
            IsVisible = shape.IsVisible,
            HasFill = shape.HasFill,
            Locked = shape.Locked,
            Title = shape.Title,
            AltText = shape.AltText,
            FillColor = shape.FillColor,
            OutlineColor = shape.OutlineColor,
            GradientFillEndColor = shape.GradientFillEndColor,
            GradientFillDirection = shape.GradientFillDirection,
            FillThemeColor = shape.FillThemeColor,
            OutlineThemeColor = shape.OutlineThemeColor,
            HasShadowEffect = shape.HasShadowEffect,
            EffectPreset = shape.EffectPreset,
            UsesThemeEffects = shape.UsesThemeEffects,
            // R127B-clone-editas-parity: see the matching comment on CloneTextBox's DrawingAnchorKind copy.
            DrawingAnchorKind = shape.DrawingAnchorKind,
            // R97-model-drawing-hyperlink-2-2: see the matching comment on CloneTextBox's Hyperlink copy.
            Hyperlink = shape.Hyperlink,
            // A source-loaded shape's on-disk part is preserved by keying source drawing parts by
            // sheet NAME (XlsxFileAdapter.SavePostProcessing.GetSourceDrawingPathsBySheet); the
            // duplicate always gets a brand-new sheet name (e.g. "Sheet1 (2)") that is absent from
            // the source package, so no source part is ever mapped to it and the writer's
            // IsSourceLoaded-skipping emission drops it — the shape would be silently dropped on
            // save. Mark the clone as NOT source-loaded so it round-trips through the normal shape
            // writer like any other authored shape, mirroring ClonePicture below.
            IsSourceLoaded = false,
            AdjustValues = shape.AdjustValues,
            OutlineWidthPoints = shape.OutlineWidthPoints,
            OutlineHasNoFill = shape.OutlineHasNoFill,
            OutlineDash = shape.OutlineDash,
            HeadArrowhead = shape.HeadArrowhead,
            TailArrowhead = shape.TailArrowhead,
            // R90-shape-5-3: carry the connector's shape-attachment endpoints (stCxn/endCxn) into the
            // clone too -- otherwise duplicating a sheet containing a connector attached to another
            // shape silently drops that attachment, since the clone is marked NOT source-loaded (see
            // IsSourceLoaded above) and goes through the regenerated-element writer path, which only
            // emits what this model carries.
            StartConnectedShapeId = shape.StartConnectedShapeId,
            StartConnectedShapeConnectionIndex = shape.StartConnectedShapeConnectionIndex,
            EndConnectedShapeId = shape.EndConnectedShapeId,
            EndConnectedShapeConnectionIndex = shape.EndConnectedShapeConnectionIndex,
            IsWordArt = shape.IsWordArt,
            WarpPreset = shape.WarpPreset,
            ShapeTextGradientEndColor = shape.ShapeTextGradientEndColor,
            ShapeTextGradientEndThemeColor = shape.ShapeTextGradientEndThemeColor,
            ShapeTextGradientAngle = shape.ShapeTextGradientAngle,
            ShapeTextOutlineColor = shape.ShapeTextOutlineColor,
            ShapeTextOutlineThemeColor = shape.ShapeTextOutlineThemeColor,
            ShapeTextOutlineWidthPoints = shape.ShapeTextOutlineWidthPoints,
            ShapeText = shape.ShapeText,
            ShapeTextFontSizePoints = shape.ShapeTextFontSizePoints,
            ShapeTextBold = shape.ShapeTextBold,
            ShapeTextItalic = shape.ShapeTextItalic,
            ShapeTextUnderline = shape.ShapeTextUnderline,
            ShapeTextColor = shape.ShapeTextColor,
            ShapeTextThemeColor = shape.ShapeTextThemeColor,
            ShapeTextHAlign = shape.ShapeTextHAlign,
            ShapeTextVAnchor = shape.ShapeTextVAnchor,
            ShapeTextWrap = shape.ShapeTextWrap
        };

    /// <summary>
    /// Also used by <c>DuplicateDrawingObjectCommand</c> (single-object Ctrl+C/Ctrl+V) --
    /// R92-consumer-wiring-sweep-2 -- completing the Picture case DuplicateDrawingObjectCommand's
    /// Chart/Shape cases already had (R91-io-clipboard-image-formats-5-1). Bumped from private to
    /// internal for that reuse, mirroring <see cref="CloneDrawingShape"/>/<see cref="CloneTextBox"/>.
    /// </summary>
    internal static PictureModel ClonePicture(
        PictureModel picture,
        SheetId sourceSheetId,
        string sourceSheetName,
        string copySheetName,
        SheetId copyId)
    {
        var copiedPicture = new PictureModel
        {
            Name = picture.Name,
            Anchor = RemapAddress(picture.Anchor, copyId),
            AnchorOffsetX = picture.AnchorOffsetX,
            AnchorOffsetY = picture.AnchorOffsetY,
            Kind = picture.Kind,
            SourceRowCount = picture.SourceRowCount,
            SourceColumnCount = picture.SourceColumnCount,
            IsLinkedToSourceRange = picture.IsLinkedToSourceRange,
            LinkedSourceRange = picture.LinkedSourceRange is { } linkedSourceRange &&
                linkedSourceRange.Start.Sheet == sourceSheetId
                    ? RemapRange(linkedSourceRange, copyId)
                    : picture.LinkedSourceRange,
            LinkedSourceSheetName = picture.LinkedSourceSheetName == sourceSheetName
                ? copySheetName
                : picture.LinkedSourceSheetName,
            ImageBytes = picture.ImageBytes?.ToArray(),
            ContentType = picture.ContentType,
            // R80-io-drawing-image-5-3: an Insert > Icons/SVG picture keeps a PNG rasterization in
            // ImageBytes as the compatibility fallback but carries the editable vector original in
            // SvgImageBytes. Copying ImageBytes without this would silently downgrade the duplicated
            // picture to a flat PNG, re-introducing the same drop the round-80 fix addressed on the
            // original-picture path. Defensive-copy the array to match ImageBytes above so the
            // duplicate owns its own bytes rather than aliasing the source picture's.
            SvgImageBytes = picture.SvgImageBytes?.ToArray(),
            // R65-io-image-drawing-6-1: for a "Link to File" picture, ImageBytes is null and this
            // r:link external target is the picture's ONLY image reference — dropping it here would
            // leave the duplicate with no image at all.
            LinkedImageTarget = picture.LinkedImageTarget,
            Title = picture.Title,
            AltText = picture.AltText,
            // R91-print-twin-two-tier-synthetic-sweep-2: preserve the r90 "Mark as decorative" flag
            // on the clone -- without it, a duplicated decorative picture reverts to the default
            // false and falsely fails AccessibilityCheckerService's missing-alt-text rule even
            // though real Excel keeps the decorative marking across Move-or-Copy/Duplicate Sheet.
            IsDecorative = picture.IsDecorative,
            // R127B-clone-editas-parity: see the matching comment on CloneTextBox's DrawingAnchorKind copy.
            DrawingAnchorKind = picture.DrawingAnchorKind,
            Width = picture.Width,
            Height = picture.Height,
            LockAspectRatio = picture.LockAspectRatio,
            RotationDegrees = picture.RotationDegrees,
            FlipHorizontal = picture.FlipHorizontal,
            FlipVertical = picture.FlipVertical,
            IsVisible = picture.IsVisible,
            CropLeft = picture.CropLeft,
            CropTop = picture.CropTop,
            CropRight = picture.CropRight,
            CropBottom = picture.CropBottom,
            // R97-model-drawing-hyperlink-2-2: see the matching comment on CloneTextBox's Hyperlink copy.
            Hyperlink = picture.Hyperlink,
            // A source-loaded picture's on-disk part is preserved by keying source drawing parts by
            // sheet NAME (XlsxFileAdapter.SavePostProcessing.GetSourceDrawingPathsBySheet); the
            // duplicate always gets a brand-new name (e.g. "Sheet1 (2)") that is absent from the
            // source package, so no source part is ever mapped to it and the writer's
            // !IsSourceLoaded-only emission (XlsxWorksheetDrawingObjectWriter.IsSupportedPicture)
            // skips it too — the picture would be silently dropped on save. The already-copied raw
            // ImageBytes/ContentType fully support authoring it fresh instead, so mark the clone as
            // NOT source-loaded so it round-trips through the normal picture writer like any other
            // authored picture, matching Excel (the pasted copy is a normal embedded picture).
            IsSourceLoaded = false
        };

        foreach (var cell in picture.Cells)
            copiedPicture.Cells.Add(cell);

        return copiedPicture;
    }

    /// <summary>
    /// Also used by <c>DuplicateDrawingObjectCommand</c> (single-object Ctrl+C/Ctrl+V, not just
    /// whole-sheet Duplicate Sheet) -- R91-io-clipboard-image-formats-5-1 -- and by
    /// <c>PasteChartsCommand</c> (plain-range-copy floating-object carry) --
    /// R92-cmd-paste-floating-objects. Bumped from private to internal for that reuse rather than
    /// duplicating this ~250-property clone list a second time.
    /// </summary>
    /// <param name="remapSameSheetDataRange">
    /// True only for the whole-sheet "Duplicate Sheet" caller, where the copy is a parallel sheet
    /// containing its own copy of the data -- a same-sheet DataRange must follow the duplicate onto
    /// the copy sheet (Excel's Duplicate Sheet behavior). Every other caller (plain Ctrl+V of a
    /// chart-carrying range, or Ctrl+C/Ctrl+V of a selected chart object) duplicates only the chart
    /// itself, not the data it plots, so the DataRange -- and any verbatim series/error-bar formula
    /// text -- must keep pointing at the exact original source sheet/cells unchanged, regardless of
    /// where the duplicate lands (R94-cmd-paste-charts-cross-sheet-dataRange). Defaults to true so
    /// existing Duplicate Sheet call sites are unaffected.
    /// </param>
    internal static ChartModel CloneChart(
        ChartModel chart, SheetId sourceSheetId, SheetId copyId, bool remapSameSheetDataRange = true) =>
        new()
        {
            Name = chart.Name,
            AltTextTitle = chart.AltTextTitle,
            AltTextDescription = chart.AltTextDescription,
            // R98-io-chart-hyperlink-model-field: carry the object-level hyperlink onto the clone --
            // without this, a copy-pasted/duplicated chart's hyperlink could only ever be found by
            // falling back to the fragile sheet-name-keyed source-package lookup, which (per the same
            // name both charts now share) either drops it or misattributes it. Mirrors ClonePicture/
            // CloneTextBox/CloneDrawingShape's identical Hyperlink = ... copy (R97-model-drawing-hyperlink-2-2).
            Hyperlink = chart.Hyperlink,
            Type = chart.Type,
            Uses1904DateSystem = chart.Uses1904DateSystem,
            Language = chart.Language,
            ChartStyleId = chart.ChartStyleId,
            ColorMapOverride = chart.ColorMapOverride,
            ExternalData = chart.ExternalData,
            // Only remap the DataRange onto the copy when it actually points at the sheet being
            // duplicated — a cross-sheet DataRange (e.g. a Dashboard chart plotting Data!A1:B10)
            // must keep pointing at the original source sheet, matching Excel's Duplicate Sheet
            // behavior (only same-sheet references travel with the copy). Non-Duplicate-Sheet
            // callers pass remapSameSheetDataRange:false so the DataRange is always left verbatim.
            DataRange = remapSameSheetDataRange && chart.DataRange.Start.Sheet == sourceSheetId
                ? RemapRange(chart.DataRange, copyId)
                : chart.DataRange,
            IsVisible = chart.IsVisible,
            FirstRowIsHeader = chart.FirstRowIsHeader,
            FirstColIsCategories = chart.FirstColIsCategories,
            IsPivotChart = chart.IsPivotChart,
            PivotSourceSheetName = chart.PivotSourceSheetName,
            PivotTableName = chart.PivotTableName,
            PivotSourceFormatId = chart.PivotSourceFormatId,
            PivotCacheId = chart.PivotCacheId,
            PivotFormatsXml = chart.PivotFormatsXml,
            ShowPivotChartFieldButtons = chart.ShowPivotChartFieldButtons,
            ShowPivotChartReportFilterButtons = chart.ShowPivotChartReportFilterButtons,
            ShowPivotChartAxisFieldButtons = chart.ShowPivotChartAxisFieldButtons,
            ShowPivotChartValueFieldButtons = chart.ShowPivotChartValueFieldButtons,
            Title = chart.Title,
            TitleLayout = chart.TitleLayout,
            TitleOverlay = chart.TitleOverlay,
            XAxisTitle = chart.XAxisTitle,
            XAxisTitleLayout = chart.XAxisTitleLayout,
            XAxisTitleVerbatimXml = chart.XAxisTitleVerbatimXml,
            XAxisTitleRotation = chart.XAxisTitleRotation,
            YAxisTitle = chart.YAxisTitle,
            YAxisTitleLayout = chart.YAxisTitleLayout,
            YAxisTitleVerbatimXml = chart.YAxisTitleVerbatimXml,
            YAxisTitleRotation = chart.YAxisTitleRotation,
            PlotAreaLayout = chart.PlotAreaLayout,
            LegendLayout = chart.LegendLayout,
            RoundedCorners = chart.RoundedCorners,
            BlankDisplayMode = chart.BlankDisplayMode,
            ShowDataLabelsOverMaximum = chart.ShowDataLabelsOverMaximum,
            AutoTitleDeleted = chart.AutoTitleDeleted,
            ShowDataInHiddenRowsAndColumns = chart.ShowDataInHiddenRowsAndColumns,
            Protection = chart.Protection,
            SeriesInRows = chart.SeriesInRows,
            HideXAxis = chart.HideXAxis,
            HideYAxis = chart.HideYAxis,
            XAxisPosition = chart.XAxisPosition,
            YAxisPosition = chart.YAxisPosition,
            ChartDefaultTextColor = chart.ChartDefaultTextColor,
            ChartDefaultTextThemeColor = chart.ChartDefaultTextThemeColor,
            ChartDefaultFontSize = chart.ChartDefaultFontSize,
            ChartTitleTextColor = chart.ChartTitleTextColor,
            ChartTitleTextThemeColor = chart.ChartTitleTextThemeColor,
            ChartTitleFontSize = chart.ChartTitleFontSize,
            AxisTitleTextColor = chart.AxisTitleTextColor,
            AxisTitleTextThemeColor = chart.AxisTitleTextThemeColor,
            AxisTitleFontSize = chart.AxisTitleFontSize,
            XAxisTitleFontSize = chart.XAxisTitleFontSize,
            XAxisTitleTextColor = chart.XAxisTitleTextColor,
            XAxisTitleTextThemeColor = chart.XAxisTitleTextThemeColor,
            YAxisTitleFontSize = chart.YAxisTitleFontSize,
            YAxisTitleTextColor = chart.YAxisTitleTextColor,
            YAxisTitleTextThemeColor = chart.YAxisTitleTextThemeColor,
            ChartAreaFillColor = chart.ChartAreaFillColor,
            ChartAreaFillThemeColor = chart.ChartAreaFillThemeColor,
            ChartAreaNoFill = chart.ChartAreaNoFill,
            ChartAreaBorderColor = chart.ChartAreaBorderColor,
            ChartAreaBorderThemeColor = chart.ChartAreaBorderThemeColor,
            ChartAreaBorderThickness = chart.ChartAreaBorderThickness,
            ChartAreaNoLine = chart.ChartAreaNoLine,
            PlotAreaFillColor = chart.PlotAreaFillColor,
            PlotAreaFillThemeColor = chart.PlotAreaFillThemeColor,
            PlotAreaNoFill = chart.PlotAreaNoFill,
            PlotAreaBorderColor = chart.PlotAreaBorderColor,
            PlotAreaBorderThemeColor = chart.PlotAreaBorderThemeColor,
            PlotAreaBorderThickness = chart.PlotAreaBorderThickness,
            PlotAreaNoLine = chart.PlotAreaNoLine,
            LegendTextColor = chart.LegendTextColor,
            LegendTextThemeColor = chart.LegendTextThemeColor,
            LegendFillColor = chart.LegendFillColor,
            LegendFillThemeColor = chart.LegendFillThemeColor,
            LegendBorderColor = chart.LegendBorderColor,
            LegendBorderThemeColor = chart.LegendBorderThemeColor,
            LegendBorderThickness = chart.LegendBorderThickness,
            LegendFontSize = chart.LegendFontSize,
            LegendBold = chart.LegendBold,
            LegendItalic = chart.LegendItalic,
            LegendEntries = chart.LegendEntries.ToList(),
            SeriesColumnMappings = chart.SeriesColumnMappings.ToList(),
            SeriesRangeDataLabels = chart.SeriesRangeDataLabels.ToList(),
            VerbatimSeriesFormulas = chart.VerbatimSeriesFormulas?.ToList(),
            EmbeddedSeriesData = chart.EmbeddedSeriesData?.ToList(),
            DoughnutHoleSize = chart.DoughnutHoleSize,
            FirstSliceAngle = chart.FirstSliceAngle,
            ExplodedSliceIndex = chart.ExplodedSliceIndex,
            ExplodedSliceDistance = chart.ExplodedSliceDistance,
            ExplodedSlices = chart.ExplodedSlices.ToList(),
            XAxisMinimum = chart.XAxisMinimum,
            XAxisMaximum = chart.XAxisMaximum,
            XAxisMajorUnit = chart.XAxisMajorUnit,
            XAxisMinorUnit = chart.XAxisMinorUnit,
            XAxisLogScale = chart.XAxisLogScale,
            XAxisLogBase = chart.XAxisLogBase,
            XAxisReverseOrder = chart.XAxisReverseOrder,
            XAxisNumberFormat = chart.XAxisNumberFormat,
            XAxisNumberFormatCode = chart.XAxisNumberFormatCode,
            XAxisNumberFormatSourceLinked = chart.XAxisNumberFormatSourceLinked,
            ShowXAxisMajorGridlines = chart.ShowXAxisMajorGridlines,
            ShowXAxisMinorGridlines = chart.ShowXAxisMinorGridlines,
            XAxisIsDateAxis = chart.XAxisIsDateAxis,
            XAxisMajorGridlineColor = chart.XAxisMajorGridlineColor,
            XAxisMinorGridlineColor = chart.XAxisMinorGridlineColor,
            XAxisGridlineThickness = chart.XAxisGridlineThickness,
            XAxisMajorTickStyle = chart.XAxisMajorTickStyle,
            XAxisMinorTickStyle = chart.XAxisMinorTickStyle,
            ShowXAxisLabels = chart.ShowXAxisLabels,
            XAxisTickLabelPosition = chart.XAxisTickLabelPosition,
            XAxisLabelTextColor = chart.XAxisLabelTextColor,
            XAxisLabelTextThemeColor = chart.XAxisLabelTextThemeColor,
            XAxisLabelFontSize = chart.XAxisLabelFontSize,
            XAxisLabelAngle = chart.XAxisLabelAngle,
            XAxisLabelSkip = chart.XAxisLabelSkip,
            XAxisTickMarkSkip = chart.XAxisTickMarkSkip,
            XAxisLabelOffset = chart.XAxisLabelOffset,
            XAxisNoMultiLevelLabels = chart.XAxisNoMultiLevelLabels,
            XAxisLabelAlignment = chart.XAxisLabelAlignment,
            XAxisBaseTimeUnit = chart.XAxisBaseTimeUnit,
            XAxisMajorTimeUnit = chart.XAxisMajorTimeUnit,
            XAxisMinorTimeUnit = chart.XAxisMinorTimeUnit,
            XAxisLineColor = chart.XAxisLineColor,
            XAxisLineThickness = chart.XAxisLineThickness,
            XAxisCrosses = chart.XAxisCrosses,
            XAxisCrossesAt = chart.XAxisCrossesAt,
            XAxisCrossBetween = chart.XAxisCrossBetween,
            XAxisDisplayUnit = chart.XAxisDisplayUnit,
            XAxisCustomDisplayUnit = chart.XAxisCustomDisplayUnit,
            YAxisMinimum = chart.YAxisMinimum,
            YAxisMaximum = chart.YAxisMaximum,
            YAxisMajorUnit = chart.YAxisMajorUnit,
            YAxisMinorUnit = chart.YAxisMinorUnit,
            YAxisLogScale = chart.YAxisLogScale,
            YAxisLogBase = chart.YAxisLogBase,
            YAxisReverseOrder = chart.YAxisReverseOrder,
            YAxisNumberFormat = chart.YAxisNumberFormat,
            YAxisNumberFormatCode = chart.YAxisNumberFormatCode,
            YAxisNumberFormatSourceLinked = chart.YAxisNumberFormatSourceLinked,
            ShowYAxisMajorGridlines = chart.ShowYAxisMajorGridlines,
            ShowYAxisMinorGridlines = chart.ShowYAxisMinorGridlines,
            YAxisMajorGridlineColor = chart.YAxisMajorGridlineColor,
            YAxisMinorGridlineColor = chart.YAxisMinorGridlineColor,
            YAxisGridlineThickness = chart.YAxisGridlineThickness,
            YAxisMajorTickStyle = chart.YAxisMajorTickStyle,
            YAxisMinorTickStyle = chart.YAxisMinorTickStyle,
            ShowYAxisLabels = chart.ShowYAxisLabels,
            YAxisTickLabelPosition = chart.YAxisTickLabelPosition,
            YAxisLabelTextColor = chart.YAxisLabelTextColor,
            YAxisLabelTextThemeColor = chart.YAxisLabelTextThemeColor,
            YAxisLabelFontSize = chart.YAxisLabelFontSize,
            YAxisLabelAngle = chart.YAxisLabelAngle,
            YAxisLineColor = chart.YAxisLineColor,
            YAxisLineThickness = chart.YAxisLineThickness,
            YAxisCrosses = chart.YAxisCrosses,
            YAxisCrossesAt = chart.YAxisCrossesAt,
            YAxisCrossBetween = chart.YAxisCrossBetween,
            YAxisDisplayUnit = chart.YAxisDisplayUnit,
            YAxisCustomDisplayUnit = chart.YAxisCustomDisplayUnit,
            ShowXAxisDisplayUnitLabel = chart.ShowXAxisDisplayUnitLabel,
            ShowYAxisDisplayUnitLabel = chart.ShowYAxisDisplayUnitLabel,
            DataTable = chart.DataTable is null
                ? null
                : new ChartDataTableModel
                {
                    ShowHorizontalBorder = chart.DataTable.ShowHorizontalBorder,
                    ShowVerticalBorder = chart.DataTable.ShowVerticalBorder,
                    ShowOutline = chart.DataTable.ShowOutline,
                    ShowLegendKeys = chart.DataTable.ShowLegendKeys,
                    FillColor = chart.DataTable.FillColor,
                    FillThemeColor = chart.DataTable.FillThemeColor,
                    BorderColor = chart.DataTable.BorderColor,
                    BorderThemeColor = chart.DataTable.BorderThemeColor,
                    BorderThickness = chart.DataTable.BorderThickness,
                    TextColor = chart.DataTable.TextColor,
                    TextThemeColor = chart.DataTable.TextThemeColor,
                    FontSize = chart.DataTable.FontSize
            },
            ThreeDView = chart.ThreeDView,
            FloorFormat = CloneSurfaceFormat(chart.FloorFormat),
            SideWallFormat = CloneSurfaceFormat(chart.SideWallFormat),
            BackWallFormat = CloneSurfaceFormat(chart.BackWallFormat),
            PrintSettings = ClonePrintSettings(chart.PrintSettings),
            UserShapes = CloneUserShapes(chart.UserShapes),
            BarGapWidth = chart.BarGapWidth,
            BarOverlap = chart.BarOverlap,
            VaryColorsByPoint = chart.VaryColorsByPoint,
            BubbleScale = chart.BubbleScale,
            ShowNegativeBubbles = chart.ShowNegativeBubbles,
            BubbleSizeRepresents = chart.BubbleSizeRepresents,
            StockSubtype = chart.StockSubtype,
            LegendPosition = chart.LegendPosition,
            LegendPositionExplicit = chart.LegendPositionExplicit,
            LegendOverlay = chart.LegendOverlay,
            ShowLegend = chart.ShowLegend,
            ShowDataLabels = chart.ShowDataLabels,
            DataLabelPosition = chart.DataLabelPosition,
            ShowDataLabelValue = chart.ShowDataLabelValue,
            ShowDataLabelLegendKey = chart.ShowDataLabelLegendKey,
            ShowDataLabelBubbleSize = chart.ShowDataLabelBubbleSize,
            ShowDataLabelCategoryName = chart.ShowDataLabelCategoryName,
            ShowDataLabelSeriesName = chart.ShowDataLabelSeriesName,
            ShowDataLabelPercentage = chart.ShowDataLabelPercentage,
            DataLabelSeparator = chart.DataLabelSeparator,
            DataLabelSeparatorText = chart.DataLabelSeparatorText,
            DataLabelNumberFormat = chart.DataLabelNumberFormat,
            DataLabelNumberFormatCode = chart.DataLabelNumberFormatCode,
            DataLabelNumberFormatSourceLinked = chart.DataLabelNumberFormatSourceLinked,
            ShowDataLabelCallouts = chart.ShowDataLabelCallouts,
            DataLabelFillColor = chart.DataLabelFillColor,
            DataLabelFillThemeColor = chart.DataLabelFillThemeColor,
            DataLabelBorderColor = chart.DataLabelBorderColor,
            DataLabelBorderThemeColor = chart.DataLabelBorderThemeColor,
            DataLabelTextColor = chart.DataLabelTextColor,
            DataLabelTextThemeColor = chart.DataLabelTextThemeColor,
            DataLabelBorderThickness = chart.DataLabelBorderThickness,
            DataLabelFontSize = chart.DataLabelFontSize,
            DataLabelAngle = chart.DataLabelAngle,
            DataLabelLeaderLineColor = chart.DataLabelLeaderLineColor,
            DataLabelLeaderLineThemeColor = chart.DataLabelLeaderLineThemeColor,
            DataLabelLeaderLineThickness = chart.DataLabelLeaderLineThickness,
            DataLabelLeaderLineDashStyle = chart.DataLabelLeaderLineDashStyle,
            ShowLinearTrendline = chart.ShowLinearTrendline,
            TrendlineSeriesIndex = chart.TrendlineSeriesIndex,
            TrendlineName = chart.TrendlineName,
            TrendlineType = chart.TrendlineType,
            TrendlinePeriod = chart.TrendlinePeriod,
            TrendlineOrder = chart.TrendlineOrder,
            TrendlineForward = chart.TrendlineForward,
            TrendlineBackward = chart.TrendlineBackward,
            TrendlineIntercept = chart.TrendlineIntercept,
            ShowTrendlineEquation = chart.ShowTrendlineEquation,
            ShowTrendlineRSquared = chart.ShowTrendlineRSquared,
            TrendlineLabelNumberFormatCode = chart.TrendlineLabelNumberFormatCode,
            TrendlineLabelNumberFormatSourceLinked = chart.TrendlineLabelNumberFormatSourceLinked,
            TrendlineLabelLayout = chart.TrendlineLabelLayout,
            TrendlineLabelFillColor = chart.TrendlineLabelFillColor,
            TrendlineLabelFillThemeColor = chart.TrendlineLabelFillThemeColor,
            TrendlineLabelBorderColor = chart.TrendlineLabelBorderColor,
            TrendlineLabelBorderThemeColor = chart.TrendlineLabelBorderThemeColor,
            TrendlineLabelBorderThickness = chart.TrendlineLabelBorderThickness,
            TrendlineLabelTextColor = chart.TrendlineLabelTextColor,
            TrendlineLabelTextThemeColor = chart.TrendlineLabelTextThemeColor,
            TrendlineLabelFontSize = chart.TrendlineLabelFontSize,
            TrendlineLabelAngle = chart.TrendlineLabelAngle,
            TrendlineColor = chart.TrendlineColor,
            TrendlineThemeColor = chart.TrendlineThemeColor,
            TrendlineThickness = chart.TrendlineThickness,
            TrendlineDashStyle = chart.TrendlineDashStyle,
            ShowErrorBars = chart.ShowErrorBars,
            ErrorBarSeriesIndex = chart.ErrorBarSeriesIndex,
            ErrorBarKind = chart.ErrorBarKind,
            ErrorBarAxisDirection = chart.ErrorBarAxisDirection,
            ErrorBarDirection = chart.ErrorBarDirection,
            ErrorBarValue = chart.ErrorBarValue,
            ErrorBarPlusRangeFormula = chart.ErrorBarPlusRangeFormula,
            ErrorBarMinusRangeFormula = chart.ErrorBarMinusRangeFormula,
            ErrorBarPlusRangeCacheXml = chart.ErrorBarPlusRangeCacheXml,
            ErrorBarMinusRangeCacheXml = chart.ErrorBarMinusRangeCacheXml,
            ErrorBarEndCaps = chart.ErrorBarEndCaps,
            ErrorBarColor = chart.ErrorBarColor,
            ErrorBarThemeColor = chart.ErrorBarThemeColor,
            ErrorBarThickness = chart.ErrorBarThickness,
            ErrorBarDashStyle = chart.ErrorBarDashStyle,
            ShowDropLines = chart.ShowDropLines,
            DropLineColor = chart.DropLineColor,
            DropLineThemeColor = chart.DropLineThemeColor,
            DropLineThickness = chart.DropLineThickness,
            DropLineDashStyle = chart.DropLineDashStyle,
            ShowHighLowLines = chart.ShowHighLowLines,
            HighLowLineColor = chart.HighLowLineColor,
            HighLowLineThemeColor = chart.HighLowLineThemeColor,
            HighLowLineThickness = chart.HighLowLineThickness,
            HighLowLineDashStyle = chart.HighLowLineDashStyle,
            ShowSeriesLines = chart.ShowSeriesLines,
            SeriesLineColor = chart.SeriesLineColor,
            SeriesLineThemeColor = chart.SeriesLineThemeColor,
            SeriesLineThickness = chart.SeriesLineThickness,
            SeriesLineDashStyle = chart.SeriesLineDashStyle,
            ShowUpDownBars = chart.ShowUpDownBars,
            UpDownBarGapWidth = chart.UpDownBarGapWidth,
            UpBarFillColor = chart.UpBarFillColor,
            UpBarFillThemeColor = chart.UpBarFillThemeColor,
            UpBarBorderColor = chart.UpBarBorderColor,
            UpBarBorderThemeColor = chart.UpBarBorderThemeColor,
            UpBarBorderThickness = chart.UpBarBorderThickness,
            DownBarFillColor = chart.DownBarFillColor,
            DownBarFillThemeColor = chart.DownBarFillThemeColor,
            DownBarBorderColor = chart.DownBarBorderColor,
            DownBarBorderThemeColor = chart.DownBarBorderThemeColor,
            DownBarBorderThickness = chart.DownBarBorderThickness,
            ShowSecondaryAxis = chart.ShowSecondaryAxis,
            SecondaryAxisSeriesIndexes = chart.SecondaryAxisSeriesIndexes.ToList(),
            SecondaryAxisTitle = chart.SecondaryAxisTitle,
            SecondaryAxisMinimum = chart.SecondaryAxisMinimum,
            SecondaryAxisMaximum = chart.SecondaryAxisMaximum,
            SecondaryAxisMajorUnit = chart.SecondaryAxisMajorUnit,
            SecondaryAxisMinorUnit = chart.SecondaryAxisMinorUnit,
            SecondaryAxisNumberFormat = chart.SecondaryAxisNumberFormat,
            SecondaryAxisNumberFormatCode = chart.SecondaryAxisNumberFormatCode,
            SecondaryAxisNumberFormatSourceLinked = chart.SecondaryAxisNumberFormatSourceLinked,
            SecondaryAxisReverseOrder = chart.SecondaryAxisReverseOrder,
            SecondaryAxisLogScale = chart.SecondaryAxisLogScale,
            SecondaryAxisLogBase = chart.SecondaryAxisLogBase,
            SecondaryAxisMajorTickStyle = chart.SecondaryAxisMajorTickStyle,
            SecondaryAxisMinorTickStyle = chart.SecondaryAxisMinorTickStyle,
            SecondaryAxisCrosses = chart.SecondaryAxisCrosses,
            SecondaryAxisCrossesAt = chart.SecondaryAxisCrossesAt,
            SecondaryAxisCrossBetween = chart.SecondaryAxisCrossBetween,
            SecondaryAxisDisplayUnit = chart.SecondaryAxisDisplayUnit,
            SecondaryAxisCustomDisplayUnit = chart.SecondaryAxisCustomDisplayUnit,
            ShowSecondaryAxisDisplayUnitLabel = chart.ShowSecondaryAxisDisplayUnitLabel,
            ComboLineSeriesIndexes = chart.ComboLineSeriesIndexes.ToList(),
            ComboScatterSeriesIndexes = chart.ComboScatterSeriesIndexes.ToList(),
            SeriesPlotOrder = chart.SeriesPlotOrder.ToList(),
            SeriesFormats = chart.SeriesFormats.ToList(),
            SeriesDataLabelFormats = chart.SeriesDataLabelFormats.ToList(),
            PointDataLabelFormats = chart.PointDataLabelFormats.ToList(),
            RangeDataLabels = chart.RangeDataLabels.ToList(),
            PointFillColors = chart.PointFillColors.ToList(),
            SeriesOrderOverrides = chart.SeriesOrderOverrides.ToList(),
            MultiLevelCategoryXml = chart.MultiLevelCategoryXml.ToList(),
            PointMarkerFormats = chart.PointMarkerFormats.ToList(),
            HistogramBinning = chart.HistogramBinning,
            WaterfallTotalPointIndices = chart.WaterfallTotalPointIndices?.ToList(),
            QuartileMethod = chart.QuartileMethod,
            AdditionalPlotGroupDataLabels = chart.AdditionalPlotGroupDataLabels.ToList(),
            AdditionalSeriesErrorBarsXml = chart.AdditionalSeriesErrorBarsXml.ToList(),
            AdditionalSeriesTrendlinesXml = chart.AdditionalSeriesTrendlinesXml.ToList(),
            UseComboLineForSecondarySeries = chart.UseComboLineForSecondarySeries,
            Left = chart.Left,
            Top = chart.Top,
            Width = chart.Width,
            Height = chart.Height,
            DrawingAnchorKind = chart.DrawingAnchorKind
        };

    private static CellAddress RemapAddress(CellAddress address, SheetId sheetId) =>
        new(sheetId, address.Row, address.Col);

    private static GridRange RemapRange(GridRange range, SheetId sheetId) =>
        new(RemapAddress(range.Start, sheetId), RemapAddress(range.End, sheetId));

    /// <summary>
    /// Rewrites the custom error-bar range formulas (raw <c>&lt;c:f&gt;</c> text, same
    /// verbatim/multi-area-union form as <see cref="ChartModel.VerbatimSeriesFormulas"/>) so a
    /// same-sheet reference travels with the duplicate instead of still pointing at the source
    /// sheet by name.
    /// </summary>
    private static void RewriteErrorBarRangeFormulas(ChartModel chart, RenameSheetOp op, string hostSheetName)
    {
        if (chart.ErrorBarPlusRangeFormula is { } plus)
        {
            var rewritten = RewriteVerbatimRangeText(plus, op, hostSheetName);
            if (rewritten is not null)
                chart.ErrorBarPlusRangeFormula = rewritten;
        }

        if (chart.ErrorBarMinusRangeFormula is { } minus)
        {
            var rewritten = RewriteVerbatimRangeText(minus, op, hostSheetName);
            if (rewritten is not null)
                chart.ErrorBarMinusRangeFormula = rewritten;
        }
    }

    /// <summary>
    /// Rewrites a raw (non-"="-prefixed) <c>&lt;c:f&gt;</c> range formula that may be a
    /// comma-separated multi-area union wrapped in parentheses, e.g.
    /// <c>(Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5)</c>. Mirrors
    /// <c>RowColumnShiftHelpers.RewriteVerbatimFormula</c>'s wrapper handling since that helper is
    /// private to its own file. Returns <see langword="null"/> when nothing changed.
    /// </summary>
    /// <remarks>
    /// Excel writes/accepts multi-area unions where only the FIRST area of the union is
    /// sheet-qualified and later areas omit the sheet name, e.g.
    /// <c>(Sheet1!$A$1:$A$5,$C$1:$C$5)</c> — the unqualified areas implicitly mean "same sheet
    /// as the union's first (or nearest preceding qualified) area", not "current/host sheet".
    /// <see cref="FormulaRewriter.Rewrite"/> only rewrites <see cref="RenameSheetOp"/> references
    /// that already carry an explicit sheet qualifier, so splitting the union and rewriting each
    /// area independently would silently leave later unqualified areas untouched — detached from
    /// the sheet being renamed. To avoid that, each area inherits the nearest preceding explicit
    /// sheet qualifier in the union before being rewritten, and the rewritten qualifier is made
    /// explicit on that area so it travels with the duplicate/rename unambiguously.
    /// </remarks>
    private static string? RewriteVerbatimRangeText(string text, RenameSheetOp op, string hostSheetName)
    {
        var hasPrefix = text.Length > 0 && text[0] == '=';
        var body = hasPrefix ? text[1..] : text;

        var hasParens = body.Length >= 2 && body[0] == '(' && body[^1] == ')';
        if (hasParens)
            body = body[1..^1];

        var areas = SplitOnUnquotedCommas(body);
        var anyChanged = false;
        var rewrittenAreas = new string[areas.Length];
        string? inheritedSheetQualifier = null;
        for (var i = 0; i < areas.Length; i++)
        {
            var area = areas[i];
            var qualifier = TryExtractLeadingSheetQualifier(area);
            if (qualifier is not null)
            {
                inheritedSheetQualifier = qualifier;
            }
            else if (inheritedSheetQualifier is not null)
            {
                // This area has no sheet qualifier of its own; it implicitly belongs to the
                // nearest preceding qualified area's sheet. Make that explicit before rewriting
                // so FormulaRewriter's RenameSheetOp match (which requires a non-null sheet name)
                // can see and rewrite it too.
                area = inheritedSheetQualifier + "!" + area;
            }

            var rewritten = FormulaRewriter.Rewrite(area, op, hostSheetName);
            if (rewritten is not null && rewritten != areas[i])
            {
                rewrittenAreas[i] = rewritten;
                anyChanged = true;
            }
            else
            {
                rewrittenAreas[i] = areas[i];
            }
        }

        if (!anyChanged)
            return null;

        var newBody = string.Join(",", rewrittenAreas);
        if (hasParens)
            newBody = "(" + newBody + ")";
        return hasPrefix ? "=" + newBody : newBody;
    }

    /// <summary>
    /// Detects a leading sheet-name qualifier (<c>Sheet1</c> from <c>Sheet1!...</c>, or the quoted
    /// form <c>'Sheet 1'</c> from <c>'Sheet 1'!...</c>, quotes included) at the start of a single
    /// range/cell reference area and returns it (without the trailing <c>!</c>), or
    /// <see langword="null"/> if the area has no sheet qualifier. Only looks at the very start of
    /// the string since a verbatim area is a bare reference, not a general formula expression.
    /// </summary>
    private static string? TryExtractLeadingSheetQualifier(string area)
    {
        if (area.Length == 0)
            return null;

        if (area[0] == '\'')
        {
            var i = 1;
            while (i < area.Length)
            {
                if (area[i] == '\'')
                {
                    if (i + 1 < area.Length && area[i + 1] == '\'')
                    {
                        i += 2; // escaped quote inside the sheet name
                        continue;
                    }

                    // Closing quote found; must be immediately followed by '!'.
                    return i + 1 < area.Length && area[i + 1] == '!'
                        ? area[..(i + 1)]
                        : null;
                }

                i++;
            }

            return null;
        }

        var bang = area.IndexOf('!');
        if (bang <= 0)
            return null;

        // Bare (unquoted) sheet name: must not itself contain characters that would mean this
        // '!' belongs to something other than a leading sheet qualifier (e.g. a colon inside the
        // prefix would mean this isn't a simple "Sheet!" prefix).
        var candidate = area[..bang];
        return candidate.IndexOfAny([':', '$', '\'']) < 0 ? candidate : null;
    }

    /// <summary>
    /// Splits a comma-separated area-union string on commas that are not inside single-quoted
    /// sheet names (e.g. <c>'Sheet, Name'!A1</c> must not be split on the comma inside the quotes).
    /// Mirrors <c>RowColumnShiftHelpers.SplitOnUnquotedCommas</c>.
    /// </summary>
    private static string[] SplitOnUnquotedCommas(string text)
    {
        var parts = new List<string>();
        var start = 0;
        var inQuote = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\'')
            {
                if (inQuote && i + 1 < text.Length && text[i + 1] == '\'')
                    i++; // skip escaped quote
                else
                    inQuote = !inQuote;
            }
            else if (c == ',' && !inQuote)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts.ToArray();
    }

    private static GridRange? RemapRange(GridRange? range, SheetId sheetId) =>
        range.HasValue ? RemapRange(range.Value, sheetId) : null;

    private static ChartSurfaceFormatModel? CloneSurfaceFormat(ChartSurfaceFormatModel? format) =>
        format is null
            ? null
            : new ChartSurfaceFormatModel
            {
                FillColor = format.FillColor,
                FillThemeColor = format.FillThemeColor,
                BorderColor = format.BorderColor,
                BorderThemeColor = format.BorderThemeColor,
                BorderThickness = format.BorderThickness
            };

    private static ChartPrintSettingsModel? ClonePrintSettings(ChartPrintSettingsModel? printSettings) =>
        printSettings is null
            ? null
            : new ChartPrintSettingsModel
            {
                PageMargins = printSettings.PageMargins is null
                    ? null
                    : new ChartPageMarginsModel
                    {
                        Left = printSettings.PageMargins.Left,
                        Right = printSettings.PageMargins.Right,
                        Top = printSettings.PageMargins.Top,
                        Bottom = printSettings.PageMargins.Bottom,
                        Header = printSettings.PageMargins.Header,
                        Footer = printSettings.PageMargins.Footer
                    },
                PageSetup = printSettings.PageSetup is null
                    ? null
                    : new ChartPageSetupModel
                    {
                        PaperSize = printSettings.PageSetup.PaperSize,
                        Orientation = printSettings.PageSetup.Orientation,
                        Copies = printSettings.PageSetup.Copies,
                        UsePrinterDefaults = printSettings.PageSetup.UsePrinterDefaults,
                        FirstPageNumber = printSettings.PageSetup.FirstPageNumber,
                        UseFirstPageNumber = printSettings.PageSetup.UseFirstPageNumber,
                        HorizontalDpi = printSettings.PageSetup.HorizontalDpi,
                        VerticalDpi = printSettings.PageSetup.VerticalDpi,
                        BlackAndWhite = printSettings.PageSetup.BlackAndWhite,
                        Draft = printSettings.PageSetup.Draft
                    },
                HeaderFooter = printSettings.HeaderFooter is null
                    ? null
                    : new ChartHeaderFooterModel
                    {
                        DifferentOddEven = printSettings.HeaderFooter.DifferentOddEven,
                        DifferentFirst = printSettings.HeaderFooter.DifferentFirst,
                        AlignWithMargins = printSettings.HeaderFooter.AlignWithMargins,
                        OddHeader = printSettings.HeaderFooter.OddHeader,
                        OddFooter = printSettings.HeaderFooter.OddFooter,
                        EvenHeader = printSettings.HeaderFooter.EvenHeader,
                        EvenFooter = printSettings.HeaderFooter.EvenFooter,
                        FirstHeader = printSettings.HeaderFooter.FirstHeader,
                        FirstFooter = printSettings.HeaderFooter.FirstFooter
                    }
            };

    private static ChartUserShapesModel? CloneUserShapes(ChartUserShapesModel? userShapes) =>
        userShapes is null
            ? null
            : new ChartUserShapesModel
            {
                RelationshipId = userShapes.RelationshipId,
                RelationshipType = userShapes.RelationshipType,
                Target = userShapes.Target,
                TargetMode = userShapes.TargetMode
            };
}
