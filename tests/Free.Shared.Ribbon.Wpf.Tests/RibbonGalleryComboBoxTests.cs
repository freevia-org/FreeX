using System.Windows;
using System.Runtime.ExceptionServices;
using FluentAssertions;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf.Tests;

[Trait("Category", "RibbonUiLane")]
public sealed class RibbonGalleryComboBoxTests
{
    [Fact]
    public void GalleryCombo_UsesItsOwnPopupAndKeepsTheCompactComboSelection()
    {
        RunSta(() =>
        {
            var choices = new[]
            {
                new RibbonComboBoxChoice("General", "General", "No specific format", RibbonComboBoxGalleryPreviewKind.General),
                new RibbonComboBoxChoice("number-format.more", "More Number Formats...", null, RibbonComboBoxGalleryPreviewKind.More),
            };
            var gallery = new RibbonGalleryComboBox();
            foreach (var choice in choices)
                gallery.Items.Add(choice);
            gallery.SetGalleryChoices(choices);
            gallery.SelectedIndex = 0;

            gallery.OpenGallery();

            gallery.IsGalleryOpen.Should().BeTrue();
            gallery.IsDropDownOpen.Should().BeFalse();
            gallery.GalleryPopupChild.Measure(new Size(264, double.PositiveInfinity));
            gallery.GalleryPopupChild.DesiredSize.Width.Should().BeGreaterThan(0);

            gallery.CloseGallery();
            gallery.IsGalleryOpen.Should().BeFalse();
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
