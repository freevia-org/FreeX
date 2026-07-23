using System.Text.Json.Serialization;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    // ── DTOs ─────────────────────────────────────────────────────────────────

    private class CellPhoneticGuideDto
    {
        public string? Address { get; set; }
        public List<string> RunPhoneticXmls { get; set; } = [];

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PhoneticPropertiesXml { get; set; }
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    private static List<CellPhoneticGuideDto> ToPhoneticGuideDtos(Sheet sheet)
    {
        if (sheet.CellPhoneticGuides.Count == 0)
            return [];

        var result = new List<CellPhoneticGuideDto>(sheet.CellPhoneticGuides.Count);
        foreach (var (address, guide) in sheet.CellPhoneticGuides)
        {
            if (!IsValidAddressOnSheet(address, sheet.Id))
                continue;

            result.Add(new CellPhoneticGuideDto
            {
                Address = address.ToA1(),
                RunPhoneticXmls = [.. guide.RunPhoneticXmls],
                PhoneticPropertiesXml = guide.PhoneticPropertiesXml
            });
        }

        return result;
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    private static void LoadPhoneticGuides(
        IReadOnlyList<CellPhoneticGuideDto>? dtos,
        Sheet sheet)
    {
        if (dtos is null or { Count: 0 })
            return;

        foreach (var dto in dtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Address))
                continue;

            if (!CellAddress.TryParse(dto.Address, sheet.Id, out var address))
                continue;
            if (address.Sheet != sheet.Id)
                continue;

            sheet.CellPhoneticGuides[address] = new CellPhoneticGuide(
                dto.RunPhoneticXmls ?? [],
                dto.PhoneticPropertiesXml);
        }
    }
}
