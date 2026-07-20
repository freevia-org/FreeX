using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round-55 io-a bucket findings:
/// <list type="bullet">
///   <item>R55-io-data-validation-round-trip-5-1: a non-List data-validation rule (e.g. Decimal)
///     with a cross-sheet bound formula that ClosedXML silently auto-promotes into its own x14
///     extension block must not be deleted by <see cref="XlsxX14DataValidationWriter"/> when the
///     same sheet also has a genuine FreeX-modeled x14 List rule.</item>
///   <item>R55-io-cf-rule-types-writer-5-1: a freshly-authored ContainsText/NotContainsText/
///     BeginsWith/EndsWith/Blanks/NoBlanks/Errors/NoErrors conditional-format rule (created
///     through the app, never round-tripped from an existing file) must be saved with a real
///     Excel-evaluable <c>&lt;formula&gt;</c> (and, for the text-rule family, an
///     <c>operator</c> attribute) instead of an inert metadata-only shell.</item>
/// </list>
///
/// R55-io-defined-names-scope-5-1 (setting/clearing a print area on the cell-patch-save path)
/// was investigated but not fixed here: XlsxFileAdapter.SourcePackageSnapshot.cs's generic
/// "change_unsupported_model_delta" model-fingerprint check (in TryGetPatchableValueChanges)
/// already forces a full ClosedXML rebuild whenever the model's full-serialization fingerprint
/// -- which includes Sheet.PrintAreas/PrintTitleRows/PrintTitleColumns via
/// NativeJsonAdapter.SaveForPatchValidationFingerprint -- diverges from the patch baseline. A
/// print-area/print-titles change (set, clear, or edit) always causes that divergence, so the
/// patch-save path never actually silently drops it in practice; a dedicated presence guard in
/// PackageAllowsCellPatchSave would be redundant. See the structured result for details.
/// </summary>
public sealed class R55_IoABucketTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── R55-io-data-validation-round-trip-5-1 ───────────────────────────────────────────────

    [Fact]
    public void Save_DecimalCrossSheetRuleAlongsideGenuineX14ListRule_BothSurviveRoundTrip()
    {
        var workbook = new Workbook("R55X14MergeTest");
        var sheet = workbook.AddSheet("Sheet1");
        var rules = workbook.AddSheet("Rules");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        rules.SetCell(new CellAddress(rules.Id, 1, 1), new NumberValue(0));
        rules.SetCell(new CellAddress(rules.Id, 2, 1), new NumberValue(100));
        rules.SetCell(new CellAddress(rules.Id, 1, 2), new TextValue("Alpha"));
        rules.SetCell(new CellAddress(rules.Id, 2, 2), new TextValue("Beta"));
        rules.SetCell(new CellAddress(rules.Id, 3, 2), new TextValue("Gamma"));

        // D4: a Decimal 'between' rule bound to cross-sheet cell references. FreeX's own
        // authoring path only ever sets IsX14 for List rules (DataValidationDialogPlanner.
        // RequiresX14ForListSource), so this one is left IsX14=false even though ClosedXML's own
        // writer silently auto-promotes it into the x14 extension on save.
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 4, 4), new CellAddress(sheet.Id, 4, 4)),
            Type = DvType.Decimal,
            Operator = DvOperator.Between,
            Formula1 = "Rules!$A$1",
            Formula2 = "Rules!$A$2",
            IsX14 = false,
        });

        // E5: a genuine cross-sheet List rule, correctly marked IsX14.
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 5, 5)),
            Type = DvType.List,
            Formula1 = "Rules!$B$1:$B$3",
            IsX14 = true,
        });

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var reloadedSheet = adapter.Load(stream).GetSheetAt(0);
        reloadedSheet.DataValidations.Should().HaveCount(
            2,
            "the Decimal rule ClosedXML auto-promoted into the x14 block must not be deleted when " +
            "XlsxX14DataValidationWriter rewrites the block for the genuine List x14 rule " +
            "(R55-io-data-validation-round-trip-5-1)");
    }

    [Fact]
    public void Save_SingleX14ListRule_StillRoundTrips()
    {
        var workbook = new Workbook("R55X14SingleRuleTest");
        var sheet = workbook.AddSheet("Sheet1");
        var rules = workbook.AddSheet("Rules");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        rules.SetCell(new CellAddress(rules.Id, 1, 2), new TextValue("Alpha"));
        rules.SetCell(new CellAddress(rules.Id, 2, 2), new TextValue("Beta"));

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 5, 5)),
            Type = DvType.List,
            Formula1 = "Rules!$B$1:$B$2",
            IsX14 = true,
        });

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var reloadedSheet = adapter.Load(stream).GetSheetAt(0);
        reloadedSheet.DataValidations.Should().ContainSingle(
            dv => dv.Type == DvType.List && dv.IsX14,
            "the ordinary single-x14-rule case (no foreign entry to preserve) must be unaffected " +
            "by the merge-instead-of-replace fix (no regression from R55-io-data-validation-round-trip-5-1)");
    }

    // ── R55-io-cf-rule-types-writer-5-1 ──────────────────────────────────────────────────────

    [Fact]
    public void Save_FreshlyAuthoredContainsTextRule_SynthesizesOperatorAndFormula()
    {
        var workbook = new Workbook("R55CfSynthesizeTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("urgent memo"));

        // Mirrors ConditionalFormatRuleBuilder.Build's actual output for a fresh "Text that
        // Contains" rule created through the ribbon/dialog: TextRuleText is set, FormulaText is
        // left null (nothing in the app populates it for this rule type).
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "urgent",
        });

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        var cfRuleElement = ReadCfRuleElement(stream, "xl/worksheets/sheet1.xml", "containsText");
        ((string?)cfRuleElement.Attribute("operator")).Should().Be(
            "containsText",
            "real Excel always writes an operator attribute for a containsText rule -- without it " +
            "the rule is unrecognizable to Excel's own CF UI (R55-io-cf-rule-types-writer-5-1)");
        var formula = cfRuleElement.Element(WorksheetNs + "formula")?.Value;
        formula.Should().NotBeNullOrWhiteSpace(
            "without a <formula> child, real Excel treats a containsText rule as inert and never " +
            "highlights anything on reopen (R55-io-cf-rule-types-writer-5-1)");
        formula.Should().Contain("SEARCH(\"urgent\"");
    }

    [Fact]
    public void Save_ContainsTextRuleWithExplicitFormulaText_PreservesFormulaVerbatim()
    {
        var workbook = new Workbook("R55CfExplicitFormulaTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("urgent memo"));

        const string explicitFormula = "NOT(ISERROR(SEARCH((\"urgent\"),(A1))))";
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "urgent",
            FormulaText = explicitFormula,
        });

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        var cfRuleElement = ReadCfRuleElement(stream, "xl/worksheets/sheet1.xml", "containsText");
        cfRuleElement.Element(WorksheetNs + "formula")?.Value.Should().Be(
            explicitFormula,
            "a rule that already carries an explicit FormulaText (e.g. round-tripped from a real " +
            "Excel file) must not have it overwritten by the synthesized fallback formula (no " +
            "regression from R55-io-cf-rule-types-writer-5-1's fix)");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static XElement ReadCfRuleElement(MemoryStream package, string worksheetPath, string cfRuleType)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(worksheetPath)!;
        XDocument doc;
        using (var entryStream = entry.Open())
            doc = XDocument.Load(entryStream);

        return doc.Root!
            .Elements(WorkbookNs + "conditionalFormatting")
            .Elements(WorkbookNs + "cfRule")
            .Single(rule => (string?)rule.Attribute("type") == cfRuleType);
    }
}
