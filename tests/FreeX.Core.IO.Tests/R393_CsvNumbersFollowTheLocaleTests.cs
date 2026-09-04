using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r393: plain CSV must write numbers with the same culture it takes its delimiter from.
///
/// <para>FreeX already picks the delimiter from the OS list separator because Excel does -- ';' on
/// de-DE, as <c>CsvFileAdapter</c>'s own comment explains, "precisely because ',' is their decimal
/// mark". But the number was still written invariantly, so FreeX produced <c>3.14;</c>: a
/// combination no locale writes. Excel on such a machine imports that as TEXT, which means every
/// number FreeX exported was unusable for exactly the user the localisation was for.</para>
///
/// <para>Only the write path was wrong. <c>DelimitedTextWorkbookReader.TryParseFiniteNumber</c>
/// already tries the current culture and then the invariant one, so FreeX read European files
/// correctly and its own round trip stayed green -- the defect was invisible to any test that only
/// went out and back.</para>
/// </summary>
public sealed class R393_CsvNumbersFollowTheLocaleTests
{
    private static string SaveCsv(double value)
    {
        var workbook = new Workbook();
        workbook.AddSheet("Sheet1");
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(value));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(value * 2));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        return DelimitedTextWorkbookWriter.ResolveAnsiEncoding().GetString(stream.ToArray());
    }

    [Fact]
    public void OnAGermanMachineTheDecimalMarkIsACommaAndTheDelimiterIsASemicolon()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            var csv = SaveCsv(3.14);

            csv.Should().Contain(
                "3,14",
                "Excel writes the locale decimal mark; \"3.14\" is imported as text on this machine");
            csv.Should().NotContain(
                "3.14",
                "an invariant decimal point beside a locale delimiter is a combination no locale writes");
            csv.Should().Contain(
                ";",
                "the delimiter must still follow the locale, which is why the decimal comma is safe");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void OnAnEnglishMachineTheOutputIsUnchanged()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");

        try
        {
            var csv = SaveCsv(3.14);

            csv.Should().Contain("3.14", "en-US writes a decimal point, exactly as before");
            csv.Should().Contain(",", "and a comma delimiter");
            csv.Should().NotContain("3,14");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// The guard: a culture whose list separator IS its decimal separator would produce ambiguous
    /// fields, so it must keep invariant numbers rather than emit "3,14" beside a ',' delimiter --
    /// which is precisely how the r392 sensitivity probe corrupted 3.14 into 3.
    /// </summary>
    [Fact]
    public void ACultureWhoseListSeparatorIsItsDecimalMarkKeepsInvariantNumbers()
    {
        var previous = CultureInfo.CurrentCulture;
        var ambiguous = (CultureInfo)new CultureInfo("de-DE").Clone();
        ambiguous.TextInfo.ListSeparator = ",";
        CultureInfo.CurrentCulture = ambiguous;

        try
        {
            ambiguous.NumberFormat.NumberDecimalSeparator.Should().Be(
                ",", "this culture is only interesting because its decimal mark collides");

            var csv = SaveCsv(3.14);

            csv.Should().Contain(
                "3.14",
                "a decimal comma beside a comma delimiter would split one number into two fields, so " +
                "this culture must fall back to invariant numbers");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
