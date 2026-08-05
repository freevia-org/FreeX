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
        GetField<ComboBox>(dialog, "_dateFormatCombo").SelectedItem
            .Should().Be(HeaderFooterCommandPlanner.DateFormatOptions[0]);
    }

    [StaFact]
    public void HeaderFooterDialog_ThrowsOnNullEditor()
    {
        var act = () => new HeaderFooterDialog(null!, HeaderFooterCommandFocus.HeaderFooter);
        act.Should().Throw<ArgumentNullException>();
    }

    [StaFact]
    public void HeaderFooterDialog_ApplyAllCanSuppressTitleSlideThroughSharedPlanner()
    {
        var editor = MakeSession();
        editor.Presentation.Layouts.Add(new SlideLayout
        {
            Id = "content",
            Name = "Title and Content",
            LayoutType = SlideLayoutType.TitleContent,
        });
        editor.Presentation.Slides.Add(new Slide { LayoutId = "content" });
        var dialog = new HeaderFooterDialog(editor, HeaderFooterCommandFocus.HeaderFooter);

        dialog.ApplyForTests(
            showDateTime: true,
            showFooter: true,
            showSlideNumber: true,
            footerText: "Deck footer",
            scope: HeaderFooterApplyScope.AllSlides,
            suppressOnTitleSlide: true).Should().BeTrue();

        dialog.LastApplyPlan!.Options.SuppressOnTitleSlide.Should().BeTrue();
        editor.Presentation.Slides[0].HfVisibility!.ShowFooter.Should().BeFalse();
        editor.Presentation.Slides[1].HfVisibility!.ShowFooter.Should().BeTrue();
        editor.Presentation.ShowSpecialPlaceholdersOnTitleSlide.Should().BeFalse();
    }

    [StaFact]
    public void HeaderFooterDialog_ApplyForTests_ForwardsFixedDateOptions()
    {
        var editor = MakeSession();
        var dialog = new HeaderFooterDialog(editor, HeaderFooterCommandFocus.DateTime);

        dialog.ApplyForTests(
            showDateTime: true,
            showFooter: false,
            showSlideNumber: false,
            footerText: string.Empty,
            scope: HeaderFooterApplyScope.CurrentSlide,
            dateTimeMode: HeaderFooterDateTimeMode.Fixed,
            fixedDateTimeText: "Issued").Should().BeTrue();

        dialog.LastApplyPlan!.Options.DateTimeMode.Should().Be(HeaderFooterDateTimeMode.Fixed);
        var dateRun = editor.Presentation.Slides[0].Shapes
            .Single(shape => shape.Placeholder?.Type == PlaceholderType.DateTime)
            .TextBody!.Paragraphs.Single().Runs.Single();
        dateRun.Field.Should().BeNull();
        dateRun.Text.Should().Be("Issued");
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
