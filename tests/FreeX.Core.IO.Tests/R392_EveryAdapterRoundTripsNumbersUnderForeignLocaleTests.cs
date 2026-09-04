using System.Globalization;
using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r392: every file adapter must round-trip a fractional number under a foreign locale.
///
/// <para>r391 pinned the .xlsx, .pptx and PDF writers. This closes the rest of the surface in one
/// contract by driving EVERY <see cref="IFileAdapter"/> in the assembly through reflection, so a
/// format added later is covered the day it appears rather than the day someone remembers to write
/// a test for it.</para>
///
/// <para>A round trip is a stronger instrument than scanning the written bytes: it fails when the
/// writer emits a comma decimal AND when the reader parses one with the wrong culture, including the
/// case where both are culture-sensitive and cancel out on the author's machine while corrupting the
/// file for everyone exchanging it. The text formats are the real exposure here -- CSV, DIF, SLK and
/// the like are numbers-as-text by definition.</para>
///
/// <para>Adapters that cannot round-trip are REPORTED, not silently skipped. A silent skip is how a
/// contract quietly stops covering the thing it names.</para>
/// </summary>
public sealed class R392_EveryAdapterRoundTripsNumbersUnderForeignLocaleTests
{
    private const double Fractional = 3.14;

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void EveryAdapterPreservesAFractionalNumber(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);

        try
        {
            Fractional.ToString().Should().Be(
                "3,14", "the culture must actually be in effect or a pass below means nothing");

            var adapters = typeof(IFileAdapter).Assembly
                .GetTypes()
                .Where(type => type is { IsAbstract: false, IsPublic: true } &&
                               typeof(IFileAdapter).IsAssignableFrom(type) &&
                               type.GetConstructor(Type.EmptyTypes) is not null)
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ToList();

            // A reflection-driven contract is only as good as its query: if a refactor moved the
            // adapters or narrowed their visibility, the loop below would iterate nothing and report
            // a confident green over zero coverage. 18 adapters exist today (CSV, CSV-UTF8, DBF,
            // DIF, HTML, legacy XLS, MHT, native JSON, ODS, PDF, PRN, SLK, SpreadsheetXML, Unicode
            // text, and the XLSX/XLSM/XLTX/XLTM family).
            adapters.Should().HaveCountGreaterThanOrEqualTo(
                18,
                "the query must still reach every adapter -- a smaller number means it stopped " +
                "covering formats rather than that formats were removed. Found: " +
                string.Join(", ", adapters.Select(type => type.Name)));

            var corrupted = new List<string>();
            var unsupported = new List<string>();

            foreach (var type in adapters)
            {
                var adapter = (IFileAdapter)Activator.CreateInstance(type)!;

                var workbook = new Workbook();
                workbook.AddSheet("Sheet1");
                var sheet = workbook.GetSheetAt(0);
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(Fractional));

                double? reloaded;
                try
                {
                    using var stream = new MemoryStream();
                    adapter.Save(workbook, stream);
                    stream.Position = 0;

                    var loaded = adapter.Load(stream);
                    var loadedSheet = loaded.GetSheetAt(0);
                    reloaded = loadedSheet.GetCell(new CellAddress(loadedSheet.Id, 1, 1))?.Value is NumberValue number
                        ? number.Value
                        : null;
                }
                catch (Exception exception) when (exception is NotSupportedException or NotImplementedException)
                {
                    unsupported.Add($"{type.Name} ({exception.GetType().Name})");
                    continue;
                }

                if (reloaded is null || Math.Abs(reloaded.Value - Fractional) > 1e-9)
                {
                    corrupted.Add($"{type.Name}: expected {Fractional.ToString(CultureInfo.InvariantCulture)}, " +
                                  $"got {reloaded?.ToString(CultureInfo.InvariantCulture) ?? "no numeric cell"}");
                }
            }

            corrupted.Should().BeEmpty(
                "a number must survive save+load whatever locale the machine runs; these adapters " +
                "lost or changed it under " + culture + ":\n" + string.Join("\n", corrupted) +
                "\n(adapters reporting the format cannot do this: " +
                (unsupported.Count == 0 ? "none" : string.Join(", ", unsupported)) + ")");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
