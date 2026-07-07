using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FreeX.App.Presentation.Charts;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Regression tests for the cleanup-batch MED15 findings:
/// P94 (workbook reprotect-with-new-password left a stale modern-hash verifier for the OLD
/// password alongside the new legacy hash), P91 (c:varyColors / "Vary colors by point" was
/// persisted but never consulted by either renderer), and P20 (Scenario Summary result cells read
/// the same stale value in every scenario column when the workbook is in Manual calculation mode).
/// </summary>
public sealed class FreeXCleanupMED15Tests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── P94: reprotecting a workbook with a new password must drop the stale modern-hash bag ───

    [Fact]
    public void ProtectWorkbookCommand_AfterUnprotectingModernHashWorkbook_DropsStaleVerifierForOldPassword()
    {
        // Arrange: a workbook whose structure is locked the way Excel 2013+ locks it -- only the
        // modern ISO 29500 hash quartet (workbookAlgorithmName/workbookHashValue/workbookSaltValue/
        // workbookSpinCount), no legacy workbookPassword attribute at all.
        var workbook = new Workbook("ReprotectRoundTrip");
        workbook.AddSheet("S1");

        var adapter = new XlsxFileAdapter();
        var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        var (saltBase64, hashBase64) = ComputeReferenceHash("old password", "SHA-512", 1000,
            [1, 2, 3, 4, 5, 6, 7, 8]);
        RewriteWorkbookProtection(source, protection =>
        {
            protection.SetAttributeValue("lockStructure", "1");
            protection.SetAttributeValue("workbookAlgorithmName", "SHA-512");
            protection.SetAttributeValue("workbookHashValue", hashBase64);
            protection.SetAttributeValue("workbookSaltValue", saltBase64);
            protection.SetAttributeValue("workbookSpinCount", "1000");
        });
        source.Position = 0;

        var loaded = adapter.Load(source);
        loaded.IsStructureProtected.Should().BeTrue();
        // Sanity: the loaded model carries the preserved modern-hash bag that ApplyProtection would
        // otherwise blindly re-apply.
        loaded.ProtectionMetadata.Should().NotBeNull();

        // Act: exactly what the Protect/Unprotect Workbook dialogs do -- unprotect with the
        // (verified) old password, then protect again with a brand-new password.
        var ctx = new FakeCommandContext(loaded);
        new UnprotectWorkbookCommand("old password").Apply(ctx).Success.Should().BeTrue();
        new ProtectWorkbookCommand("new password").Apply(ctx).Success.Should().BeTrue();

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        // Assert on the raw saved XML: only ONE verifier scheme may be present, and it must be for
        // the NEW password -- not a leftover modern hash for the revoked old one.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var entryStream = entry.Open();
        var workbookXml = XDocument.Load(entryStream);
        var savedProtection = workbookXml.Root!.Element(WorkbookNs + "workbookProtection");

        savedProtection.Should().NotBeNull();
        savedProtection!.Attribute("workbookHashValue").Should().BeNull(
            "the stale modern-hash verifier for the revoked old password must not survive a reprotect with a new password");
        savedProtection.Attribute("workbookAlgorithmName").Should().BeNull();
        savedProtection.Attribute("workbookPassword").Should().NotBeNull(
            "the new password must be the only verifier written back");

        // And a fresh reload agrees with FreeX's own reader: the NEW password unlocks, the OLD one
        // (previously authoritative for Excel) no longer does.
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        ProtectionPasswordHelper.VerifyStoredPassword(reloaded.StructureProtectionPassword, "new password")
            .Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword(reloaded.StructureProtectionPassword, "old password")
            .Should().BeFalse("the revoked old password must no longer unlock the workbook in either FreeX or Excel");
    }

    // ── P91: "Vary colors by point" must actually change the resolved per-point fill color ─────

    [Fact]
    public void ResolveVaryColorsPointFill_SingleSeriesWithFlagSet_CyclesThroughPalette()
    {
        var chart = new ChartModel { Type = ChartType.Column, VaryColorsByPoint = true };
        var theme = WorkbookTheme.Office;
        var palette = ChartStylePlanner.BuildExcelSeriesPalette(theme);

        // With varyColors on and exactly one plotted series, each point index must resolve to a
        // DIFFERENT palette color (matching Excel's "vary colors by point" behavior) rather than
        // being silently ignored (the pre-fix behavior, where every bar kept the single series color).
        var point0 = ChartStylePlanner.ResolveVaryColorsPointFill(chart, seriesIndex: 0, pointIndex: 0, plottedSeriesCount: 1, theme, palette);
        var point1 = ChartStylePlanner.ResolveVaryColorsPointFill(chart, seriesIndex: 0, pointIndex: 1, plottedSeriesCount: 1, theme, palette);

        point0.Should().Be(palette[0]);
        point1.Should().Be(palette[1]);
        point0.Should().NotBe(point1);
    }

    [Fact]
    public void ResolveVaryColorsPointFill_MultiSeriesChart_IgnoresVaryColorsFlag()
    {
        // Excel only applies varyColors to a chart's sole plotted series; a multi-series chart still
        // needs one distinct color per SERIES for its legend to make sense, so the flag must be a
        // no-op there (caller falls back to its normal per-series color).
        var chart = new ChartModel { Type = ChartType.Column, VaryColorsByPoint = true };
        var theme = WorkbookTheme.Office;
        var palette = ChartStylePlanner.BuildExcelSeriesPalette(theme);

        var result = ChartStylePlanner.ResolveVaryColorsPointFill(chart, seriesIndex: 0, pointIndex: 2, plottedSeriesCount: 2, theme, palette);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveVaryColorsPointFill_ExplicitPerPointFillTakesPrecedenceOverVaryColors()
    {
        var explicitFill = new CellColor(10, 20, 30);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            VaryColorsByPoint = true,
            PointFillColors = [new ChartPointFillFormat(0, 0, FillColor: explicitFill)]
        };
        var theme = WorkbookTheme.Office;
        var palette = ChartStylePlanner.BuildExcelSeriesPalette(theme);

        var result = ChartStylePlanner.ResolveVaryColorsPointFill(chart, seriesIndex: 0, pointIndex: 0, plottedSeriesCount: 1, theme, palette);

        result.Should().Be(explicitFill);
    }

    // ── P20: Scenario Summary result cells must be recalculated per scenario even in Manual mode ─

    [Fact]
    public void ScenarioSummaryReportCommand_InManualCalculationMode_ReportsDistinctPerScenarioResults()
    {
        var workbook = new Workbook("ManualModeSummary");
        // Manual mode is the crux of the bug: the buggy call sites gated recalculation on
        // WorkbookCalculationMode.Automatic, so every scenario column repeated the same stale value.
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        var sheet = workbook.AddSheet("Sheet1");
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(b1, new NumberValue(1));
        sheet.SetFormula(c1, "B1*2");
        sheet.GetCell(c1)!.Value = new NumberValue(2);

        workbook.Scenarios.Add(new WorkbookScenario("Ten", [new ScenarioCellValue(b1, new NumberValue(10))]));
        workbook.Scenarios.Add(new WorkbookScenario("Hundred", [new ScenarioCellValue(b1, new NumberValue(100))]));

        // The recalculate delegate below mirrors exactly what WorkbookSession.
        // ExecuteScenarioManagerSummaryReportPlan / MainWindow.CreateScenarioSummaryReport now wire
        // up post-fix: an UNCONDITIONAL recalculation, independent of workbook.CalculationMode (the
        // pre-fix delegates gated this on Automatic mode via RecalculateIfAutomatic).
        var command = new ScenarioSummaryReportCommand(
            [c1],
            (book, _) =>
            {
                var targetSheet = book.GetSheet(sheet.Id)!;
                var b1Value = ((NumberValue)targetSheet.GetValue(b1)).Value;
                targetSheet.GetCell(c1)!.Value = new NumberValue(b1Value * 2);
            });

        var ctx = new FakeCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();

        var report = workbook.Sheets.Should().Contain(s => s.Name == "Scenario Summary").Which;
        // Header row 7 (no changing-cells section requested), result row 8, columns 2/3 = scenarios.
        var tenResult = report.GetValue(8, 2);
        var hundredResult = report.GetValue(8, 3);

        tenResult.Should().Be(new NumberValue(20), "the 'Ten' scenario (B1=10) must show its own recalculated C1=B1*2 result");
        hundredResult.Should().Be(new NumberValue(200), "the 'Hundred' scenario (B1=100) must show its own recalculated C1=B1*2 result");
        tenResult.Should().NotBe(hundredResult,
            "each scenario column must reflect its own recalculated result, not the same stale pre-report value repeated in Manual calculation mode");

        // The live worksheet must be restored to its pre-report state once the report is built.
        sheet.GetValue(b1).Should().Be(new NumberValue(1));
        sheet.GetValue(c1).Should().Be(new NumberValue(2));
    }

    // ── Test helpers ─────────────────────────────────────────────────────────────────────────

    private sealed class FakeCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static void RewriteWorkbookProtection(MemoryStream packageStream, Action<XElement> mutate)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument workbookXml;
            using (var entryStream = entry.Open())
                workbookXml = XDocument.Load(entryStream);

            workbookXml.Root!.Element(WorkbookNs + "workbookProtection")?.Remove();
            var protection = new XElement(WorkbookNs + "workbookProtection");
            mutate(protection);

            var bookViews = workbookXml.Root.Element(WorkbookNs + "bookViews");
            if (bookViews is not null)
                bookViews.AddBeforeSelf(protection);
            else
                workbookXml.Root.AddFirst(protection);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/workbook.xml");
            using var writeStream = newEntry.Open();
            workbookXml.Save(writeStream);
        }

        packageStream.Position = 0;
    }

    // Independent reference implementation of the ECMA-376 iterated hash used to synthesize
    // ground-truth test fixtures (kept separate from the production algorithm under test).
    private static (string SaltBase64, string HashBase64) ComputeReferenceHash(
        string password, string algorithmName, int spinCount, byte[] salt)
    {
        using HashAlgorithm algorithm = algorithmName switch
        {
            "SHA-512" => SHA512.Create(),
            "SHA-1" => SHA1.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithmName))
        };

        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var buffer = new byte[salt.Length + passwordBytes.Length];
        salt.CopyTo(buffer, 0);
        passwordBytes.CopyTo(buffer, salt.Length);
        var digest = algorithm.ComputeHash(buffer);

        for (var i = 0; i < spinCount; i++)
        {
            var iterationBuffer = new byte[digest.Length + 4];
            digest.CopyTo(iterationBuffer, 0);
            BitConverter.GetBytes(i).CopyTo(iterationBuffer, digest.Length);
            digest = algorithm.ComputeHash(iterationBuffer);
        }

        return (Convert.ToBase64String(salt), Convert.ToBase64String(digest));
    }
}
