using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r415: every simple cell-style property must survive an .xlsx save and reload.
///
/// <para>Third app in the set. r412 found a real instance of this class in FreeP -- an edit applied
/// on screen, written nowhere, gone on reopen -- and r413/r414 generalised it there and in FreeW.
/// A spreadsheet hides it best of the three: a dropped number format or wrap setting looks like a
/// formatting quirk, not a save bug, and the value it was protecting is still on screen.</para>
///
/// <para>Dxf* properties are excluded: they are the differential-format slots used by conditional
/// formatting, not a cell's own style, and do not travel through a cell's xf record.</para>
/// </summary>
public sealed class R415_EveryCellStylePropertyReachesTheFileTests
{
    [Fact]
    public void EverySimpleCellStylePropertySurvivesAnXlsxRoundTrip()
    {
        var properties = typeof(CellStyle).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property is { CanRead: true, CanWrite: true })
            .Where(property => property.PropertyType == typeof(bool) || property.PropertyType == typeof(double) ||
                               property.PropertyType == typeof(string) || property.PropertyType == typeof(int))
            .Where(property => !property.Name.StartsWith("Dxf", StringComparison.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();

        properties.Should().HaveCountGreaterThanOrEqualTo(
            15, "the query must still reach the style model rather than quietly matching little");

        var lost = new List<string>();

        foreach (var property in properties)
        {
            // Values chosen to differ from each property's default -- a value equal to the default
            // would round-trip trivially and prove nothing, which is how this kind of sweep ends up
            // green over a writer that emits nothing at all.
            object? value = property.PropertyType switch
            {
                var type when type == typeof(bool) => property.Name != "Locked",
                var type when type == typeof(double) => property.Name == "FontSize" ? 14.5d : 2.0d,
                var type when type == typeof(int) => property.Name == "TextRotation" ? 45 : 3,
                var type when type == typeof(string) => property.Name switch
                {
                    "NumberFormat" => "0.00",
                    "FontName" => "Verdana",
                    _ => "probe",
                },
                _ => null,
            };

            if (value is null)
                continue;

            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            var style = new CellStyle();
            property.SetValue(style, value);

            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
            sheet.GetCell(1, 1)!.StyleId = workbook.RegisterStyle(style);

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(workbook, stream);
            stream.Position = 0;

            var reloaded = new XlsxFileAdapter().Load(stream);
            var cell = reloaded.Sheets[0].GetCell(1, 1);
            var reloadedStyle = cell is null ? null : reloaded.GetStyle(cell.StyleId);

            if (reloadedStyle is null || !Equals(property.GetValue(reloadedStyle), value))
            {
                lost.Add($"{property.Name}: wrote {value}, read " +
                         (reloadedStyle is null ? "(no style)" : property.GetValue(reloadedStyle)?.ToString() ?? "(null)"));
            }
        }

        lost.Should().BeEmpty(
            "a style property the writer drops is applied on screen and gone on reopen, and in a " +
            "spreadsheet it reads as a formatting quirk rather than a save bug:\n" + string.Join("\n", lost));
    }
}
