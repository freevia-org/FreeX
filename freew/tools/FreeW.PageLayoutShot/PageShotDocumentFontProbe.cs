using System.Text.Json;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;

/// <summary>Opt-in diagnostics using the real Skia backend; no screenshots or font installation.</summary>
internal static class PageShotDocumentFontProbe
{
    public static int Run()
    {
        var manager = FontManager.Current;
        var hasCalibri = manager.SystemFonts.Any(font => string.Equals(font.Name, "Calibri", StringComparison.OrdinalIgnoreCase));
        var resolved = DocumentFontFamilyResolver.Resolve("Calibri");
        var expectedFamily = hasCalibri ? "Calibri" : "Carlito";
        var faces = new List<object>();
        var success = true;
        foreach (var (style, weight) in new[]
        {
            (FontStyle.Normal, FontWeight.Normal),
            (FontStyle.Normal, FontWeight.Bold),
            (FontStyle.Italic, FontWeight.Normal),
            (FontStyle.Italic, FontWeight.Bold),
        })
        {
            var found = manager.TryGetGlyphTypeface(new Typeface(resolved, style, weight), out var glyphTypeface);
            var actualFamily = glyphTypeface?.FamilyName;
            var matches = found && string.Equals(actualFamily, expectedFamily, StringComparison.OrdinalIgnoreCase)
                && glyphTypeface!.Style == style && glyphTypeface.Weight == weight
                && glyphTypeface.FontSimulations == FontSimulations.None;
            success &= matches;
            faces.Add(new
            {
                requestedStyle = style.ToString(),
                requestedWeight = weight.ToString(),
                found,
                actualFamily,
                actualStyle = glyphTypeface?.Style.ToString(),
                actualWeight = glyphTypeface?.Weight.ToString(),
                simulations = glyphTypeface?.FontSimulations.ToString(),
                matchesExpected = matches,
            });
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            backend = "avalonia-skia",
            hasInstalledCalibri = hasCalibri,
            defaultFamily = manager.DefaultFontFamily.Name,
            requestedFamily = "Calibri",
            expectedFamily,
            faces,
            success,
        }, new JsonSerializerOptions { WriteIndented = true }));
        return success ? 0 : 1;
    }
}
