using System.Security.Cryptography;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentFontFamilyResolverTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData("Calibri")]
    [InlineData("calibri")]
    [InlineData(" CALIBRI ")]
    public void CalibriPrefersTheInstalledFaceBeforeTheBundledMetricFallback(string family)
    {
        var resolved = DocumentFontFamilyResolver.Resolve(family);

        resolved.FamilyNames.Should().Equal("Calibri", "Carlito");
        resolved.Key!.Source.Scheme.Should().Be("compositefont");
        resolved.Should().Be(new FontFamily("Calibri, avares://FreeW/Assets/Fonts/Carlito#Carlito"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingFamilyKeepsThePlatformDefault(string? family) =>
        DocumentFontFamilyResolver.Resolve(family).Should().Be(FontFamily.Default);

    [Theory]
    [InlineData("Arial")]
    [InlineData("Cambria")]
    [InlineData("Aptos")]
    [InlineData("Example custom font")]
    [InlineData("Calibri, Arial")]
    public void OtherAuthoredFamiliesAndExplicitChainsArePreserved(string family) =>
        DocumentFontFamilyResolver.Resolve(family).Should().Be(new FontFamily(family));

    [Theory]
    [InlineData("Regular", "f6418f708baede9789daef5d458c0f53d2a888af9820e8062934e504fedc6595")]
    [InlineData("Bold", "bb5d20f79b82599ec72983597437373a80f2d2085fa91fc144fd74e876a594db")]
    [InlineData("Italic", "0b019225e58d702bfedcbd35c21696769f8ee115cb6343f84c2f240312450d1c")]
    [InlineData("BoldItalic", "b32928186c119599e03ca6a1ffc680fdcb7fac95772f4b95d989cf6cd3861517")]
    public Task AllFourOriginalFontFilesArePackagedAsAvaloniaResources(string variant, string sha256) =>
        Session.Dispatch(() =>
        {
            using var stream = AssetLoader.Open(new Uri($"avares://FreeW/Assets/Fonts/Carlito/Carlito-{variant}.ttf"));

            Convert.ToHexStringLower(SHA256.HashData(stream)).Should().Be(sha256);
        }, CancellationToken.None);

    [Fact]
    public Task RenderingAndSavingKeepTheAuthoredCalibriName() => Session.Dispatch(() =>
    {
        var document = TextDocument.CreateEmpty();
        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.Runs.Clear();
        paragraph.Runs.Add(new Run("Calibri remains the authored font.", RunFormatting.Default with { FontFamily = "Calibri" }));
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 1200));

        paragraph.Runs.Single().Formatting.FontFamily.Should().Be("Calibri");
        using var saved = new MemoryStream();
        DocxWriter.Write(view.Document, saved);
        saved.Position = 0;
        var reopened = DocxReader.Read(saved);
        ((Paragraph)reopened.Blocks[0]).Runs.Single().Formatting.FontFamily.Should().Be("Calibri");
    }, CancellationToken.None);
}
