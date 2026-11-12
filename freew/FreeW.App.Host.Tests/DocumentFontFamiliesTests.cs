using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// A face that is not installed but sits in Office's on-demand cloud-font cache (where Word fetches
/// Traditional Arabic for Arabic documents) is drawn from that cached file, as Word on the same machine
/// does, so the metrics -- and every line break -- match Word's.
/// </summary>
public sealed class DocumentFontFamiliesTests
{
    [StaFact]
    public void CloudCachedFace_LoadsFromTheCacheFolder_WithAnAbsoluteSourceThatMapsBackToItsName()
    {
        var fontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var sourceFont = Path.Combine(fontsFolder, "times.ttf");
        if (!File.Exists(sourceFont))
            return; // Windows font set without Times New Roman: nothing to copy into the fake cache.

        var root = Path.Combine(Path.GetTempPath(), "freew-cloudfonts-" + Guid.NewGuid().ToString("N"));
        var faceFolder = Path.Combine(root, "Times New Roman");
        Directory.CreateDirectory(faceFolder);
        try
        {
            File.Copy(sourceFont, Path.Combine(faceFolder, "12345.ttf"));

            var family = DocumentFontFamilies.LoadCloudFamily(root, "Times New Roman");

            Assert.NotNull(family);
            Assert.StartsWith("file:///", family!.Source, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Times New Roman", DocumentFontFamilies.ModelName(family));
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            Assert.True(typeface.TryGetGlyphTypeface(out var glyphs));
            Assert.StartsWith(faceFolder.Replace('\\', '/'), glyphs.FontUri.LocalPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

            // A cached file whose family name differs from the folder's is not borrowed.
            Assert.Null(DocumentFontFamilies.LoadCloudFamily(root, "Not Times"));
            Assert.Null(DocumentFontFamilies.LoadCloudFamily(Path.Combine(root, "missing"), "Times New Roman"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [StaFact]
    public void InstalledFace_KeepsItsPlainName()
    {
        var family = DocumentFontFamilies.Create("Arial");

        Assert.Equal("Arial", family.Source);
        Assert.Equal("Arial", DocumentFontFamilies.ModelName(family));
        Assert.True(DocumentFontFamilies.IsAvailable("Arial"));
        Assert.False(DocumentFontFamilies.IsAvailable("Definitely Not An Installed Face 7731"));
    }

    /// <summary>
    /// Word sizes a "multiple" line of right-to-left text by the face and size the text is drawn in --
    /// its w:cs font at w:szCs -- not by the run's Latin pair.
    /// </summary>
    [StaFact]
    public void MultipleLineBasis_OfRightToLeftText_UsesTheComplexScriptFaceAndSize()
    {
        var document = FreeW.Core.Model.TextDocument.CreateEmpty();
        var paragraph = new FreeW.Core.Model.Paragraph();
        paragraph.Runs.Add(new FreeW.Core.Model.Run("مقدمة الكتاب")
        {
            Formatting = new FreeW.Core.Model.RunFormatting
            {
                FontFamily = "Times New Roman",
                FontSizePt = 8,
                ComplexScriptFontFamily = "Arial",
                ComplexScriptFontSizePt = 16,
                Rtl = true,
            }
        });

        var basisPx = DocumentView.ParagraphMultipleLineBasisPx(paragraph, document);

        Assert.Equal(new FontFamily("Arial").LineSpacing * 16 * 96.0 / 72.0, basisPx, 3);
    }
}
