using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r310: a format that holds one sheet must keep the sheet the user is looking at.
///
/// <para>Excel's Save-As exports the active sheet, and FreeX had already decided the same --
/// <c>DelimitedTextWorkbookWriter</c> says so in a comment and DIF, PRN and SLK follow it. The HTML
/// writer took <c>Sheets[0]</c> instead, so a user who switched tabs and exported silently got a
/// different sheet than the one on screen. r309's log recorded this as a product decision left open;
/// it was not one -- the decision was already made, and one writer did not follow it.</para>
/// </summary>
public sealed class R310_SingleSheetSaveKeepsTheActiveSheetTests
{
    private static Workbook ThreeSheetsWithActive(int activeIndex)
    {
        var workbook = new Workbook("Book1");
        foreach (var name in new[] { "First", "Middle", "Last" })
        {
            var sheet = workbook.AddSheet(name);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue($"marker-{name}"));
        }

        workbook.Sheets.Should().HaveCount(3, "the fixture depends on exactly these three sheets");
        workbook.ActiveSheetIndex = activeIndex;
        return workbook;
    }

    private static string SaveToText(IFileAdapter adapter, Workbook workbook)
    {
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static TheoryData<string> SingleSheetAdapters() => new()
    {
        nameof(HtmlFileAdapter), nameof(CsvFileAdapter), nameof(DifFileAdapter),
        nameof(PrnFileAdapter), nameof(SlkFileAdapter),
    };

    private static IFileAdapter Create(string name) => name switch
    {
        nameof(HtmlFileAdapter) => new HtmlFileAdapter(),
        nameof(CsvFileAdapter) => new CsvFileAdapter(),
        nameof(DifFileAdapter) => new DifFileAdapter(),
        nameof(PrnFileAdapter) => new PrnFileAdapter(),
        nameof(SlkFileAdapter) => new SlkFileAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unmapped adapter"),
    };

    [Theory]
    [MemberData(nameof(SingleSheetAdapters))]
    public void SavingWritesTheActiveSheetNotTheFirst(string adapterName)
    {
        var text = SaveToText(Create(adapterName), ThreeSheetsWithActive(2));

        text.Should().Contain("marker-Last",
            $"{adapterName} must export the sheet the user has selected");
        text.Should().NotContain("marker-First",
            $"{adapterName} exported the first sheet in tab order instead of the active one");
    }

    /// <summary>
    /// The other direction: with the first sheet active the first sheet is what gets written, so the
    /// test above cannot pass by writing every sheet or by picking the last one.
    /// </summary>
    [Theory]
    [MemberData(nameof(SingleSheetAdapters))]
    public void SavingWritesTheFirstSheetWhenItIsTheActiveOne(string adapterName)
    {
        var text = SaveToText(Create(adapterName), ThreeSheetsWithActive(0));

        text.Should().Contain("marker-First");
        text.Should().NotContain("marker-Last");
    }

    /// <summary>
    /// Guards the hazard rather than these five adapters: a single-sheet writer added later must not
    /// reach for Sheets[0], which is the exact line this round found in the HTML writer.
    /// </summary>
    [Fact]
    public void NoSingleSheetWriterSelectsItsSheetByPosition()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var ioDirectory = Path.Combine(root, "src", "FreeX.Core.IO");
        var offenders = new List<string>();
        var examined = 0;

        foreach (var file in Directory.EnumerateFiles(ioDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("ISingleSheetFileAdapter", StringComparison.Ordinal)
                && !Regex.IsMatch(file, @"(Delimited|Html|Dif|Prn|Slk)\w*Writer\.cs$"))
            {
                continue;
            }

            examined++;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // Code only: the comment above the fixed line in HtmlTableWriter names Sheets[0] to
                // explain what it stopped doing, and a contract that cannot tell an explanation from
                // the thing it explains punishes writing the explanation down.
                var code = lines[i].TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith("///", StringComparison.Ordinal))
                    continue;

                if (lines[i].Contains("Sheets[0]", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
            }
        }

        examined.Should().BeGreaterThanOrEqualTo(6,
            "this must scan the single-sheet adapters and their writers; if it stops finding them the "
            + "contract is passing vacuously");

        offenders.Should().BeEmpty(
            "a single-sheet format keeps the ACTIVE sheet, so selecting one by tab position exports "
            + "a sheet other than the one the user is looking at:\n" + string.Join("\n", offenders));
    }
}
