using System.Reflection;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r201: the "a serializer that carries a SUBSET of its model type" question found a defect on every
/// pass for four rounds -- the .fxl chart DTO, the conditional-format DTO, the sparkline DTO, and
/// then the sheet and workbook DTOs. Each round found one, fixed it, and left the class alive.
/// <para>
/// This test retires the class instead of sampling it. It reflects over every <c>*Dto</c> the native
/// .fxl serializer declares, pairs it with the model type of the same name, and fails when the model
/// has a public settable member the DTO does not. A new model property that someone forgets to carry
/// now fails here rather than waiting to be noticed by a future review round -- which is the only
/// form "exhausted" can take for a class that grows with the model.
/// </para>
/// <para>
/// <see cref="IntentionallyNotSerialized"/> is the escape hatch, and every entry needs a reason. It
/// is deliberately a list of exact <c>Type.Member</c> pairs rather than a name pattern, so adding a
/// property does not silently inherit an exemption.
/// </para>
/// </summary>
public sealed class R201_NativeDtoCoverageContractTests
{
    /// <summary>
    /// Model members the .fxl format deliberately does not carry, each with the reason it is
    /// transient rather than document state.
    /// </summary>
    private static readonly Dictionary<string, string> IntentionallyNotSerialized = new()
    {
        ["Workbook.FilePath"] = "where the file currently lives is not part of its content",
        ["Workbook.HasPendingManualRecalculation"] =
            "a session flag; a reopened workbook recalculates according to its own settings",
        ["Cell.CachedAst"] =
            "a parse cache, rebuilt on demand and cleared whenever FormulaText changes",
        ["WorkbookTheme.EffectDefaults"] =
            "derived: ReadFormatSchemeEffectDefaults recomputes it from NativeFormatSchemeXml, "
            + "which this DTO does carry",
        ["Sheet.ValidationCircleCells"] =
            "the Circle Invalid Data overlay, a view state cleared by its own command -- Excel does "
            + "not persist circles either",
        ["DataValidation.Id"] =
            "a fresh per-load identity; nothing durable stores it, so regenerating cannot dangle "
            + "the way a structured-table id can (see finding 60)",
        ["ConditionalFormat.Id"] = "a fresh per-load identity, as DataValidation.Id",
        ["Workbook.NextStructuredTableIdWatermark"] =
            "deliberately NOT persisted: R109 folds every slicer's and pivot cache's SourceTableId "
            + "into CreateStructuredTableCommand.NextTableId, and "
            + "R109_StructuredTableIdWatermarkPersistenceTests asserts the reloaded watermark is 0 "
            + "precisely because that floor-fold is what blocks reissuing a freed id. r201 carried it "
            + "and that test caught it -- the fold makes persistence unnecessary, and a durable "
            + "watermark would only ratchet the id space upward across every save",
    };

    /// <summary>
    /// Model members the DTO carries under a different name. Checked, not just excused: the named
    /// DTO member must exist, so a rename on either side fails here rather than passing quietly.
    /// </summary>
    private static readonly Dictionary<string, string> CarriedUnderADifferentName = new()
    {
        ["Cell.FormulaText"] = "Formula",
        ["Cell.ArrayMode"] = "FormulaArrayMode",
    };

    [Fact]
    public void EveryNativeDtoCarriesEveryMemberOfItsModelType()
    {
        var gaps = new List<string>();
        var pairsChecked = 0;

        foreach (var (dto, model) in PairedTypes())
        {
            pairsChecked++;
            var carried = SettableMemberNames(dto);

            foreach (var member in SettableMemberNames(model))
            {
                var key = $"{model.Name}.{member}";

                if (carried.Contains(member) || IntentionallyNotSerialized.ContainsKey(key))
                    continue;

                if (CarriedUnderADifferentName.TryGetValue(key, out var alias))
                {
                    if (!carried.Contains(alias))
                        gaps.Add($"{dto.Name} was said to carry {key} as '{alias}', but has no such member");
                    continue;
                }

                gaps.Add($"{dto.Name} does not carry {key}");
            }
        }

        pairsChecked.Should().BeGreaterThan(10,
            "the reflection pairing must actually be finding DTO/model pairs -- if this drops to "
            + "zero the test passes vacuously and stops guarding anything");

        gaps.Should().BeEmpty(
            "a .fxl round trip silently discards anything the DTO does not carry. Add the member to "
            + "the DTO and to BOTH conversion directions, or record it in IntentionallyNotSerialized "
            + "/ CarriedUnderADifferentName with the reason. Gaps:\n" + string.Join("\n", gaps));
    }

    [Fact]
    public void EveryExemptionStillNamesALiveMember()
    {
        // An exemption for a member that no longer exists is dead weight that would silently cover a
        // future property of the same name.
        var stale = new List<string>();

        foreach (var key in IntentionallyNotSerialized.Keys)
        {
            var parts = key.Split('.');
            var model = PairedTypes().Select(pair => pair.Model)
                .FirstOrDefault(type => type.Name == parts[0]);

            if (model is null || !SettableMemberNames(model).Contains(parts[1]))
                stale.Add(key);
        }

        stale.Should().BeEmpty("remove exemptions whose member is gone");
    }

    /// <summary>Each nested <c>*Dto</c> of the adapter paired with the model type of the same name.</summary>
    private static List<(Type Dto, Type Model)> PairedTypes()
    {
        var modelTypes = typeof(FreeX.Core.Model.Workbook).Assembly.GetTypes()
            .Where(type => type.IsClass && type.IsPublic)
            .GroupBy(type => type.Name)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());

        return typeof(NativeJsonAdapter)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(type => type.Name.EndsWith("Dto", StringComparison.Ordinal))
            .Select(dto => (Dto: dto, Name: dto.Name[..^3]))
            .Where(pair => modelTypes.ContainsKey(pair.Name))
            .Select(pair => (pair.Dto, Model: modelTypes[pair.Name]))
            .ToList();
    }

    private static HashSet<string> SettableMemberNames(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && property.SetMethod?.IsPublic == true)
            .Select(property => property.Name)];
}
