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

        /// <summary>
        /// RRGGBB hex string when <see cref="FontColorKind"/> is <see cref="CellRunColorKind.Rgb"/>
        /// (or absent, for files written before <see cref="FontColorKind"/> existed), or null =
        /// inherit cell style color.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FontColor { get; set; }

        /// <summary>
        /// Discriminator for how <see cref="FontColor"/> is expressed. Absent/default (Rgb) on files
        /// written before this field existed, which always meant a plain RGB (or no) color.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public CellRunColorKind FontColorKind { get; set; } = CellRunColorKind.Rgb;

        /// <summary>For <see cref="CellRunColorKind.Theme"/>: the zero-based theme-color index.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int FontColorThemeIndex { get; set; }

        /// <summary>For <see cref="CellRunColorKind.Theme"/>: luminance tint in [-1, 1].</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? FontColorTint { get; set; }

        /// <summary>For <see cref="CellRunColorKind.Indexed"/>: the zero-based OOXML indexed-color value.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int FontColorIndexedIndex { get; set; }

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

    private static CellTextRunDto ToCellTextRunDto(CellTextRun run)
    {
        var dto = new CellTextRunDto
        {
            Text         = run.Text,
            Bold         = run.Bold,
            Italic       = run.Italic,
            Underline    = run.Underline,
            Strikethrough = run.Strikethrough,
            FontName     = run.FontName,
            FontSize     = run.FontSize,
            VertAlign    = run.VertAlign,
        };

        if (run.FontColor is { } rc)
        {
            dto.FontColorKind = rc.Kind;
            switch (rc.Kind)
            {
                case CellRunColorKind.Rgb:
                    dto.FontColor = FormatDtoColor(rc.Rgb);
                    break;
                case CellRunColorKind.Theme:
                    dto.FontColorThemeIndex = rc.ThemeIndex;
                    dto.FontColorTint = rc.Tint;
                    break;
                case CellRunColorKind.Indexed:
                    dto.FontColorIndexedIndex = rc.IndexedIndex;
                    break;
                case CellRunColorKind.Auto:
                    break;
            }
        }

        return dto;
    }

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
        ToCellRunColor(dto),
        Enum.IsDefined(dto.VertAlign) ? dto.VertAlign : CellTextRunVertAlign.None);

    private static CellRunColor? ToCellRunColor(CellTextRunDto dto)
    {
        var kind = Enum.IsDefined(dto.FontColorKind) ? dto.FontColorKind : CellRunColorKind.Rgb;
        return kind switch
        {
            CellRunColorKind.Theme => CellRunColor.FromTheme(dto.FontColorThemeIndex, dto.FontColorTint ?? 0),
            CellRunColorKind.Indexed => CellRunColor.FromIndexed(dto.FontColorIndexedIndex),
            CellRunColorKind.Auto => CellRunColor.Auto(),
            _ => ParseDtoColor(dto.FontColor) is { } c ? CellRunColor.FromRgb(c) : (CellRunColor?)null,
        };
    }

    // FormatDtoColor / ParseDtoColor are shared helpers defined in NativeJsonAdapter.Sparkline.cs.
}
