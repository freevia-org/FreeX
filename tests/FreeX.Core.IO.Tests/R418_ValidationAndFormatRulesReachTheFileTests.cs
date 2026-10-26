using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r418: data-validation and conditional-format rules must survive an .xlsx round trip field by field.
///
/// <para>r413-r415 swept the styling models. These two are a harder case and a worse failure: a
/// dropped style is visible, whereas a validation rule that loses its operator or its bounds keeps
/// LOOKING like a rule while silently no longer enforcing what the user set. The cell still shows a
/// dropdown; it just stops rejecting the values it was created to reject.</para>
///
/// <para>Each property is set to a value distinct from its default -- several here default to true,
/// so testing with true would round-trip trivially through a writer that emits nothing.</para>
/// </summary>
public sealed class R418_ValidationAndFormatRulesReachTheFileTests
{
    private static Workbook WorkbookWith(Action<Sheet> seed)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        seed(sheet);
        return workbook;
    }

    private static Sheet RoundTrip(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream).Sheets[0];
    }

    private static DataValidation NewRule(Sheet sheet) => new()
    {
        AppliesTo = GridRange.Parse("A1:A5", sheet.Id),
        Type = DvType.WholeNumber,
        Operator = DvOperator.Between,
    };

    [Fact]
    public void EverySimpleValidationPropertySurvivesAnXlsxRoundTrip()
    {
        var properties = typeof(DataValidation).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property is { CanRead: true, CanWrite: true })
            .Where(property => property.PropertyType == typeof(bool) ||
                               property.PropertyType == typeof(string) ||
                               property.PropertyType.IsEnum)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();

        properties.Should().HaveCountGreaterThanOrEqualTo(
            8, "the query must still reach the validation model");

        var lost = new List<string>();

        foreach (var property in properties)
        {
            // Distinct from each default: AllowBlank, ShowDropdown, ShowInputMessage and
            // ShowErrorMessage all default to true, so a writer emitting nothing would pass if the
            // probe value were true as well.
            object? value = property.PropertyType switch
            {
                var type when type == typeof(bool) => false,
                var type when type == typeof(string) => "probe-" + property.Name,
                _ => Enum.GetValues(property.PropertyType).Cast<object>().Skip(1).FirstOrDefault(),
            };

            if (value is null)
                continue;

            var workbook = WorkbookWith(sheet =>
            {
                var rule = NewRule(sheet);
                property.SetValue(rule, value);
                sheet.DataValidations.Add(rule);
            });

            var reloaded = RoundTrip(workbook).DataValidations.FirstOrDefault();
            if (reloaded is null || !Equals(property.GetValue(reloaded), value))
            {
                lost.Add($"{property.Name}: wrote {value}, read " +
                         (reloaded is null ? "(no rule)" : property.GetValue(reloaded)?.ToString() ?? "(null)"));
            }
        }

        lost.Should().BeEmpty(
            "a validation rule that loses a field still looks like a rule and stops enforcing what " +
            "the user set, with nothing on screen to show it:\n" + string.Join("\n", lost));
    }

    [Fact]
    public void AValidationRuleSurvivesAtAll()
    {
        // The control for the sweep above: if rules did not survive the round trip at all, every
        // property comparison would fail for one reason and the per-property detail would be noise.
        var workbook = WorkbookWith(sheet => sheet.DataValidations.Add(NewRule(sheet)));

        RoundTrip(workbook).DataValidations
            .Should().NotBeEmpty("the rule itself must round-trip before its fields can be compared");
    }

    [Fact]
    public void AConditionalFormatRuleSurvivesAtAll()
    {
        var workbook = WorkbookWith(sheet => sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = GridRange.Parse("A1:A5", sheet.Id),
        }));

        RoundTrip(workbook).ConditionalFormats
            .Should().NotBeEmpty("a conditional-format rule must survive a save and reload");
    }
}
