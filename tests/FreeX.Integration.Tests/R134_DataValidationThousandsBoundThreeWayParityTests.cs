using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for round 134's finding: a data-validation numeric bound's text was parsed with
/// THREE different <see cref="NumberStyles"/> across three independent call sites -- the Data
/// Validation dialog's entry gate
/// (<see cref="FreeX.App.Presentation.Dialogs.DataValidationDialogModel"/>, which used
/// <see cref="NumberStyles.Float"/>, no thousands grouping at all), live enforcement while the
/// session runs (<see cref="DataValidationBoundsParser"/>, which used <see cref="NumberStyles.Any"/>
/// plus a grouping-shape guard), and file-save canonicalization
/// (<see cref="XlsxDataValidationClosedXmlMapper"/>, which used a hand-picked style set that also
/// omitted thousands grouping). A thousands-grouped bound like "1,234" therefore parsed
/// successfully for live enforcement (as 1234) but failed to parse at save time, so
/// <c>NormalizeNumericFormulaForSave</c> fell through its `_ =&gt; formula` branch and wrote the
/// ORIGINAL locale-grouped text "1,234" to the XLSX verbatim, instead of the canonical invariant
/// digits "1234" every other numeric DV bound gets.
/// <para>
/// This test drives the bug/fix through the public, portable entry points shared by both the WPF
/// and Avalonia shells: <see cref="DataValidationService.Validate"/> for live enforcement (the same
/// call both <c>MainWindow.Editing.cs</c> hosts use on every cell edit) and
/// <see cref="XlsxFileAdapter"/> Save/Load for the persisted round trip. It asserts that (1) live
/// enforcement and post-save-reload enforcement agree on the SAME thousands-grouped bound, and
/// (2) the persisted XML is culture-invariant digits, never the original grouped text.
/// </para>
/// </summary>
public sealed class R134_DataValidationThousandsBoundThreeWayParityTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void ThousandsGroupedUpperBound_LiveEnforcement_MatchesPostSaveReloadEnforcement_AndPersistsInvariant()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            // A thousands-grouped WholeNumber upper bound, exactly as it would be typed into the
            // Data Validation dialog's Formula2 box (which round 134 fixed to accept this shape --
            // see DataValidationDialogModelTests's sibling coverage of the entry gate itself).
            var dv = new DataValidation
            {
                Type = DvType.WholeNumber,
                Operator = DvOperator.Between,
                Formula1 = "1",
                Formula2 = "1,234",
            };

            // ── 1. Live enforcement (in-session, before any save) ──────────────────────
            DataValidationService.Validate(dv, new NumberValue(1000))
                .Should().BeNull("1000 is within the intended upper bound 1234 while the session runs");
            DataValidationService.Validate(dv, new NumberValue(1300))
                .Should().NotBeNull("1300 exceeds the intended upper bound 1234 while the session runs");

            // ── 2. Save + reload through the real XLSX pipeline ────────────────────────
            var workbook = new Workbook("R134");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

            var saved = new DataValidation
            {
                AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 2)),
                Type = DvType.WholeNumber,
                Operator = DvOperator.Between,
                Formula1 = "1",
                Formula2 = "1,234",
            };
            sheet.DataValidations.Add(saved);

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(workbook, stream);

            // ── 3. The persisted XML must be culture-invariant digits, never the locale-grouped
            //        text that was typed/authored -- a locale-dependent value written to file is
            //        the severe failure this fix closes.
            var persistedFormula2 = ReadFormula2(stream);
            persistedFormula2.Should().Be("1234",
                "the persisted bound must be canonical invariant digits, not the original " +
                "thousands-grouped/locale-dependent text \"1,234\"");

            stream.Position = 0;
            var reloaded = new XlsxFileAdapter().Load(stream);
            var reloadedSheet = reloaded.Sheets.Single();
            var reloadedDv = reloadedSheet.DataValidations.Single();

            // ── 4. Post-save-reload enforcement must agree EXACTLY with pre-save enforcement.
            DataValidationService.Validate(reloadedDv, new NumberValue(1000))
                .Should().BeNull("1000 must still be within the upper bound 1234 after save/reload");
            DataValidationService.Validate(reloadedDv, new NumberValue(1300))
                .Should().NotBeNull("1300 must still exceed the upper bound 1234 after save/reload -- " +
                                     "before the fix this drifted from what live enforcement allowed");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── Sibling/no-regression: a genuine comma-decimal bound (no grouping) must still round-trip. ──

    [Fact]
    public void CommaDecimalBound_UnderDeDeCulture_StillRoundTripsToInvariantDotDecimal()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var workbook = new Workbook("R134Sibling");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 2)),
                Type = DvType.Decimal,
                Operator = DvOperator.GreaterThanOrEqual,
                Formula1 = "1,5",
            });

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(workbook, stream);

            ReadFormula1(stream).Should().Be("1.5", "a genuine de-DE comma-decimal bound must still normalize to invariant dot notation");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static string? ReadFormula1(MemoryStream package) => ReadFormula(package, "formula1");

    private static string? ReadFormula2(MemoryStream package) => ReadFormula(package, "formula2");

    private static string? ReadFormula(MemoryStream package, string elementName)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root!;
        var result = root.Element(WorksheetNs + "dataValidations")?
            .Element(WorksheetNs + "dataValidation")?
            .Element(WorksheetNs + elementName)?
            .Value;
        package.Position = 0;
        return result;
    }
}
