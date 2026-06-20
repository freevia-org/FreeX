using System.IO;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for Design &gt; Page Background &gt; Page Color: <see cref="DocumentView.SetPageColor"/> sets the
/// model's page <see cref="PageSettings.BackgroundColorHex"/> (the value that already round-trips as
/// w:background in docx), normalises the hex, clears back to the default white sheet, and the live editing
/// surface recolours its page background to match. STA because it builds the real WPF editing surface.
/// </summary>
public sealed class PageColorTests
{
    [StaFact]
    public void SetPageColor_SetsModelAndRecoloursThePageSheet()
    {
        var doc = TextDocument.CreateEmpty();
        var view = new DocumentView();
        view.LoadModel(doc);

        view.SetPageColor("#FFFFCC");

        Assert.Equal("#FFFFCC", view.Model.Page.BackgroundColorHex);
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xCC), ((SolidColorBrush)view.Background).Color);
    }

    [StaFact]
    public void SetPageColor_NormalisesHexWithoutHash()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());

        view.SetPageColor("00B050");

        Assert.Equal("#00B050", view.Model.Page.BackgroundColorHex);
    }

    [StaFact]
    public void SetPageColor_NullClearsBackToWhiteSheet()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.BackgroundColorHex = "#FF0000";
        var view = new DocumentView();
        view.LoadModel(doc);

        view.SetPageColor(null);

        Assert.Null(view.Model.Page.BackgroundColorHex);
        Assert.Equal(Colors.White, ((SolidColorBrush)view.Background).Color);
    }

    [StaFact]
    public void SetPageColor_RoundTripsThroughDocx()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        view.SetPageColor("#C0FFEE");
        view.CommitToModel();

        using var stream = new MemoryStream();
        DocxWriter.Write(view.Model, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);

        Assert.Equal("#C0FFEE", read.Page.BackgroundColorHex);
    }
}
