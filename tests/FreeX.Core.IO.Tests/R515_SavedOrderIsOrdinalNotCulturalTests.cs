using System.IO;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r515: the .fxl save path ordered DisabledFormulaErrorCodes with the DEFAULT comparer, which for
/// strings is culture-sensitive. The set itself is OrdinalIgnoreCase and its validator accepts codes
/// case-insensitively, so a workbook can legitimately hold "#VALUE!" next to "#name?" -- and those
/// two order differently under the two comparers: ordinal compares UTF-16 units, so 'V' (86) sorts
/// before 'n' (110), while a culture-aware collation sorts n before v. The saved file's byte order
/// therefore depended on the running locale, which breaks the byte-determinism the save-idempotence
/// work relies on.
/// </summary>
public sealed class R515_SavedOrderIsOrdinalNotCulturalTests
{
    [Fact]
    public void DisabledFormulaErrorCodes_SerialiseInOrdinalOrder()
    {
        var workbook = new Workbook();

        // Mixed case is reachable: the set is OrdinalIgnoreCase and the save-side validator compares
        // OrdinalIgnoreCase, so whatever casing arrives first is what gets stored and written.
        workbook.DisabledFormulaErrorCodes.Add("#VALUE!");
        workbook.DisabledFormulaErrorCodes.Add("#name?");

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        var json = Encoding.UTF8.GetString(stream.ToArray());

        var value = json.IndexOf("#VALUE!", System.StringComparison.Ordinal);
        var name = json.IndexOf("#name?", System.StringComparison.Ordinal);

        value.Should().BeGreaterThan(-1, "the disabled codes must actually reach the saved document");
        name.Should().BeGreaterThan(-1, "the disabled codes must actually reach the saved document");

        // Ordinal order. Under the culture comparer this pair comes out the other way round, so this
        // assertion is what distinguishes the two.
        value.Should().BeLessThan(name);
    }
}
