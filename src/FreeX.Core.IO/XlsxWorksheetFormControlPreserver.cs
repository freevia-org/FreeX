using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Preserves legacy Excel form controls across an edited save (the full-rebuild path). ClosedXML
/// regenerates each worksheet without the <c>controls</c> block or its <c>legacyDrawing</c> marker,
/// which orphans the (otherwise copied) VML/ctrlProps parts so Excel shows nothing. This preserver
/// copies the source worksheet's controls block and form-control <c>legacyDrawing</c> back into the
/// generated worksheet, then re-binds the relationship ids via the shared OLE-control normalizer so
/// the controls re-attach to their preserved ctrlProps and VML drawing.
/// </summary>
internal static class XlsxWorksheetFormControlPreserver
{
    private static readonly XNamespace McNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string VmlDrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";

    /// <summary>
    /// R115-io-deleted-form-control-1: describes a <c>&lt;control&gt;</c> that existed in the SOURCE
    /// worksheet but no longer has a corresponding live <see cref="FormControlModel"/> in
    /// <see cref="Sheet.FormControls"/> -- i.e. a control whose anchor was fully deleted by a
    /// row/column delete (see <c>RowColumnShiftHelpers.AddressState.ShiftFormControls</c>, which
    /// removes such controls from the in-memory model entirely). <see cref="RelationshipId"/> is the
    /// element's own <c>r:id</c> (used to locate and remove its now-orphaned ctrlProp part).
    /// </summary>
    private readonly record struct OrphanedControl(uint ShapeId, string? RelationshipId);

    // Mirrors XlsxFormControlMapper.EmusPerPixel: the VML x:ClientData/x:Anchor stores its
    // colOff/rowOff sub-cell offsets in pixels, while FormControlModel.AnchorOffsets carries EMU.
    private const long EmusPerPixel = 9525;

    public static void Preserve(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context,
        Workbook? workbook = null)
    {
        if (context is null)
            return;

        var anyChange = false;
        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            // R102-io-rename-worksheet-exclusion-sweep-1: sheetName is the LOAD-TIME name -- resolve
            // both the target worksheet path AND the sheet's CURRENT name via the shared
            // rename-tolerant fallback so a renamed sheet's form controls survive.
            if (!XlsxRenamedSourceSheetResolver.TryResolveCurrentSheet(
                    context, sheetName, sourceWorksheetPath, out var currentSheetName, out var targetWorksheetPath))
            {
                continue;
            }

            var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceArchive, sourceWorksheetPath);
            var sourceRoot = sourceWorksheetXml?.Root;
            if (sourceRoot is null)
                continue;

            var sourceControls = FindControlsContainer(sourceRoot, context.WorkbookNs);
            if (sourceControls is null)
                continue;

            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (targetWorksheetEntry is null)
                continue;

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            // Write the live FormControlModel state (IsChecked/Value/SelectedIndex, mutated by user
            // interaction since load) back into the source archive's ctrlProp parts BEFORE cloning
            // the controls block or copying ctrlProps forward, so the round-tripped file reflects the
            // control's current state rather than silently reverting to its file-load state.
            var sheet = workbook?.GetSheet(currentSheetName);
            if (sheet is not null)
            {
                WriteControlStateToCtrlProps(sourceArchive, targetArchive, context, sourceWorksheetPath, sheet);
                // R40-io-vml-shape-geometry-3-1: a row/column shift moves FormControlModel.Anchor in
                // memory and ApplyControlAnchorsToClone rewrites the modern controlPr/anchor to match,
                // but the VML shape (copied byte-for-byte by XlsxPackageMetadataMerger.
                // CopyUnknownPackageParts, well before this preserver runs) is never touched, leaving
                // legacy Form Controls — which Excel still renders via the VML layer, not DrawingML —
                // visually stuck at their pre-shift position. Sync the VML shape's cell-relative
                // ClientData Anchor to the live anchor/offsets regardless of whether the modern
                // controls block below is being injected or already survived byte-identical.
                SyncFormControlVmlAnchors(sourceArchive, targetArchive, context, sourceWorksheetPath, sheet);

                // R115-io-deleted-form-control-1: a control whose anchor was fully deleted is dropped
                // from sheet.FormControls by ShiftFormControls, but its ctrlProp part, worksheet
                // relationship, and VML shape were all already byte-copied forward by
                // XlsxPackageMetadataMerger.CopyUnknownPackageParts regardless of that in-memory
                // removal. Clean those package-level leftovers up here (independent of whether the
                // modern <controls> XML block below gets touched) so the control cannot resurrect
                // from its still-referenced VML shape even when the "already survived" shortcut just
                // below skips the modern block entirely.
                RemoveOrphanedControlPackageArtifacts(sourceArchive, targetArchive, context, sourceWorksheetPath, targetWorksheetPath, sheet);
            }

            // If a controls block already survived (clean byte-copy path), leave it alone.
            if (FindControlsContainer(targetRoot, context.WorkbookNs) is not null)
                continue;

            InjectFormControlLegacyDrawing(
                sourceArchive,
                targetArchive,
                context,
                sourceRoot,
                sourceWorksheetPath,
                targetRoot,
                targetWorksheetPath);

            targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", context.RelNs.NamespaceName);
            targetRoot.SetAttributeValue(XNamespace.Xmlns + "mc", McNs.NamespaceName);
            InsertControlsInWorksheetOrder(targetRoot, context.WorkbookNs, CloneControlsBlock(sourceRoot, context.WorkbookNs, sheet));
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
            anyChange = true;
        }

        if (anyChange)
        {
            // Re-bind the freshly injected <control> r:id values to the copied ctrlProps parts.
            XlsxWorksheetOleControlNormalizer.NormalizePackage(targetArchive);
        }
    }

    /// <summary>
    /// R112-io-formcontrol-vml-anchor-comment-reorder-1: re-applies the Form Control VML anchor
    /// sync AFTER <see cref="XlsxLegacyCommentPreserver.Preserve"/> has run. On a worksheet that has
    /// BOTH a legacy Form Control and at least one cell Note, both shapes live in the same single
    /// shared <c>legacyDrawing</c> VML part. <see cref="Preserve"/> (called earlier, before the
    /// comment preserver) patches that part's control shape's ClientData Anchor in place via
    /// <see cref="SyncFormControlVmlAnchors"/> to reflect a row/column shift, but
    /// XlsxLegacyCommentPreserver.Preserve unconditionally rebuilds the WHOLE VML document from the
    /// pristine SOURCE archive's copy of the part whenever the sheet has any Notes -- discarding the
    /// anchor patch this preserver already wrote into the target moments earlier (its rebuild keeps
    /// every non-Note shape, i.e. the control's shape, verbatim from that pristine snapshot). Calling
    /// <see cref="SyncFormControlVmlAnchors"/> again here, last, re-applies the anchor patch on top of
    /// whatever the comment preserver just wrote, so the sync always wins regardless of preserver
    /// order. Deliberately re-runs ONLY the anchor sync -- not <see cref="WriteControlStateToCtrlProps"/>
    /// or the controls-block injection, both of which already ran (and are idempotent-unsafe to
    /// duplicate: injection guards on "already present" but ctrlProp writes are pointless busywork)
    /// during the earlier <see cref="Preserve"/> call.
    /// </summary>
    public static void ReapplyVmlAnchorsAfterCommentReconciliation(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context,
        Workbook? workbook)
    {
        if (context is null || workbook is null)
            return;

        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            if (!XlsxRenamedSourceSheetResolver.TryResolveCurrentSheet(
                    context, sheetName, sourceWorksheetPath, out var currentSheetName, out _))
            {
                continue;
            }

            var sheet = workbook.GetSheet(currentSheetName);
            if (sheet is null)
                continue;

            SyncFormControlVmlAnchors(sourceArchive, targetArchive, context, sourceWorksheetPath, sheet);

            // R115-io-deleted-form-control-1: XlsxLegacyCommentPreserver.Preserve (which ran just
            // before this) rebuilds the WHOLE VML document from the pristine SOURCE archive's copy
            // whenever the sheet has any Notes, undoing any VML shape removal
            // RemoveOrphanedControlPackageArtifacts already performed for a deleted form control
            // during the earlier Preserve() call -- the exact same resurrection risk R112 fixed for
            // anchor sync, but for outright removal instead of a stale position. Re-run it here so a
            // deleted control's VML shape stays gone regardless of comment-preserver ordering. The
            // ctrlProp part/relationship were already removed during Preserve() and stay removed
            // (nothing after it re-copies them), so re-running the full cleanup is a safe no-op there.
            var targetWorksheetPath = context.TargetSheets.TryGetValue(sheetName, out var resolvedTargetPath)
                ? resolvedTargetPath
                : sourceWorksheetPath;
            RemoveOrphanedControlPackageArtifacts(sourceArchive, targetArchive, context, sourceWorksheetPath, targetWorksheetPath, sheet);
        }
    }

    /// <summary>
    /// Rewrites each control's <c>checked</c>/<c>val</c>/<c>sel</c>/<c>fmlaLink</c>/<c>fmlaRange</c>
    /// <c>formControlPr</c> attributes (in the TARGET archive's already-copied ctrlProp part — see
    /// <see cref="XlsxPackageMetadataMerger.CopyUnknownPackageParts"/>) from the corresponding
    /// <see cref="FormControlModel"/>'s live state, matched primarily by <c>shapeId</c> (unique per
    /// control) against <paramref name="sheet"/>.<see cref="Sheet.FormControls"/>, falling back to
    /// document order when a shapeId is unavailable on either side. Only the attributes we actually
    /// model are touched; every other attribute is left untouched so unmodeled state still
    /// round-trips byte-for-byte.
    /// </summary>
    private static void WriteControlStateToCtrlProps(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext context,
        string sourceWorksheetPath,
        Sheet sheet)
    {
        if (sheet.FormControls.Count == 0)
            return;

        var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceArchive, sourceWorksheetPath);
        var sourceRoot = sourceWorksheetXml?.Root;
        if (sourceRoot is null)
            return;

        var controlElements = EnumerateControlElements(sourceRoot, context.WorkbookNs + "control").ToList();
        if (controlElements.Count == 0)
            return;

        var worksheetRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive,
            XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath),
            sourceWorksheetPath,
            context.PackageRelNs);

        // Controls are read into FormControlModel in the same document order they're enumerated
        // here (XlsxFormControlMapper.ReadWorksheet uses the identical traversal), so pairing by
        // index recovers each model's originating <control> element PROVIDED every control parsed
        // successfully on load. XlsxFormControlMapper.ReadWorksheet swallows a single malformed
        // control's parse failure rather than aborting the whole sheet, which can shift a purely
        // positional pairing out of sync — so prefer matching by ShapeId (unique per control,
        // present on both the XML element and the loaded model) and only fall back to positional
        // pairing when ShapeId is unavailable on either side.
        var controlsByShapeId = sheet.FormControls
            .Where(c => c.ShapeId is not null)
            .ToDictionary(c => c.ShapeId!.Value, c => c);

        for (var i = 0; i < controlElements.Count; i++)
        {
            var element = controlElements[i];
            var hasShapeId = uint.TryParse(element.Attribute("shapeId")?.Value, out var shapeId);
            FormControlModel? control;
            if (hasShapeId)
            {
                // R115-io-deleted-form-control-1: a shapeId present on the element but absent from
                // the live model means this control was deleted (RowColumnShiftHelpers.AddressState.
                // ShiftFormControls dropped it from sheet.FormControls). Do NOT fall back to
                // positional indexing here -- that would silently rewrite a SURVIVING control's live
                // state onto this orphaned control's ctrlProp part, right before
                // RemoveOrphanedControlPackageArtifacts deletes that same part; skip it outright.
                if (!controlsByShapeId.TryGetValue(shapeId, out control))
                    continue;
            }
            else
            {
                control = i < sheet.FormControls.Count ? sheet.FormControls[i] : null;
            }

            if (control is null)
                continue;

            var relId = element.Attribute(context.RelNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(relId) ||
                !worksheetRels.TryGetValue(relId, out var ctrlPropPath) ||
                string.IsNullOrWhiteSpace(ctrlPropPath))
            {
                continue;
            }

            var targetEntry = targetArchive.GetEntry(ctrlPropPath);
            if (targetEntry is null)
                continue;

            var ctrlPropXml = XlsxPackageXmlEditor.LoadXml(targetEntry);
            var formControlPr = ctrlPropXml.Root;
            if (formControlPr is null)
                continue;

            ApplyControlStateToFormControlPr(formControlPr, control);
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, ctrlPropPath, ctrlPropXml);
        }
    }

    /// <summary>
    /// Rewrites each form control's VML shape <c>&lt;x:ClientData&gt;&lt;x:Anchor&gt;</c> (the
    /// cell-relative, pixel-offset geometry legacy VML-rendered Form Controls are actually
    /// repositioned from — see <see cref="XlsxFormControlMapper.ParseVmlAnchor"/>, the mirror-image
    /// reader) from the live, possibly row/column-shifted <see cref="FormControlModel.Anchor"/>/
    /// <see cref="FormControlModel.AnchorOffsets"/>, matched by <c>shapeId</c> the same way
    /// <see cref="ApplyControlAnchorsToClone"/> matches the modern <c>controlPr/anchor</c>. The VML
    /// part itself was already byte-copied into <paramref name="targetArchive"/> verbatim by
    /// <see cref="XlsxPackageMetadataMerger.CopyUnknownPackageParts"/> before this preserver runs, so
    /// without this rewrite a shifted control's stale VML anchor leaves it rendered at its pre-shift
    /// position in Excel even though the modern anchor was correctly updated.
    /// </summary>
    private static void SyncFormControlVmlAnchors(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext context,
        string sourceWorksheetPath,
        Sheet sheet)
    {
        var controlsByShapeId = sheet.FormControls
            .Where(c => c.ShapeId is not null && c.Anchor is not null)
            .ToDictionary(c => c.ShapeId!.Value, c => c);
        if (controlsByShapeId.Count == 0)
            return;

        var vmlPath = ResolveSourceLegacyDrawingVmlPath(sourceArchive, context, sourceWorksheetPath);
        if (vmlPath is null)
            return;

        var targetVmlEntry = targetArchive.GetEntry(vmlPath);
        if (targetVmlEntry is null)
            return;

        XDocument vmlXml;
        try
        {
            vmlXml = XlsxPackageXmlEditor.LoadXml(targetVmlEntry);
        }
        catch
        {
            return;
        }

        var root = vmlXml.Root;
        if (root is null)
            return;

        var changed = false;
        foreach (var shape in root.Descendants(VmlNs + "shape"))
        {
            var id = shape.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id))
                continue;

            FormControlModel? control = null;
            foreach (var (shapeId, candidate) in controlsByShapeId)
            {
                if (id.EndsWith("s" + shapeId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                {
                    control = candidate;
                    break;
                }
            }

            if (control is null)
                continue;

            ApplyAnchorToVmlShape(shape, control.Anchor!.Value, control.AnchorOffsets);
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, vmlPath, vmlXml);
    }

    /// <summary>
    /// Resolves the worksheet's legacy VML drawing part path from its source <c>legacyDrawing</c>
    /// marker's <c>r:id</c>, mirroring <see cref="InjectFormControlLegacyDrawing"/>'s resolution but
    /// usable regardless of whether the target already carries a <c>legacyDrawing</c> marker.
    /// </summary>
    private static string? ResolveSourceLegacyDrawingVmlPath(
        ZipArchive sourceArchive,
        XlsxSourcePackagePreservationContext context,
        string sourceWorksheetPath)
    {
        var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceArchive, sourceWorksheetPath);
        var sourceRoot = sourceWorksheetXml?.Root;
        var sourceMarker = sourceRoot?.Element(context.WorkbookNs + "legacyDrawing");
        var sourceRelId = sourceMarker?.Attribute(context.RelNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(sourceRelId))
            return null;

        var sourceRelsPath = XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath);
        var sourceRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive,
            sourceRelsPath,
            sourceWorksheetPath,
            context.PackageRelNs);

        return sourceRels.TryGetValue(sourceRelId, out var vmlPath) && !string.IsNullOrWhiteSpace(vmlPath)
            ? vmlPath
            : null;
    }

    /// <summary>
    /// Writes a live <see cref="FormControlModel.Anchor"/>/<see cref="FormControlModel.AnchorOffsets"/>
    /// pair into a VML shape's <c>&lt;x:ClientData&gt;&lt;x:Anchor&gt;</c> text, using the same
    /// comma-separated <c>leftCol,leftColOff,topRow,topRowOff,rightCol,rightColOff,bottomRow,bottomRowOff</c>
    /// shape (0-based cells, offsets in PIXELS) that <see cref="XlsxFormControlMapper.ParseVmlAnchor"/>
    /// reads. Creates the <c>&lt;x:Anchor&gt;</c> element (inside the first <c>x:ClientData</c> child)
    /// when absent, since a control's VML shape always carries a ClientData element.
    /// </summary>
    private static void ApplyAnchorToVmlShape(XElement shape, GridRange anchor, DrawingAnchorRange? offsets)
    {
        var clientData = shape.Element(ExcelVmlNs + "ClientData");
        if (clientData is null)
            return;

        var leftCol = anchor.Start.Col - 1;
        var topRow = anchor.Start.Row - 1;
        var rightCol = anchor.End.Col - 1;
        var bottomRow = anchor.End.Row - 1;
        var leftColOff = EmuToPixels(offsets?.From.ColumnOffsetEmu ?? 0);
        var topRowOff = EmuToPixels(offsets?.From.RowOffsetEmu ?? 0);
        var rightColOff = EmuToPixels(offsets?.To.ColumnOffsetEmu ?? 0);
        var bottomRowOff = EmuToPixels(offsets?.To.RowOffsetEmu ?? 0);

        var anchorText = string.Join(
            ",",
            leftCol.ToString(CultureInfo.InvariantCulture),
            leftColOff.ToString(CultureInfo.InvariantCulture),
            topRow.ToString(CultureInfo.InvariantCulture),
            topRowOff.ToString(CultureInfo.InvariantCulture),
            rightCol.ToString(CultureInfo.InvariantCulture),
            rightColOff.ToString(CultureInfo.InvariantCulture),
            bottomRow.ToString(CultureInfo.InvariantCulture),
            bottomRowOff.ToString(CultureInfo.InvariantCulture));

        var anchorElement = clientData.Element(ExcelVmlNs + "Anchor");
        if (anchorElement is null)
        {
            anchorElement = new XElement(ExcelVmlNs + "Anchor");
            clientData.AddFirst(anchorElement);
        }

        anchorElement.Value = anchorText;
    }

    private static long EmuToPixels(long emu) =>
        Math.Max(0, (long)Math.Round(emu / (double)EmusPerPixel, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Writes IsChecked/Value/SelectedIndex/LinkedCell/ListFillRange onto a <c>formControlPr</c>
    /// element's attributes. R26-form-controls-deep-1: LinkedCell/ListFillRange are shifted in
    /// memory by <c>RowColumnShiftHelpers.AddressState.ShiftFormControls</c> on a structural edit
    /// (row/column insert-delete), so they must be written back here the same way IsChecked/Value/
    /// SelectedIndex already are, or a reload silently re-links the control to its stale, pre-edit
    /// cell reference.
    /// </summary>
    private static void ApplyControlStateToFormControlPr(XElement formControlPr, FormControlModel control)
    {
        if (!string.IsNullOrWhiteSpace(control.LinkedCell))
            formControlPr.SetAttributeValue("fmlaLink", control.LinkedCell);

        if (!string.IsNullOrWhiteSpace(control.ListFillRange))
            formControlPr.SetAttributeValue("fmlaRange", control.ListFillRange);

        switch (control.Kind)
        {
            case FormControlKind.CheckBox:
            case FormControlKind.OptionButton:
                // R39-meta-2: for CheckBox/OptionButton, FormControlModel.Value carries Excel's
                // tri-state ST_Checked encoding (0=Unchecked/1=Checked/2=Mixed) — see
                // XlsxFormControlMapper.ReadControlProperties. IsChecked alone cannot represent
                // "Mixed" (it stays false), so writing IsChecked ? "Checked" : "Unchecked"
                // unconditionally silently downgrades a Mixed control to Unchecked on a
                // full-rebuild save. Prefer the tri-state Value when it carries the "Mixed"
                // reading; otherwise fall back to the two-state IsChecked (kept for controls
                // whose Value was never populated, e.g. constructed purely in-memory).
                formControlPr.SetAttributeValue(
                    "checked",
                    control.Value == 2 ? "Mixed" : control.IsChecked ? "Checked" : "Unchecked");
                break;

            case FormControlKind.Spinner:
            case FormControlKind.ScrollBar:
                if (control.Value is { } value)
                    formControlPr.SetAttributeValue("val", value);
                break;

            case FormControlKind.ListBox:
            case FormControlKind.DropDown:
                if (control.SelectedIndex is { } selectedIndex)
                    formControlPr.SetAttributeValue("sel", selectedIndex);
                break;
        }
    }

    /// <summary>
    /// Enumerates a worksheet's <c>&lt;control&gt;</c> elements in document order, descending
    /// through <c>mc:AlternateContent</c>/<c>mc:Choice</c>/<c>mc:Fallback</c> wrappers exactly like
    /// <see cref="XlsxFormControlMapper"/>'s loader does (same namespace-qualified name match and
    /// first-Choice-else-Fallback traversal), so the two traversals stay paired.
    /// </summary>
    private static IEnumerable<XElement> EnumerateControlElements(XElement element, XName controlName)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name == controlName)
            {
                yield return child;
                continue;
            }

            if (child.Name == McNs + "AlternateContent")
            {
                var preferred = child.Element(McNs + "Choice") ?? child.Element(McNs + "Fallback");
                if (preferred is not null)
                {
                    foreach (var match in EnumerateControlElements(preferred, controlName))
                        yield return match;
                }

                continue;
            }

            foreach (var match in EnumerateControlElements(child, controlName))
                yield return match;
        }
    }

    /// <summary>
    /// Copy the source form-control <c>legacyDrawing</c> marker (the VML shape geometry) into the
    /// target worksheet + relationships. Returns true when a marker was injected.
    /// </summary>
    private static bool InjectFormControlLegacyDrawing(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext context,
        XElement sourceRoot,
        string sourceWorksheetPath,
        XElement targetRoot,
        string targetWorksheetPath)
    {
        // A control's VML lives behind the worksheet legacyDrawing marker. Only inject it if the
        // target does not already have one (comments also use legacyDrawing — that path is handled
        // by XlsxWorksheetVmlReferencePreserver).
        if (targetRoot.Element(context.WorkbookNs + "legacyDrawing") is not null)
            return false;

        var sourceMarker = sourceRoot.Element(context.WorkbookNs + "legacyDrawing");
        var sourceRelId = sourceMarker?.Attribute(context.RelNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(sourceRelId))
            return false;

        var sourceRelsPath = XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath);
        var sourceRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive,
            sourceRelsPath,
            sourceWorksheetPath,
            context.PackageRelNs);
        if (!sourceRels.TryGetValue(sourceRelId, out var vmlPath) ||
            targetArchive.GetEntry(vmlPath) is null)
        {
            return false;
        }

        var targetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
        var targetRelsXml = targetArchive.GetEntry(targetRelsPath) is { } targetRelsEntry
            ? XlsxPackageXmlEditor.LoadXml(targetRelsEntry)
            : new XDocument(new XElement(context.PackageRelNs + "Relationships"));
        var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            targetRelsXml,
            context.PackageRelNs,
            targetWorksheetPath,
            vmlPath,
            VmlDrawingRelationshipType);
        XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetRelsPath, targetRelsXml);

        var marker = new XElement(context.WorkbookNs + "legacyDrawing",
            new XAttribute(context.RelNs + "id", targetRelId));
        InsertLegacyDrawingInWorksheetOrder(targetRoot, context.WorkbookNs, marker);
        return true;
    }

    private static XElement? FindControlsContainer(XElement worksheetRoot, XNamespace worksheetNs)
    {
        // Excel stores controls either directly as <controls> or wrapped in an mc:AlternateContent.
        var direct = worksheetRoot.Element(worksheetNs + "controls");
        if (direct is not null)
            return direct;

        foreach (var alternateContent in worksheetRoot.Elements(McNs + "AlternateContent"))
        {
            var preferred = alternateContent.Element(McNs + "Choice") ?? alternateContent.Element(McNs + "Fallback");
            if (preferred?.Element(worksheetNs + "controls") is not null)
                return alternateContent;
        }

        return null;
    }

    /// <summary>
    /// Clone the source controls container (direct or AlternateContent-wrapped), then rewrite each
    /// control's anchor from the live, possibly-shifted <see cref="FormControlModel"/> state (see
    /// <see cref="ApplyControlAnchorsToClone"/>). R26-form-controls-deep-2: a structural edit
    /// (row/column insert-delete) shifts <see cref="FormControlModel.Anchor"/> in memory via
    /// <c>RowColumnShiftHelpers.AddressState.ShiftFormControls</c>, but a plain verbatim clone of the
    /// pristine source controls block would silently revert to the pre-edit anchor on save.
    /// </summary>
    private static XElement CloneControlsBlock(XElement sourceRoot, XNamespace worksheetNs, Sheet? sheet)
    {
        var container = FindControlsContainer(sourceRoot, worksheetNs)!;
        var clone = new XElement(container);
        if (sheet is not null)
            ApplyControlAnchorsToClone(clone, worksheetNs, sheet);

        return clone;
    }

    /// <summary>
    /// Rewrites each control's <c>controlPr/anchor</c> <c>from</c>/<c>to</c> markers (in the just-
    /// cloned controls block) from the corresponding <see cref="FormControlModel"/>'s live
    /// <see cref="FormControlModel.Anchor"/>/<see cref="FormControlModel.AnchorOffsets"/> — matched
    /// primarily by <c>shapeId</c>, falling back to document order only when a shapeId is
    /// unavailable on the ELEMENT side (mirroring <see cref="WriteControlStateToCtrlProps"/>'s
    /// matching). R115-io-deleted-form-control-1: when an element's shapeId IS present but has no
    /// corresponding live model, the control was deleted by a row/column edit (see
    /// <c>RowColumnShiftHelpers.AddressState.ShiftFormControls</c>) — such an element is removed from
    /// the clone outright rather than falling back to positional indexing, which would otherwise
    /// wrongly bind a SURVIVING control's live anchor onto this orphaned element (producing a
    /// duplicate, overlapping shape at the survivor's position instead of the deleted control simply
    /// disappearing).
    /// </summary>
    private static void ApplyControlAnchorsToClone(XElement clonedControlsBlock, XNamespace worksheetNs, Sheet sheet)
    {
        var controlElements = EnumerateControlElements(clonedControlsBlock, worksheetNs + "control").ToList();
        if (controlElements.Count == 0)
            return;

        var controlsByShapeId = sheet.FormControls
            .Where(c => c.ShapeId is not null)
            .ToDictionary(c => c.ShapeId!.Value, c => c);

        for (var i = 0; i < controlElements.Count; i++)
        {
            var element = controlElements[i];
            var hasShapeId = uint.TryParse(element.Attribute("shapeId")?.Value, out var shapeId);
            FormControlModel? control;
            if (hasShapeId)
            {
                if (!controlsByShapeId.TryGetValue(shapeId, out control))
                {
                    RemoveControlElement(element);
                    continue;
                }
            }
            else
            {
                control = i < sheet.FormControls.Count ? sheet.FormControls[i] : null;
            }

            if (control?.Anchor is not { } anchor)
                continue;

            var anchorElement = element.Element(worksheetNs + "controlPr")?.Element(worksheetNs + "anchor");
            if (anchorElement is null)
                continue;

            ApplyAnchorToElement(anchorElement, anchor, control.AnchorOffsets);
        }
    }

    /// <summary>
    /// Removes an orphaned <c>&lt;control&gt;</c> element from a cloned controls block, then cleans
    /// up any now-empty <c>mc:Choice</c>/<c>mc:Fallback</c>/<c>mc:AlternateContent</c> ancestor chain
    /// (Excel commonly wraps each individual control in its own AlternateContent for forward
    /// compatibility — see the fixtures in <c>XlsxFormControlShiftPersistenceTests</c>), so the saved
    /// worksheet does not carry a hollow wrapper with no content. Stops at the enclosing
    /// <c>&lt;controls&gt;</c> element itself, which is left in place (possibly empty — a
    /// <c>controls</c> element with zero children is valid) since other controls may still live
    /// alongside it.
    /// </summary>
    private static void RemoveControlElement(XElement element)
    {
        var parent = element.Parent;
        element.Remove();

        while (parent is not null &&
               parent.Name.LocalName is "Choice" or "Fallback" or "AlternateContent" &&
               !parent.Elements().Any())
        {
            var grandParent = parent.Parent;
            parent.Remove();
            parent = grandParent;
        }
    }

    /// <summary>
    /// Finds every <c>&lt;control&gt;</c> in the SOURCE worksheet's controls container that no longer
    /// has a corresponding live <see cref="FormControlModel"/> in <paramref name="sheet"/>'s
    /// <see cref="Sheet.FormControls"/> — i.e. a control deleted by a row/column edit (see
    /// <see cref="OrphanedControl"/>). Elements without a parseable <c>shapeId</c> cannot be
    /// correlated to the model at all and are conservatively left out (never reported as orphaned),
    /// matching the same fallback semantics used elsewhere in this file.
    /// </summary>
    private static IReadOnlyList<OrphanedControl> FindOrphanedControls(
        XElement sourceRoot,
        XNamespace worksheetNs,
        XNamespace relNs,
        Sheet sheet)
    {
        var container = FindControlsContainer(sourceRoot, worksheetNs);
        if (container is null)
            return [];

        var liveShapeIds = sheet.FormControls
            .Where(c => c.ShapeId is not null)
            .Select(c => c.ShapeId!.Value)
            .ToHashSet();

        List<OrphanedControl>? orphaned = null;
        foreach (var element in EnumerateControlElements(container, worksheetNs + "control"))
        {
            if (!uint.TryParse(element.Attribute("shapeId")?.Value, out var shapeId) ||
                liveShapeIds.Contains(shapeId))
            {
                continue;
            }

            orphaned ??= [];
            orphaned.Add(new OrphanedControl(shapeId, element.Attribute(relNs + "id")?.Value));
        }

        return orphaned ?? (IReadOnlyList<OrphanedControl>)[];
    }

    /// <summary>
    /// R115-io-deleted-form-control-1: removes the package-level leftovers of every control that
    /// <see cref="FindOrphanedControls"/> finds deleted from the live model — its ctrlProp part, the
    /// worksheet relationship pointing at that part, the part's <c>[Content_Types].xml</c> override,
    /// and its VML shape (Excel renders legacy Form Controls from the VML layer, not the modern
    /// <c>&lt;control&gt;</c>/<c>controlPr</c> block, so leaving the VML shape behind would keep the
    /// "deleted" control fully visible even after the modern XML element is gone). Safe to call more
    /// than once for the same sheet (e.g. once from <see cref="Preserve"/> and again from
    /// <see cref="ReapplyVmlAnchorsAfterCommentReconciliation"/> to defend against
    /// <see cref="XlsxLegacyCommentPreserver.Preserve"/> rebuilding the whole VML part from the
    /// pristine source afterward) — once a part/relationship is gone, re-finding it is a harmless
    /// no-op.
    /// </summary>
    private static void RemoveOrphanedControlPackageArtifacts(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext context,
        string sourceWorksheetPath,
        string targetWorksheetPath,
        Sheet sheet)
    {
        var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceArchive, sourceWorksheetPath);
        var sourceRoot = sourceWorksheetXml?.Root;
        if (sourceRoot is null)
            return;

        var orphanedControls = FindOrphanedControls(sourceRoot, context.WorkbookNs, context.RelNs, sheet);
        if (orphanedControls.Count == 0)
            return;

        RemoveOrphanedCtrlProps(targetArchive, context, targetWorksheetPath, orphanedControls);
        RemoveOrphanedVmlShapes(sourceArchive, targetArchive, context, sourceWorksheetPath, orphanedControls);
    }

    private static void RemoveOrphanedCtrlProps(
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext context,
        string targetWorksheetPath,
        IReadOnlyList<OrphanedControl> orphanedControls)
    {
        var relIds = orphanedControls
            .Select(o => o.RelationshipId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        if (relIds.Count == 0)
            return;

        var targetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
        var targetRelsEntry = targetArchive.GetEntry(targetRelsPath);
        if (targetRelsEntry is null)
            return;

        XDocument targetRelsXml;
        try
        {
            targetRelsXml = XlsxPackageXmlEditor.LoadXml(targetRelsEntry);
        }
        catch
        {
            return;
        }

        var relsRoot = targetRelsXml.Root;
        if (relsRoot is null)
            return;

        var removedPartPaths = new List<string>();
        var relsChanged = false;
        foreach (var relationship in relsRoot.Elements(context.PackageRelNs + "Relationship").ToList())
        {
            var id = relationship.Attribute("Id")?.Value;
            if (id is null || !relIds.Contains(id))
                continue;

            var target = relationship.Attribute("Target")?.Value;
            if (!string.IsNullOrWhiteSpace(target))
            {
                var resolved = XlsxPackagePath.ResolveRelationshipTarget(targetWorksheetPath, target);
                if (!string.IsNullOrWhiteSpace(resolved))
                    removedPartPaths.Add(resolved);
            }

            relationship.Remove();
            relsChanged = true;
        }

        if (relsChanged)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetRelsPath, targetRelsXml);

        if (removedPartPaths.Count == 0)
            return;

        foreach (var partPath in removedPartPaths)
            targetArchive.GetEntry(partPath)?.Delete();

        RemoveContentTypeOverridesForParts(targetArchive, removedPartPaths);
    }

    private static void RemoveContentTypeOverridesForParts(ZipArchive targetArchive, IReadOnlyList<string> partPaths)
    {
        var contentTypesEntry = targetArchive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return;

        XDocument contentTypesXml;
        try
        {
            contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        }
        catch
        {
            return;
        }

        var root = contentTypesXml.Root;
        if (root is null)
            return;

        var targets = partPaths
            .Select(path => "/" + path.TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var element in root.Elements(ContentTypesNs + "Override").ToList())
        {
            var partName = element.Attribute("PartName")?.Value;
            if (partName is null || !targets.Contains(partName))
                continue;

            element.Remove();
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, "[Content_Types].xml", contentTypesXml);
    }

    /// <summary>
    /// Removes the VML shape(s) belonging to each orphaned control from the worksheet's legacy VML
    /// drawing part, matched by <c>shapeId</c> suffix the same way <see cref="SyncFormControlVmlAnchors"/>
    /// matches a LIVE control's shape (<c>id.EndsWith("s" + shapeId)</c>).
    /// </summary>
    private static void RemoveOrphanedVmlShapes(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext context,
        string sourceWorksheetPath,
        IReadOnlyList<OrphanedControl> orphanedControls)
    {
        var vmlPath = ResolveSourceLegacyDrawingVmlPath(sourceArchive, context, sourceWorksheetPath);
        if (vmlPath is null)
            return;

        var targetVmlEntry = targetArchive.GetEntry(vmlPath);
        if (targetVmlEntry is null)
            return;

        XDocument vmlXml;
        try
        {
            vmlXml = XlsxPackageXmlEditor.LoadXml(targetVmlEntry);
        }
        catch
        {
            return;
        }

        var root = vmlXml.Root;
        if (root is null)
            return;

        var suffixes = orphanedControls
            .Select(o => "s" + o.ShapeId.ToString(CultureInfo.InvariantCulture))
            .ToList();

        var changed = false;
        foreach (var shape in root.Descendants(VmlNs + "shape").ToList())
        {
            var id = shape.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id))
                continue;

            if (suffixes.Any(suffix => id.EndsWith(suffix, StringComparison.Ordinal)))
            {
                shape.Remove();
                changed = true;
            }
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, vmlPath, vmlXml);
    }

    /// <summary>
    /// Writes a live <see cref="FormControlModel.Anchor"/>/<see cref="FormControlModel.AnchorOffsets"/>
    /// pair into a worksheet <c>controlPr/anchor</c>'s <c>from</c>/<c>to</c> markers, using the same
    /// 0-based col/row + EMU colOff/rowOff shape that <see cref="XlsxFormControlMapper"/>'s
    /// <c>ReadAnchor</c>/<c>ReadAnchorOffsets</c> read. Falls back to zero sub-cell offsets when
    /// <paramref name="offsets"/> is unavailable.
    /// </summary>
    private static void ApplyAnchorToElement(XElement anchorElement, GridRange anchor, DrawingAnchorRange? offsets)
    {
        var ns = anchorElement.Name.Namespace;
        var from = anchorElement.Element(ns + "from");
        var to = anchorElement.Element(ns + "to");
        if (from is null || to is null)
            return;

        SetAnchorMarker(from, anchor.Start.Col - 1, anchor.Start.Row - 1, offsets?.From.ColumnOffsetEmu ?? 0, offsets?.From.RowOffsetEmu ?? 0);
        SetAnchorMarker(to, anchor.End.Col - 1, anchor.End.Row - 1, offsets?.To.ColumnOffsetEmu ?? 0, offsets?.To.RowOffsetEmu ?? 0);
    }

    private static void SetAnchorMarker(XElement marker, uint col, uint row, long colOffEmu, long rowOffEmu)
    {
        SetMarkerElementValue(marker, DrawingNs + "col", col);
        SetMarkerElementValue(marker, DrawingNs + "colOff", colOffEmu);
        SetMarkerElementValue(marker, DrawingNs + "row", row);
        SetMarkerElementValue(marker, DrawingNs + "rowOff", rowOffEmu);
    }

    private static void SetMarkerElementValue(XElement marker, XName name, long value)
    {
        var element = marker.Element(name);
        if (element is null)
            return;

        element.Value = value.ToString(CultureInfo.InvariantCulture);
    }

    private static void InsertControlsInWorksheetOrder(XElement worksheetRoot, XNamespace worksheetNs, XElement controlsBlock)
    {
        string[] laterElements = ["webPublishItems", "tableParts", "extLst"];
        var insertionPoint = FindFirstWorksheetElement(worksheetRoot, worksheetNs, laterElements);
        if (insertionPoint is null)
            worksheetRoot.Add(controlsBlock);
        else
            insertionPoint.AddBeforeSelf(controlsBlock);
    }

    private static void InsertLegacyDrawingInWorksheetOrder(XElement worksheetRoot, XNamespace worksheetNs, XElement marker)
    {
        string[] laterElements = ["legacyDrawingHF", "picture", "oleObjects", "controls", "webPublishItems", "tableParts", "extLst"];
        var insertionPoint = FindFirstWorksheetElement(worksheetRoot, worksheetNs, laterElements);
        if (insertionPoint is null)
            worksheetRoot.Add(marker);
        else
            insertionPoint.AddBeforeSelf(marker);
    }

    private static XElement? FindFirstWorksheetElement(XElement worksheetRoot, XNamespace worksheetNs, string[] laterElements)
    {
        foreach (var element in worksheetRoot.Elements())
        {
            if (element.Name.Namespace == worksheetNs &&
                laterElements.Contains(element.Name.LocalName, StringComparer.Ordinal))
            {
                return element;
            }

            // Controls wrapped in AlternateContent should also be treated as a "later" boundary.
            if (element.Name == McNs + "AlternateContent")
                return element;
        }

        return null;
    }
}
