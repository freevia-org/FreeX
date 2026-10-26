using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r465: characters XML cannot represent must not abort the save.
///
/// <para>A known past defect class, re-checked through a NEW entry path. A control character or lone
/// surrogate in cell text used to kill the WHOLE save -- not the cell, the file -- and every one of
/// these can reach a cell through an ordinary paste, which r465's sibling test shows the clipboard
/// parser passes through verbatim.</para>
///
/// <para>The fix (a shared sanitizer at the write boundaries) is guarded by a source-scan tripwire
/// across all three apps. This complements it behaviourally: a source scan proves the sanitizer is
/// CALLED, not that a hostile document still round-trips. Each case also asserts a neighbouring cell
/// survives, because "the save succeeded" would be true of a file that silently lost everything.</para>
/// </summary>
public sealed class R465_PastedIllegalCharactersStillSaveTests
{
    [Fact]
    public void HostileCharactersInCellTextDoNotAbortTheSave()
    {
        // Characters XML 1.0 cannot represent, all of which can reach a cell through a paste: a NUL
        // and other C0 controls, a lone high surrogate, and a lone low surrogate.
        var hostile = new (string Label, string Text)[]
        {
            ("NUL", "a\0b"),
            ("bell + vertical tab", "a\u0007b\u000Bc"),
            ("lone high surrogate", "a\uD83Db"),
            ("lone low surrogate", "a\uDE00b"),
            ("form feed", "a\fb"),
            ("all C0 controls", new string(Enumerable.Range(0, 32).Select(c => (char)c).ToArray())),
        };

        var failures = new List<string>();

        foreach (var (label, text) in hostile)
        {
            var workbook = new Workbook("hostile");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("survivor"));

            try
            {
                using var stream = new MemoryStream();
                new XlsxFileAdapter().Save(workbook, stream);
                stream.Position = 0;

                var reloaded = new XlsxFileAdapter().Load(stream);
                var cells = reloaded.Sheets.Sum(s => s.EnumerateCells().Count());
                var survivor = reloaded.Sheets[0].GetValue(2, 1);

                if (cells != 2 || (survivor as TextValue)?.Value != "survivor")
                    failures.Add($"{label} :: saved but lost content (cells={cells})");
            }
            catch (Exception ex)
            {
                failures.Add($"{label} :: SAVE FAILED {ex.GetType().Name}");
            }
        }

        failures.Should().BeEmpty(
            "one hostile character must cost at most its own cell -- it used to kill the entire save, " +
            "so the whole file was unwritable because of one pasted control character",
            Array.Empty<object>());
    }
}
