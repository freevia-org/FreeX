using System.Text.Json.Serialization;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    // ── DTOs ─────────────────────────────────────────────────────────────────

    private class RichTextRunDto
    {
        public string? Address { get; set; }
        public List<CellTextRunDto> Runs { get; set; } = [];
    }

    private class CellTextRunDto
    {
        public string Text { get; set; } = "";

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Bold { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Italic { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Underline { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Strikethrough { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FontName { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? FontSize { get; set; }

        /// <summary>RRGGBB hex string, or null = inherit cell style color.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FontColor { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public CellTextRunVertAlign VertAlign { get; set; } = CellTextRunVertAlign.None;
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    private static List<RichTextRunDto> ToRichTextRunDtos(Sheet sheet)
    {
        if (sheet.RichTextRuns.Count == 0)
            return [];

        var result = new List<RichTextRunDto>(sheet.RichTextRuns.Count);
        foreach (var (address, runs) in sheet.RichTextRuns)
        {
            if (!IsValidAddressOnSheet(address, sheet.Id))
                continue;
            if (runs is not { Count: > 0 })
                continue;

            var dto = new RichTextRunDto
            {
                Address = address.ToA1(),
                Runs = runs.Select(ToCellTextRunDto).ToList()
            };
            result.Add(dto);
        }

        return result;
    }

    private static CellTextRunDto ToCellTextRunDto(CellTextRun run) => new()
    {
        Text         = run.Text,
        Bold         = run.Bold,
        Italic       = run.Italic,
        Underline    = run.Underline,
        Strikethrough = run.Strikethrough,
        FontName     = run.FontName,
        FontSize     = run.FontSize,
        // Native JSON stores run colors as plain RGB hex (theme/indexed refs cannot round-trip
        // through JSON without workbook context; use XLSX for lossless theme-color round-trips).
        FontColor    = run.FontColor is { } rc && rc.Kind == CellRunColorKind.Rgb
                           ? FormatDtoColor(rc.Rgb)
                           : null,
        VertAlign    = run.VertAlign,
    };

    // ── Load ─────────────────────────────────────────────────────────────────

    private static void LoadRichTextRuns(
        IReadOnlyList<RichTextRunDto>? dtos,
        Sheet sheet)
    {
        if (dtos is null or { Count: 0 })
            return;

        foreach (var dto in dtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Address) || dto.Runs is not { Count: > 0 })
                continue;

            if (!CellAddress.TryParse(dto.Address, sheet.Id, out var address))
                continue;
            if (address.Sheet != sheet.Id)
                continue;

            var runs = dto.Runs
                .Select(ToCellTextRun)
                .ToList();
            if (runs.Count > 0)
                sheet.RichTextRuns[address] = runs;
        }
    }

    private static CellTextRun ToCellTextRun(CellTextRunDto dto) => new(
        dto.Text,
        dto.Bold,
        dto.Italic,
        dto.Underline,
        dto.Strikethrough,
        dto.FontName,
        dto.FontSize,
        ParseDtoColor(dto.FontColor) is { } c ? CellRunColor.FromRgb(c) : (CellRunColor?)null,
        Enum.IsDefined(dto.VertAlign) ? dto.VertAlign : CellTextRunVertAlign.None);

    // FormatDtoColor / ParseDtoColor are shared helpers defined in NativeJsonAdapter.Sparkline.cs.
}
