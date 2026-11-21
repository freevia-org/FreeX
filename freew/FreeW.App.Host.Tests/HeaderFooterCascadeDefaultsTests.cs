using FreeW.App.Host;
using FreeW.Core.Model;
using Xunit;
using ModelParagraph = FreeW.Core.Model.Paragraph;
using ModelRun = FreeW.Core.Model.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// A header/footer is laid out through a wrapper document; it must carry the source document's cascade-root
/// flags, or a package whose docDefaults omit w:line (single spacing) gets Word's 1.15 application default
/// in its header only (corpus WordWithAttachments: 22px header line pitch against Word's 19px).
/// </summary>
public sealed class HeaderFooterCascadeDefaultsTests
{
    private static double HeaderHeight(bool useApplicationDefaultLineSpacing)
    {
        var model = TextDocument.CreateEmpty();
        model.DefaultRun = model.DefaultRun with { FontFamily = "Times New Roman", FontSizePt = 12 };
        model.UseWordApplicationDefaultLineSpacing = useApplicationDefaultLineSpacing;
        var header = new HeaderFooter();
        for (var line = 0; line < 3; line++)
        {
            var paragraph = new ModelParagraph();
            paragraph.Runs.Add(new ModelRun("Page " + line));
            header.Paragraphs.Add(paragraph);
        }

        return HeaderFooterBandRenderer.Measure(model, header, isHeader: true, widthDip: 600, maxHeightDip: 600).HeightDip;
    }

    [StaFact]
    public void PackageAuthoredSingleSpacing_IsNotWidenedToTheApplicationDefaultInTheHeader()
    {
        var single = HeaderHeight(useApplicationDefaultLineSpacing: false);
        var applicationDefault = HeaderHeight(useApplicationDefaultLineSpacing: true);

        Assert.True(single < applicationDefault - 1, $"single {single} vs 1.15 default {applicationDefault}");
    }
}
