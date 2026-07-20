using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Writes x14-extension data validation rules into the worksheet extLst.
///
/// For each <see cref="DataValidation"/> with <see cref="DataValidation.IsX14"/> = true this
/// writer emits:
/// <code>
/// &lt;extLst&gt;
///   &lt;ext uri="{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}"
///        xmlns:x14="…/2009/9/main" xmlns:xm="…/excel/2006/main"&gt;
///     &lt;x14:dataValidations&gt;
///       &lt;x14:dataValidation type="list" …&gt;
///         &lt;x14:formula1&gt;&lt;xm:f&gt;Sheet2!$A$1:$A$5&lt;/xm:f&gt;&lt;/x14:formula1&gt;
///         &lt;xm:sqref&gt;B2&lt;/xm:sqref&gt;
///       &lt;/x14:dataValidation&gt;
///     &lt;/x14:dataValidations&gt;
///   &lt;/ext&gt;
/// &lt;/extLst&gt;
/// </code>
///
/// The legacy <c>&lt;dataValidation&gt;</c> for the same cell is kept with an empty
/// <c>&lt;formula1&gt;</c> so older readers can still open the file without errors; the x14 block
/// carries the real (cross-sheet) formula.
///
/// Any pre-existing extLst ext children with other URIs are preserved unchanged.
/// </summary>
internal static class XlsxX14DataValidationWriter
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";

    public static bool HasX14DataValidations(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (HasX14DataValidations(sheet))
                return true;
        }

        return false;
    }

    public static bool HasX14DataValidations(Sheet sheet)
    {
        foreach (var dv in sheet.DataValidations)
        {
            if (dv.IsX14)
                return true;
        }

        return false;
    }

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        XlsxWorkbookWorksheetPathMap? worksheetPathMap;
        using (var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true))
            worksheetPathMap = XlsxWorkbookWorksheetPathMap.TryCreate(archive);

        if (worksheetPathMap is null)
            return;

        if (xlsxStream.CanSeek)
            xlsxStream.Position = 0;

        using var archive2 = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var sheet in workbook.Sheets)
        {
            if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var worksheetEntry = archive2.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            // Even when the sheet's model has zero x14 rules right now (e.g. the only rule was
            // just deleted), the preserved worksheet XML may still carry a stale x14 DV ext block
            // from the source file. Skipping the sheet in that case would let the deleted rule
            // resurrect on reopen, so we must still inspect (and, if needed, rewrite) the sheet.
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (!HasX14DataValidations(sheet) && !HasX14DataValidationExt(root))
                continue;

            WriteX14DataValidations(root, sheet);

            XlsxPackageXmlEditor.ReplaceXml(archive2, worksheetPath, worksheetXml);
        }
    }

    /// <summary>
    /// True when the worksheet root's (last) extLst already carries an x14 data-validation ext
    /// block, regardless of whether the current sheet model still has any x14 rules. Mirrors the
    /// "last extLst" convention used by <see cref="FindOrCreateExtLst"/> and the stale-ext removal
    /// below, so a sheet whose x14 ext lives in that extLst is never skipped.
    /// </summary>
    private static bool HasX14DataValidationExt(XElement worksheetRoot)
    {
        var extLst = worksheetRoot.Elements().LastOrDefault(e => e.Name.LocalName == "extLst");
        return extLst is not null && extLst.Elements()
            .Any(e => e.Name.LocalName == "ext" && e.Attribute("uri")?.Value == XlsxX14DataValidationReader.X14DvUri);
    }

    private static void WriteX14DataValidations(XElement worksheetRoot, Sheet sheet)
    {
        var x14Rules = new List<DataValidation>();
        foreach (var dv in sheet.DataValidations)
        {
            if (dv.IsX14)
                x14Rules.Add(dv);
        }

        // Every sqref FreeX is about to (re-)emit below, from its own IsX14-modeled rules, plus
        // every sqref any OTHER currently-live (non-deleted) DataValidation on this sheet covers --
        // regardless of its own IsX14 flag. An existing x14:dataValidation entry whose sqref
        // matches one of the latter but not the former was written (moments earlier, by
        // ClosedXML's own SaveAs) for a rule FreeX's model does not consider x14 -- e.g. a
        // Decimal/WholeNumber/Date/Time rule ClosedXML silently auto-promotes into the x14
        // extension because its bound formula is a cross-sheet reference, even though FreeX left
        // DataValidation.IsX14 false for it. That rule has no legacy <dataValidation> element
        // either (ClosedXML wrote it straight into the x14 block), so blindly replacing the whole
        // ext block below would delete it outright -- preserve any such foreign-but-still-live
        // entry instead of discarding it. An entry whose sqref matches NEITHER set corresponds to
        // a rule that has since been deleted from the model entirely (not just un-x14-marked) and
        // must still be dropped, matching R18-dv-extlst-x14-io-1's "delete resurrects nothing"
        // contract.
        var freeXSqrefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dv in x14Rules)
            freeXSqrefs.Add(NormalizeSqrefForComparison(BuildSqref(dv)));

        var liveSqrefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dv in sheet.DataValidations)
            liveSqrefs.Add(NormalizeSqrefForComparison(BuildSqref(dv)));

        var foreignX14DvElements = new List<XElement>();
        var extLstForForeignScan = worksheetRoot.Elements().LastOrDefault(e => e.Name.LocalName == "extLst");
        var existingX14DvExt = extLstForForeignScan?.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "ext" && e.Attribute("uri")?.Value == XlsxX14DataValidationReader.X14DvUri);
        var existingX14DvsElement = existingX14DvExt?.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "dataValidations");
        if (existingX14DvsElement is not null)
        {
            foreach (var candidate in existingX14DvsElement.Elements().Where(e => e.Name.LocalName == "dataValidation"))
            {
                var rawSqref = candidate.Elements().LastOrDefault(e => e.Name.LocalName == "sqref")?.Value;
                if (string.IsNullOrEmpty(rawSqref))
                    continue;

                var sqref = NormalizeSqrefForComparison(rawSqref);
                if (!freeXSqrefs.Contains(sqref) && liveSqrefs.Contains(sqref))
                    foreignX14DvElements.Add(new XElement(candidate));
            }
        }

        if (x14Rules.Count == 0 && foreignX14DvElements.Count == 0)
        {
            // All x14 rules on this sheet were deleted and nothing foreign needs to be kept. If
            // the preserved worksheet XML still has a stale x14 DV ext block from the source
            // file, strip it so the deleted rule(s) do not resurrect on reopen. Leave any other
            // ext children (and a non-empty extLst) untouched; only remove the extLst itself if
            // it becomes empty.
            var extLst = worksheetRoot.Elements()
                .LastOrDefault(e => e.Name.LocalName == "extLst");
            if (extLst is null)
                return;

            var staleExt = extLst.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "ext" && e.Attribute("uri")?.Value == XlsxX14DataValidationReader.X14DvUri);
            staleExt?.Remove();

            if (!extLst.HasElements)
                extLst.Remove();

            return;
        }

        // Build the x14 dataValidations element: FreeX's own modeled rules first, followed by any
        // foreign (non-FreeX-modeled) entries that must be preserved.
        var x14DvElements = new List<XElement>(x14Rules.Count + foreignX14DvElements.Count);
        foreach (var dv in x14Rules)
        {
            var x14Dv = BuildX14DataValidationElement(dv);
            x14DvElements.Add(x14Dv);
        }

        x14DvElements.AddRange(foreignX14DvElements);

        var x14DvsElement = new XElement(X14Ns + "dataValidations",
            new XAttribute("count", x14DvElements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            x14DvElements);

        // Find or create the worksheet extLst, then find/replace the x14 DV ext block.
        var existingExtLst = FindOrCreateExtLst(worksheetRoot);

        // Remove any existing x14 DV ext block (we'll rewrite it -- any entries worth keeping
        // from it were already copied into foreignX14DvElements above).
        var existing = existingExtLst.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "ext" && e.Attribute("uri")?.Value == XlsxX14DataValidationReader.X14DvUri);
        existing?.Remove();

        // Add the new x14 DV ext block (at the end of extLst, after other ext children).
        existingExtLst.Add(new XElement(
            WorksheetNs + "ext",
            new XAttribute(XNamespace.Xmlns + "x14", X14Ns.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xm", XmNs.NamespaceName),
            new XAttribute("uri", XlsxX14DataValidationReader.X14DvUri),
            x14DvsElement));
    }

    private static XElement BuildX14DataValidationElement(DataValidation dv)
    {
        var x14Dv = new XElement(X14Ns + "dataValidation");

        // Attributes
        if (dv.Type != DvType.Any)
            x14Dv.SetAttributeValue("type", ToTypeString(dv.Type));
        if (ShouldWriteOperator(dv.Type))
            x14Dv.SetAttributeValue("operator", ToOperatorString(dv.Operator));
        if (dv.AllowBlank)
            x14Dv.SetAttributeValue("allowBlank", "1");
        // In OOXML, showDropDown="1" means HIDE the dropdown. When ShowDropdown=false we write "1".
        if (!dv.ShowDropdown)
            x14Dv.SetAttributeValue("showDropDown", "1");
        if (dv.AlertStyle != DvAlertStyle.Stop)
            x14Dv.SetAttributeValue("errorStyle", ToAlertStyleString(dv.AlertStyle));
        if (!dv.ShowInputMessage)
            x14Dv.SetAttributeValue("showInputMessage", "0");
        if (!dv.ShowErrorMessage)
            x14Dv.SetAttributeValue("showErrorMessage", "0");
        if (!string.IsNullOrEmpty(dv.ErrorTitle))
            x14Dv.SetAttributeValue("errorTitle", dv.ErrorTitle);
        if (!string.IsNullOrEmpty(dv.ErrorMessage))
            x14Dv.SetAttributeValue("error", dv.ErrorMessage);
        if (!string.IsNullOrEmpty(dv.PromptTitle))
            x14Dv.SetAttributeValue("promptTitle", dv.PromptTitle);
        if (!string.IsNullOrEmpty(dv.PromptMessage))
            x14Dv.SetAttributeValue("prompt", dv.PromptMessage);

        // Re-emit any unmodeled x14-only attributes captured on load (e.g. imeMode) so they
        // round-trip. Never overwrite an attribute already set from the modeled fields above.
        if (dv.NativeAttributes is { Count: > 0 } nativeAttributes)
            XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(x14Dv, nativeAttributes);

        // <x14:formula1><xm:f>…</xm:f></x14:formula1>
        var formula1 = dv.Formula1;
        if (!string.IsNullOrEmpty(formula1))
        {
            x14Dv.Add(new XElement(X14Ns + "formula1",
                new XElement(XmNs + "f", formula1)));
        }

        // <x14:formula2><xm:f>…</xm:f></x14:formula2>
        if (!string.IsNullOrEmpty(dv.Formula2))
        {
            x14Dv.Add(new XElement(X14Ns + "formula2",
                new XElement(XmNs + "f", dv.Formula2)));
        }

        // <xm:sqref>…</xm:sqref> — MUST be last child per schema.
        x14Dv.Add(new XElement(XmNs + "sqref", BuildSqref(dv)));

        return x14Dv;
    }

    /// <summary>
    /// Finds the last extLst element at the worksheet root level, or creates a new one appended
    /// after &lt;tableParts&gt; / at the end of the root. Any pre-existing extLst children with
    /// other URIs are left intact.
    /// </summary>
    private static XElement FindOrCreateExtLst(XElement worksheetRoot)
    {
        // Prefer an existing extLst (the last one if there are multiples).
        var existing = worksheetRoot.Elements()
            .LastOrDefault(e => e.Name.LocalName == "extLst");
        if (existing is not null)
            return existing;

        // Create a new extLst positioned after any tableParts element (or at the end).
        var newExtLst = new XElement(WorksheetNs + "extLst");
        var tableParts = worksheetRoot.Elements()
            .LastOrDefault(e => e.Name.LocalName == "tableParts");
        if (tableParts is not null)
            tableParts.AddAfterSelf(newExtLst);
        else
            worksheetRoot.Add(newExtLst);

        return newExtLst;
    }

    private static string BuildSqref(DataValidation dv)
    {
        if (dv.AdditionalRanges.Count == 0)
            return RangeToSqrefPart(dv.AppliesTo);

        var sb = new StringBuilder(RangeToSqrefPart(dv.AppliesTo));
        foreach (var range in dv.AdditionalRanges)
            sb.Append(' ').Append(RangeToSqrefPart(range));

        return sb.ToString();
    }

    private static string RangeToSqrefPart(GridRange range) =>
        range.Start == range.End
            ? range.Start.ToA1()
            : range.ToString();

    /// <summary>
    /// Normalizes an sqref token list for equality comparison between FreeX's own
    /// <see cref="BuildSqref"/> output (single-cell ranges collapsed to e.g. "D4") and a sqref
    /// ClosedXML itself wrote into an x14:dataValidation (which always emits the full "D4:D4"
    /// form, even for a single cell) -- used by <see cref="WriteX14DataValidations"/> to match a
    /// foreign x14 entry back to a live <see cref="DataValidation"/> in the model regardless of
    /// which of the two equivalent single-cell notations either side happens to use.
    /// </summary>
    private static string NormalizeSqrefForComparison(string sqref)
    {
        var parts = sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var colonIndex = part.IndexOf(':');
            if (colonIndex > 0 &&
                string.Equals(part[..colonIndex], part[(colonIndex + 1)..], StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = part[..colonIndex];
            }
        }

        return string.Join(' ', parts);
    }

    private static bool ShouldWriteOperator(DvType type) =>
        type is DvType.WholeNumber or DvType.Decimal or DvType.Date or DvType.Time or DvType.TextLength;

    private static string ToTypeString(DvType type) => type switch
    {
        DvType.WholeNumber => "whole",
        DvType.Decimal => "decimal",
        DvType.List => "list",
        DvType.Date => "date",
        DvType.Time => "time",
        DvType.TextLength => "textLength",
        DvType.Custom => "custom",
        _ => "none",
    };

    private static string ToOperatorString(DvOperator op) => op switch
    {
        DvOperator.NotBetween => "notBetween",
        DvOperator.Equal => "equal",
        DvOperator.NotEqual => "notEqual",
        DvOperator.GreaterThan => "greaterThan",
        DvOperator.LessThan => "lessThan",
        DvOperator.GreaterThanOrEqual => "greaterThanOrEqual",
        DvOperator.LessThanOrEqual => "lessThanOrEqual",
        _ => "between",
    };

    private static string ToAlertStyleString(DvAlertStyle style) => style switch
    {
        DvAlertStyle.Warning => "warning",
        DvAlertStyle.Information => "information",
        _ => "stop",
    };
}
