using System;
using System.IO;
using System.Linq;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.FormatFidelity;

internal static class ProbeFxl
{
    public static void Run(string sourcePath, Action<string> emit)
    {
        var adapters = WorkbookFileAdapterCatalog.CreateDefaultAdapters();
        var open = FileFormatResolver.FindOpenAdapter(adapters, ".xlsx", out _)!;
        Workbook src;
        using (var s = File.OpenRead(sourcePath)) src = open.Load(s);

        var fxlSave = FileFormatResolver.FindSaveAdapter(adapters, ".fxl", out _)!;
        var fxlOpen = FileFormatResolver.FindOpenAdapter(adapters, ".fxl", out _)!;
        var tmp = Path.Combine(Path.GetTempPath(), "formatfidelity", "probe.fxl");
        using (var o = File.Create(tmp)) fxlSave.Save(src, o);
        Workbook got;
        using (var i = File.OpenRead(tmp)) got = fxlOpen.Load(i);

        emit($"sheet0={src.Sheets[0].Name} occ={src.Sheets[0].GetOccupiedCellMap().Count}; "
            + $"sheet1={src.Sheets[1].Name} occ={src.Sheets[1].GetOccupiedCellMap().Count}");
        emit($"src theme minor font: {src.Theme.ResolveSchemeFontName(CellFontScheme.Minor)}");
        emit($"got theme minor font: {got.Theme.ResolveSchemeFontName(CellFontScheme.Minor)}");
        emit($"src charts/sheet: {string.Join(",", src.Sheets.Where(s => s.Charts.Count > 0).Select(s => $"{s.Name}={s.Charts.Count}"))}");
        emit($"got charts/sheet: {string.Join(",", got.Sheets.Where(s => s.Charts.Count > 0).Select(s => $"{s.Name}={s.Charts.Count}"))}");

        int shown = 0;
        foreach (var srcSheet in src.Sheets)
        {
            var gotSheet = got.Sheets.FirstOrDefault(s => s.Name == srcSheet.Name);
            if (gotSheet is null) continue;
            foreach (var ((row, col), cell) in srcSheet.GetOccupiedCellMap())
            {
                var a = src.GetStyle(cell.StyleId);
                gotSheet.GetOccupiedCellMap().TryGetValue((row, col), out var gc);
                var b = gc is null ? CellStyle.Default : got.GetStyle(gc.StyleId);
                if (a.FontName != b.FontName || Math.Abs(a.FontSize - b.FontSize) > 1e-6 ||
                    a.Bold != b.Bold || a.Italic != b.Italic || a.Underline != b.Underline ||
                    a.Strikethrough != b.Strikethrough || a.FontColor != b.FontColor || a.FontScheme != b.FontScheme)
                {
                    emit($"  {srcSheet.Name}!{FidelityCompare.ColToLetter(col)}{row}: "
                        + $"name[{a.FontName}|{b.FontName}] size[{a.FontSize}|{b.FontSize}] bold[{a.Bold}|{b.Bold}] "
                        + $"color[{a.FontColor}|{b.FontColor}] scheme[{a.FontScheme}|{b.FontScheme}] "
                        + $"themeCol[{a.FontThemeColor}|{b.FontThemeColor}]");
                    if (++shown >= 12) return;
                }
            }
        }
    }
}
