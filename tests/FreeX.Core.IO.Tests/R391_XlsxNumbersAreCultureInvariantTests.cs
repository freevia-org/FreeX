using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r391: numbers written into an .xlsx package must be culture-invariant.
///
/// <para>SpreadsheetML cell values are xsd:double, which accepts only <c>.</c> as the decimal point.
/// A number formatted under a German or French locale produces a file Excel rejects or misreads --
/// and it does so only for users running that locale, never on the machine that wrote the code.</para>
///
/// <para>The writer is invariant today; this pins the OUTPUT so any future culture-sensitive
/// formatting fails here however it is written. Every case self-checks that the culture actually
/// took effect before trusting a pass, and the scan covers every XML part in the package rather
/// than the worksheet alone.</para>
/// </summary>
public sealed class R391_XlsxNumbersAreCultureInvariantTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void SavedNumbersUseInvariantFormatting(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            // Self-check: prove the culture actually took effect on THIS thread, so a pass below
            // means the writer is invariant rather than the probe being inert.
            Assert.Equal("3,14", 3.14.ToString());

            var workbook = new Workbook();
            workbook.AddSheet("Sheet1");
            var sheet = workbook.GetSheetAt(0);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(3.14));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(-1234567.891));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(1e-7));

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(workbook, stream);
            stream.Position = 0;

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var report = new System.Text.StringBuilder();
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".xml", StringComparison.Ordinal) &&
                    !entry.FullName.EndsWith(".rels", StringComparison.Ordinal))
                {
                    continue;
                }

                using var entryStream = entry.Open();
                var text = new StreamReader(entryStream).ReadToEnd();

                // A decimal comma inside an XML attribute or element value is the signature of a
                // culture-sensitive ToString reaching the wire.
                foreach (var suspect in new[] { "3,14", "-1234567,891" })
                {
                    if (text.Contains(suspect, StringComparison.Ordinal))
                        report.AppendLine($"{entry.FullName}: {suspect}");
                }
            }

            Assert.True(report.Length == 0, "culture-formatted numbers on the wire:\n" + report);
            Assert.Contains("3.14", XDocument.Load(
                archive.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
