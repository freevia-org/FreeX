using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R115-io-stylesheet-dxf-prune-1: deleting a conditional-format rule that had a dxf never removed
/// its stale &lt;dxf&gt; entry from styles.xml on a re-save of a previously-opened file.
/// <see cref="XlsxFileAdapter.Save"/> always rebuilds &lt;dxfs&gt; from the model's CURRENT rules
/// (sized exactly to that count), but <see cref="XlsxStylesheetMetadataPreserver"/>'s
/// MergeStylesheetDifferentialStyles then merged the ORIGINAL source package's &lt;dxfs&gt; back in,
/// and for any source dxf beyond the rebuilt count it unconditionally re-appended a raw clone with
/// no liveness check at all -- resurrecting the deleted rule's now-unreferenced dxf. Because the
/// just-saved package is rebased to become the next save's tracked "source" (see
/// XlsxFileAdapter.SavePostProcessing.cs), the zombie entry then persisted forever, regenerated on
/// every subsequent save. The fix (ComputeLiveTrailingDifferentialStyleIndexes in
/// XlsxStylesheetMetadataPreserver.cs) only keeps a trailing source dxf alive when something that
/// survives into the final package still addresses it by that exact source-side index.
/// </summary>
public sealed class R115_DeletedConditionalFormatDxfPruneTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static XDocument LoadStylesXml(Stream xlsxStream)
    {
        xlsxStream.Position = 0;
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/styles.xml")!;
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }

    /// <summary>
    /// Builds a workbook with two dxf-bearing classic CF rules (distinct fill colors), saves it (no
    /// source package yet), reloads it (now tracked with a source package), then drives the REAL
    /// "Manage Rules -> delete a rule" entry point (<see cref="ReplaceAllConditionalFormatsCommand"/>)
    /// to drop the red rule, and saves again through the SAME adapter instance (so
    /// XlsxFileAdapter.SourcePackages actually tracks the loaded-from-file workbook, exactly like a
    /// real open-edit-save session).
    /// </summary>
    private static (Workbook Loaded, XlsxFileAdapter Adapter) BuildLoadedWorkbookWithTwoCfRules()
    {
        var seed = new Workbook("R115DxfPrune");
        var sheet = seed.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(50));

        var redRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            FormulaText = "3",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0), FillPatternStyle = CellFillPatternStyle.Solid },
        };
        var greenRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 2,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.LessThan,
            FormulaText = "1000",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(0, 255, 0), FillPatternStyle = CellFillPatternStyle.Solid },
        };
        sheet.ConditionalFormats.Add(redRule);
        sheet.ConditionalFormats.Add(greenRule);

        var adapter = new XlsxFileAdapter();
        using var seedStream = new MemoryStream();
        adapter.Save(seed, seedStream);
        seedStream.Position = 0;

        // Reload through the SAME adapter instance so the loaded workbook is tracked in
        // XlsxFileAdapter.SourcePackages, exactly like a real "open an existing file" session.
        var loaded = adapter.Load(seedStream);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.ConditionalFormats.Should().HaveCount(2, "both dxf-bearing rules must round-trip before the deletion step");

        // Real product entry point: Manage Conditional Formats dialog deleting the red rule.
        var remaining = loadedSheet.ConditionalFormats.Where(r => r.FormatIfTrue?.FillColor == new CellColor(0, 255, 0)).ToList();
        remaining.Should().ContainSingle();
        var deleteCommand = new ReplaceAllConditionalFormatsCommand(loadedSheet.Id, remaining);
        var ctx = new TestCommandContext(loaded);
        deleteCommand.Apply(ctx).Success.Should().BeTrue();
        loadedSheet.ConditionalFormats.Should().ContainSingle();

        return (loaded, adapter);
    }

    [Fact]
    public void DeletingCfRule_PrunesItsDxf_NotResurrectedOnSave()
    {
        var (loaded, adapter) = BuildLoadedWorkbookWithTwoCfRules();

        using var savedStream = new MemoryStream();
        adapter.Save(loaded, savedStream);

        var stylesXml = LoadStylesXml(savedStream);
        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")?.Elements(WorksheetNs + "dxf").ToList()
            ?? new List<XElement>();

        dxfs.Should().ContainSingle(
            "the deleted red rule's dxf must be pruned, not blindly re-appended past the rebuilt count");

        var survivingFill = dxfs[0].Element(WorksheetNs + "fill")?
            .Element(WorksheetNs + "patternFill")?
            .Element(WorksheetNs + "bgColor")?
            .Attribute("rgb")?.Value;
        survivingFill.Should().Be("FF00FF00", "the surviving rule's own green dxf must be the one that remains");

        // Reload the RESULT and confirm only one rule -- and its correct color -- reads back.
        savedStream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(savedStream);
        var reloadedRules = reloaded.GetSheetAt(0).ConditionalFormats;
        reloadedRules.Should().ContainSingle();
        reloadedRules[0].FormatIfTrue!.FillColor.Should().Be(new CellColor(0, 255, 0));
    }

    /// <summary>
    /// The exact "persists indefinitely" claim from the defect: the just-saved package is rebased to
    /// become the NEXT save's tracked source (XlsxFileAdapter.SavePostProcessing.cs), so a second,
    /// no-op save through the same adapter instance must not regrow the pruned dxf either.
    /// </summary>
    [Fact]
    public void DeletingCfRule_DxfStaysPruned_AcrossMultipleSubsequentSaves()
    {
        var (loaded, adapter) = BuildLoadedWorkbookWithTwoCfRules();

        using var firstSave = new MemoryStream();
        adapter.Save(loaded, firstSave);

        using var secondSave = new MemoryStream();
        adapter.Save(loaded, secondSave);

        var secondStylesXml = LoadStylesXml(secondSave);
        var dxfs = secondStylesXml.Root!.Element(WorksheetNs + "dxfs")?.Elements(WorksheetNs + "dxf").ToList()
            ?? new List<XElement>();
        dxfs.Should().ContainSingle(
            "the zombie dxf must not be regenerated on a second, later save either -- it must stay pruned for good");
    }

    /// <summary>
    /// No-regression sibling: when NO rule is deleted (the CF rule set is unchanged across a
    /// save-reload-resave cycle), every dxf must still survive untouched -- this fix must not start
    /// dropping trailing dxfs that are still genuinely in range/live for an unmodified rule set. This
    /// exercises the same index-aligned RendersEquivalentDifferentialStyle merge branch the fix does
    /// NOT touch, only the "index >= target count" append branch above it.
    /// </summary>
    [Fact]
    public void UnchangedCfRuleSet_KeepsBothDxfs_NoRegression()
    {
        var seed = new Workbook("R115DxfPruneNoRegression");
        var sheet = seed.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(50));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            FormulaText = "3",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0), FillPatternStyle = CellFillPatternStyle.Solid },
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 2,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.LessThan,
            FormulaText = "1000",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(0, 255, 0), FillPatternStyle = CellFillPatternStyle.Solid },
        });

        var adapter = new XlsxFileAdapter();
        using var seedStream = new MemoryStream();
        adapter.Save(seed, seedStream);
        seedStream.Position = 0;

        var loaded = adapter.Load(seedStream);
        loaded.GetSheetAt(0).ConditionalFormats.Should().HaveCount(2);

        // No edits at all -- just a plain re-save of the loaded, unmodified workbook.
        using var resavedStream = new MemoryStream();
        adapter.Save(loaded, resavedStream);

        var stylesXml = LoadStylesXml(resavedStream);
        var dxfs = stylesXml.Root!.Element(WorksheetNs + "dxfs")!.Elements(WorksheetNs + "dxf").ToList();
        dxfs.Should().HaveCount(2, "neither rule was deleted, so both dxfs must still survive the save");

        var fillColors = dxfs
            .Select(dxf => dxf.Element(WorksheetNs + "fill")?
                .Element(WorksheetNs + "patternFill")?
                .Element(WorksheetNs + "bgColor")?
                .Attribute("rgb")?.Value)
            .ToList();
        fillColors.Should().Contain("FFFF0000");
        fillColors.Should().Contain("FF00FF00");
    }
}
