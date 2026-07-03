using System.Reflection;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class HeaderFooterDialogTests
{
    [StaFact]
    public void HeaderFooterDialog_ConstructsFromSharedState()
    {
        var editor = MakeSession();
        editor.Presentation.Slides[0].HfVisibility = new HfFlags
        {
            ShowDate = true,
            ShowFooter = false,
            ShowSlideNum = true,
        };

        var dialog = new HeaderFooterDialog(editor, HeaderFooterCommandFocus.HeaderFooter);

        GetField<CheckBox>(dialog, "_dateTimeCheck").IsChecked.Should().BeTrue();
        GetField<CheckBox>(dialog, "_footerCheck").IsChecked.Should().BeFalse();
        GetField<CheckBox>(dialog, "_slideNumberCheck").IsChecked.Should().BeTrue();
    }

    [StaFact]
    public void HeaderFooterDialog_ThrowsOnNullEditor()
    {
        var act = () => new HeaderFooterDialog(null!, HeaderFooterCommandFocus.HeaderFooter);
        act.Should().Throw<ArgumentNullException>();
    }

    private static EditingSession MakeSession()
    {
        var presentation = Presentation.CreateEmpty();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private static T GetField<T>(HeaderFooterDialog dialog, string fieldName)
        where T : class
    {
        var field = typeof(HeaderFooterDialog).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (T)field!.GetValue(dialog)!;
    }
}
